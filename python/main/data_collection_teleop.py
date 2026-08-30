"""Record Team 118 MoSim teleoperation episodes in LeRobot v3 format.

The recorder follows the same Gymnasium loop used by the examples in this
repository, then stores each transition with LeRobotDataset.add_frame.
The three Unity virtual cameras are decoded from JPEG bytes and written as
LeRobot video features. The saved state is limited to robot-local kinematics
and mechanism telemetry; field target, score, and match telemetry are omitted.

Useful references while learning this file:

* docs/ENVIRONMENT.md
* docs/VIRTUAL_CAMERAS.md
* python/examples/01_gymnasium_random_rollout.py
* python/examples/02_virtual_camera_capture.py
* python/examples/05_controller_driver_control.py
* https://huggingface.co/docs/lerobot/en/lerobot-dataset-v3
"""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import cv2
import gymnasium as gym
import numpy as np

import mosim_rl
from lerobot.datasets.lerobot_dataset import LeRobotDataset
from mosim_rl import MoSimEnv, VirtualCameraInfo
from mosim_rl.physical_gamepad import PhysicalGamepad, active_button_names


# MoSim's default frame_skip is five 20 ms control quanta, so one env.step()
# represents 0.1 seconds. LeRobot's fps must describe env.step(), not the
# Unity render rate.
NUM_EPISODES = 5
FRAME_SKIP = 5
FPS = 10
EPISODE_TIME_SEC = 60.0
RESET_TIME_SEC = 5.0
TASK_DESCRIPTION = "Teleoperate Team 118 robot for coral scoring"

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_EXECUTABLE = (
    REPOSITORY_ROOT / "_Build" / "RL" / "LinuxDevelopment" / "MoSimRL.x86_64"
)

# Keep collected datasets in the repository-level output directory. A
# dedicated child directory prevents the existing output folder itself from
# being mistaken for an already-created LeRobot dataset.
DEFAULT_DATASET_ROOT = REPOSITORY_ROOT / "output" / "mosim-teleop"
DEFAULT_REPO_ID = "local/mosim-teleop"
ROBOT_TYPE = "MoSim-Team118"

# Unity camera IDs are case-sensitive. The feature names are dataset names and
# can use the conventional LeRobot observation.images.* prefix.
CAMERA_FEATURES = {
    "LimeLightFrontLeft": "observation.images.limelight_front_left",
    "LimelightLeftBack": "observation.images.limelight_left_back",
    "LimelightRightBack": "observation.images.limelight_right_back",
}

# The 62-value MoSim observation also contains field/task/score telemetry.
# These indices retain only robot-local kinematics and mechanism state:
#
#   2:12  yaw orientation, local velocity, yaw rate, up vector, grounded,
#         and enabled
#   12:20 mechanism setpoint/angles, coral possession/state, station mode
#
# Indices 0:2 (field x/z), 20:56 (field task/target/score), and 56:62
# (previous action) are intentionally not written to the dataset.
LOCAL_STATE_INDICES = (
    2,
    3,
    4,
    5,
    6,
    7,
    8,
    9,
    10,
    11,
    12,
    13,
    14,
    15,
    16,
    17,
    18,
    19,
)
LOCAL_STATE_NAMES = (
    "robot_yaw_sin",
    "robot_yaw_cos",
    "robot_local_velocity_x",
    "robot_local_velocity_z",
    "robot_yaw_rate",
    "robot_up_x",
    "robot_up_y",
    "robot_up_z",
    "robot_grounded",
    "robot_enabled",
    "mechanism_setpoint",
    "mechanism_arm_angle",
    "mechanism_elevator_height",
    "mechanism_intake_angle",
    "mechanism_algae_arms_angle",
    "mechanism_has_coral",
    "mechanism_coral_state",
    "mechanism_station_mode",
)

# GAMEPAD_ENV_ID accepts a 25-value controller action, but info provides the
# six-value semantic command after the gamepad adapter has translated it. That
# six-value command is what the robot actually executes and what is recorded.
SEMANTIC_ACTION_NAMES = (
    "forward",
    "left_strafe",
    "ccw_yaw",
    "target_level",
    "intake_place",
    "source",
)

def positive_int(value: str) -> int:
    """Parse a strictly positive command-line integer."""

    parsed = int(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be positive")
    return parsed


def nonnegative_float(value: str) -> float:
    """Parse a non-negative command-line float."""

    parsed = float(value)
    if not np.isfinite(parsed) or parsed < 0:
        raise argparse.ArgumentTypeError("value must be finite and non-negative")
    return parsed


def parse_args() -> argparse.Namespace:
    """Build command-line options while keeping useful project defaults."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--controller", type=int, default=0)
    parser.add_argument("--deadzone", type=float, default=0.12)
    parser.add_argument("--episodes", type=positive_int, default=NUM_EPISODES)
    parser.add_argument(
        "--episode-seconds",
        type=nonnegative_float,
        default=EPISODE_TIME_SEC,
    )
    parser.add_argument(
        "--reset-delay",
        type=nonnegative_float,
        default=RESET_TIME_SEC,
        help="seconds to wait between episodes",
    )
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=4)
    parser.add_argument("--executable", type=Path, default=DEFAULT_EXECUTABLE)
    parser.add_argument("--dataset-root", type=Path, default=DEFAULT_DATASET_ROOT)
    parser.add_argument("--repo-id", default=DEFAULT_REPO_ID)
    parser.add_argument("--task", default=TASK_DESCRIPTION)
    parser.add_argument(
        "--with-preload",
        action="store_true",
        help="retain the official preloaded coral instead of an empty start",
    )
    return parser.parse_args()


def decode_camera(
    sim: MoSimEnv,
    camera_name: str,
    camera: VirtualCameraInfo,
) -> np.ndarray:
    """Request one Unity JPEG and return an RGB HWC uint8 image."""

    # Camera requests do not advance physics. They must be made before the
    # following env.step(), because MoSim rejects requests during a pending step.
    packet = sim.get_virtual_camera_frame(camera_name, jpeg_quality=90)
    encoded = np.frombuffer(packet.image_bytes, dtype=np.uint8)
    bgr = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if bgr is None:
        raise RuntimeError(f"OpenCV could not decode camera {camera_name!r}")

    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    expected_shape = (camera.height, camera.width, 3)
    if rgb.shape != expected_shape:
        raise RuntimeError(
            f"Camera {camera_name!r} returned {rgb.shape}; expected {expected_shape}"
        )
    return rgb


def capture_images(
    sim: MoSimEnv,
    cameras: dict[str, VirtualCameraInfo],
) -> dict[str, np.ndarray]:
    """Capture all configured cameras using their LeRobot feature names."""

    return {
        feature_name: decode_camera(sim, camera_name, cameras[camera_name])
        for camera_name, feature_name in CAMERA_FEATURES.items()
    }


def extract_local_state(observation: np.ndarray) -> np.ndarray:
    """Select robot-local values from MoSim's normalized 62-value state."""

    full_state = np.asarray(observation, dtype=np.float32)
    if full_state.shape != (62,):
        raise ValueError(
            f"MoSim state has shape {full_state.shape}; expected (62,)"
        )
    return full_state[list(LOCAL_STATE_INDICES)].copy()


def build_features(
    cameras: dict[str, VirtualCameraInfo],
) -> dict[str, dict[str, object]]:
    """Create a LeRobot schema containing only local robot data and cameras."""

    # Shapes are tuples because LeRobot validates NumPy shapes against tuples.
    features: dict[str, dict[str, object]] = {
        "observation.state": {
            "dtype": "float32",
            "shape": (len(LOCAL_STATE_INDICES),),
            "names": list(LOCAL_STATE_NAMES),
        },
        "action": {
            "dtype": "float32",
            "shape": (len(SEMANTIC_ACTION_NAMES),),
            "names": list(SEMANTIC_ACTION_NAMES),
        },
    }

    for camera_name, feature_name in CAMERA_FEATURES.items():
        camera = cameras[camera_name]
        features[feature_name] = {
            "dtype": "video",
            # LeRobot describes images as C,H,W, while add_frame accepts the
            # H,W,C RGB array returned by decode_camera().
            "shape": (3, camera.height, camera.width),
            "names": ["channels", "height", "width"],
        }

    return features


def main() -> None:
    args = parse_args()
    if args.controller < 0:
        raise SystemExit("--controller must be non-negative")
    if not 0 <= args.deadzone < 1:
        raise SystemExit("--deadzone must be in [0, 1)")
    if not args.task.strip():
        raise SystemExit("--task must be a non-empty description")

    executable = args.executable.expanduser().resolve()
    if not executable.is_file():
        raise FileNotFoundError(
            f"Unity graphical player not found at {executable}. Build it with "
            "scripts/build_unity.sh development or pass --executable PATH."
        )

    dataset_root = args.dataset_root.expanduser().resolve()
    if dataset_root.exists():
        raise FileExistsError(
            f"Dataset directory already exists: {dataset_root}. "
            "Choose a new --dataset-root so an existing dataset is not overwritten."
        )

    reset_options = None if args.with_preload else {"scenario": "empty_start"}
    env: gym.Env | None = None
    dataset: LeRobotDataset | None = None

    try:
        # PhysicalGamepad.read() returns the 25-value action expected by the
        # registered GAMEPAD_ENV_ID environment.
        with PhysicalGamepad(args.controller, args.deadzone) as gamepad:
            print(f"Using controller {args.controller}: {gamepad.name}")

            env = gym.make(
                mosim_rl.GAMEPAD_ENV_ID,
                executable_path=executable,
                base_seed=args.seed,
                curriculum_stage=args.curriculum_stage,
                frame_skip=FRAME_SKIP,
                automatic_curriculum=False,
                graphical=True,
                realtime=True,
                log_dir="runs/examples/data-collection-teleop",
            )
            sim = env.unwrapped
            observation, _ = env.reset(seed=args.seed, options=reset_options)

            # Discover the camera metadata from the running graphical player so
            # resolution and feature shapes cannot silently drift from Unity.
            cameras = {
                camera.name: camera for camera in sim.list_virtual_cameras()
            }
            
            missing = sorted(set(CAMERA_FEATURES) - set(cameras))
            if missing:
                available = ", ".join(sorted(cameras)) or "<none>"
                raise RuntimeError(
                    f"Missing configured camera(s): {', '.join(missing)}; "
                    f"available cameras: {available}. Rebuild the graphical player "
                    "after changing the prefab."
                )

            features = build_features(cameras)

            dataset = LeRobotDataset.create(
                repo_id=args.repo_id,
                root=dataset_root,
                fps=FPS,
                features=features,
                robot_type=ROBOT_TYPE,
                use_videos=True,
                image_writer_processes=0,
                image_writer_threads=4,
            )

            max_steps = round(args.episode_seconds * FPS)
            print(
                f"Recording {args.episodes} episode(s), up to {max_steps} steps each "
                f"at {FPS} FPS into {dataset_root}"
            )

            stop_requested = False
            for episode_index in range(args.episodes):
                if episode_index > 0:
                    if args.reset_delay:
                        time.sleep(args.reset_delay)
                    observation, _ = env.reset(
                        seed=args.seed + episode_index,
                        options=reset_options,
                    )

                for _ in range(max_steps):
                    # The frame stores s_t and the camera view at s_t. The
                    # semantic action below describes the transition s_t -> s_t+1.
                    state_t = extract_local_state(observation)
                    images_t = capture_images(sim, cameras)
                    action_t = np.asarray(gamepad.read(), dtype=np.float32)

                    if "BACK" in active_button_names(action_t):
                        stop_requested = True
                        break

                    next_observation, _reward, terminated, truncated, info = env.step(
                        action_t
                    )
                    done = terminated or truncated
                    semantic_action = np.asarray(
                        info.get("semantic_action"), dtype=np.float32
                    )
                    if semantic_action.shape != (len(SEMANTIC_ACTION_NAMES),):
                        raise RuntimeError(
                            "MoSim did not return the six-value semantic action "
                            f"(got shape {semantic_action.shape})"
                        )

                    dataset.add_frame(
                        {
                            "observation.state": state_t,
                            "action": semantic_action,
                            "task": args.task,
                            **images_t,
                        }
                    )
                    observation = next_observation

                    if done:
                        print(
                            f"Episode {episode_index} ended: "
                            f"{info.get('termination_reason', 'environment done')}"
                        )
                        break

                if dataset.episode_buffer["size"] > 0:
                    dataset.save_episode()
                    print(f"Saved episode {episode_index}")

                if stop_requested:
                    print("Back pressed; stopping after saving the current episode.")
                    break

    except KeyboardInterrupt:
        print("Recording interrupted.")
        if dataset is not None and dataset.episode_buffer["size"] > 0:
            dataset.save_episode()
            print("Saved the partially recorded episode.")
    finally:
        try:
            if dataset is not None:
                dataset.stop_image_writer()
                dataset.finalize()
                print(f"Finalized dataset at {dataset.root}")
        finally:
            if env is not None:
                env.close()


if __name__ == "__main__":
    main()
