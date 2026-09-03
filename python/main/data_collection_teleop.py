"""Record Team 118 MoSim teleoperation episodes in LeRobot v3 format.

The recorder follows the same Gymnasium loop used by the examples in this
repository, then stores each transition with LeRobotDataset.add_frame.
The three Unity virtual cameras are decoded from JPEG bytes and written as
LeRobot video features. The saved state is limited to robot-local kinematics
and mechanism telemetry; field target, score, and match telemetry are omitted.
Each frame stores the complete 25-value physical-controller action plus the
six-value semantic command actually executed by Team 118.

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
import os
import time
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any

import cv2
import gymnasium as gym
import numpy as np

import mosim_rl
from lerobot.datasets.lerobot_dataset import LeRobotDataset
from mosim_rl import MoSimEnv, VirtualCameraFrame, VirtualCameraInfo
from mosim_rl.constants import GAMEPAD_ACTION_DIM, NITROGEN_BUTTONS
from mosim_rl.realtime_gamepad import RealtimeGamepadController


# Driving and dataset sampling are intentionally independent. Commands use the
# standard FRC 20 ms cadence, while video/state/action samples use a sustainable
# fixed rate. Unity captures each sample atomically, so LeRobot timestamps match
# the actual sample cadence instead of pretending camera encoding runs at 50 Hz.
NUM_EPISODES = 20
FRAME_SKIP = 1
CONTROL_HZ = 50
FPS = 8
CAMERA_JPEG_QUALITY = 90
FRC_MATCH_TIME_SEC = 150.0
AUTO_TO_TELEOP_TRANSITION_SEC = 3.0
FINAL_SCORING_GRACE_SEC = 3.0
EPISODE_TIME_SEC = (
    FRC_MATCH_TIME_SEC
    + AUTO_TO_TELEOP_TRANSITION_SEC
    + FINAL_SCORING_GRACE_SEC
)
RESET_TIME_SEC = 0.01
TASK_DESCRIPTION = "Teleoperate Team 118 for Reefscape coral, algae, and endgame"

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_EXECUTABLE = (
    REPOSITORY_ROOT / "_Build" / "RL" / "WindowsDevelopment" / "MoSimRL.exe"
    if os.name == "nt"
    else REPOSITORY_ROOT
    / "_Build"
    / "RL"
    / "LinuxDevelopment"
    / "MoSimRL.x86_64"
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

# ``action`` is the operator command used for behavioral cloning. It retains
# every NitroGen-compatible gamepad channel, including mode toggles, auto-align,
# climb, camera flip, and recording controls. Four synthetic right-stick
# direction channels remain in the fixed layout for NitroGen compatibility.
GAMEPAD_ACTION_NAMES = (
    "left_stick_x",
    "left_stick_y",
    "right_stick_x",
    "right_stick_y",
    *(name.lower() for name in NITROGEN_BUTTONS),
)

# ``action.semantic`` is the translated command that Team 118 executes. It is
# useful for inspection, but ``action`` remains the complete policy target.
SEMANTIC_ACTION_NAMES = (
    "forward",
    "left_strafe",
    "ccw_yaw",
    "target_setpoint",
    "manipulator_intent",
    "station_source_mode",
)

if len(GAMEPAD_ACTION_NAMES) != GAMEPAD_ACTION_DIM:
    raise RuntimeError("gamepad dataset names do not match the 25-value action")


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


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Build command-line options while keeping useful project defaults."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--controller", type=int, default=0)
    parser.add_argument("--deadzone", type=float, default=0.12)
    parser.add_argument("--episodes", type=positive_int, default=NUM_EPISODES)
    parser.add_argument(
        "--dataset-fps",
        type=positive_int,
        default=FPS,
        help="synchronized video/state/action samples per second",
    )
    parser.add_argument(
        "--episode-seconds",
        type=nonnegative_float,
        default=EPISODE_TIME_SEC,
        help="per-episode safety limit; default covers a complete Reefscape match",
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
        "--windowed-fullscreen",
        action="store_true",
        help="launch Unity as a borderless desktop-sized fullscreen window",
    )
    parser.add_argument(
        "--camera-mode",
        choices=("field", "robot", "third-person", "driver-station"),
        default="field",
        help=(
            "operator presentation camera; field is the fixed-style default "
            "and does not replace the three recorded robot cameras"
        ),
    )
    parser.add_argument(
        "--drive-mode",
        choices=("robot", "field"),
        default="field",
        help="field-oriented (default) or robot-relative translation",
    )
    parser.add_argument(
        "--with-preload",
        action="store_true",
        help="retain the official preloaded coral instead of an empty start",
    )
    return parser.parse_args(argv)


def build_reset_options(args: argparse.Namespace) -> dict[str, Any]:
    """Apply the operator camera and drive frame on every episode reset."""

    options: dict[str, Any] = {
        "camera_mode": args.camera_mode,
        "drive_mode": args.drive_mode,
    }
    if not args.with_preload:
        options["scenario"] = "empty_start"
    return options


def print_control_layout(args: argparse.Namespace) -> None:
    """Print the complete controller contract used by the recorded action."""

    print("Controls:")
    display_mode = (
        "windowed fullscreen" if args.windowed_fullscreen else "1280x720 window"
    )
    print(f"  Display        {display_mode}")
    print(f"  Control rate   {CONTROL_HZ} Hz ({1000 // CONTROL_HZ} ms)")
    print(f"  Dataset rate   {args.dataset_fps} synchronized FPS")
    print(f"  Left stick     drive/strafe ({args.drive_mode}-oriented)")
    print(f"  Camera         {args.camera_mode}")
    print("  Right stick X  turn")
    print("  Coral A/B/X/Y  L1 / L2 / L3 / L4")
    print("  Algae B/X      low/high reef pickup")
    print("  Algae A        stack pickup when empty; processor when holding algae")
    print("  Algae Y        barge/net when holding algae")
    print("  Left trigger   hold intake/roller (including at B/X algae pickup)")
    print("  Right trigger  score/place at the selected position")
    print("  D-pad down     stow")
    print("  D-pad up       toggle coral/algae mode")
    print("  D-pad left     toggle normal/L1 intake mode")
    print("  LB/RB          hold auto-align left/right")
    print("  Left stick     click to cycle climb state")
    print("  Right stick    click to flip robot camera")
    print("  Start          save this episode early and reset")
    print("  Back           save this episode and stop recording")


def buffered_frame_count(dataset: LeRobotDataset) -> int:
    """Return zero before LeRobot has allocated its first episode buffer."""

    buffer = dataset.episode_buffer
    if not isinstance(buffer, Mapping):
        return 0
    return int(buffer.get("size", 0))


def decode_camera(
    packet: VirtualCameraFrame,
    camera: VirtualCameraInfo,
) -> np.ndarray:
    """Decode one synchronized Unity JPEG into an RGB HWC uint8 image."""

    encoded = np.frombuffer(packet.image_bytes, dtype=np.uint8)
    bgr = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if bgr is None:
        raise RuntimeError(f"OpenCV could not decode camera {packet.name!r}")

    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
    expected_shape = (camera.height, camera.width, 3)
    if rgb.shape != expected_shape:
        raise RuntimeError(
            f"Camera {packet.name!r} returned {rgb.shape}; expected {expected_shape}"
        )
    return rgb


def capture_images(
    frames: dict[str, VirtualCameraFrame],
    cameras: dict[str, VirtualCameraInfo],
) -> dict[str, np.ndarray]:
    """Decode an atomic camera batch using its LeRobot feature names."""

    return {
        feature_name: decode_camera(frames[camera_name], cameras[camera_name])
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
    """Create a LeRobot schema with local state, all controls, and cameras."""

    # Shapes are tuples because LeRobot validates NumPy shapes against tuples.
    features: dict[str, dict[str, object]] = {
        "observation.state": {
            "dtype": "float32",
            "shape": (len(LOCAL_STATE_INDICES),),
            "names": list(LOCAL_STATE_NAMES),
        },
        "action": {
            "dtype": "float32",
            "shape": (GAMEPAD_ACTION_DIM,),
            "names": list(GAMEPAD_ACTION_NAMES),
        },
        "action.semantic": {
            "dtype": "float32",
            "shape": (len(SEMANTIC_ACTION_NAMES),),
            "names": list(SEMANTIC_ACTION_NAMES),
        },
        "metadata.sample": {
            "dtype": "float32",
            "shape": (2,),
            "names": ["sim_time_seconds", "control_udp_sequence"],
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
    max_steps = round(args.episode_seconds * args.dataset_fps)
    if max_steps < 1:
        raise SystemExit(
            "--episode-seconds must produce at least one frame at "
            f"{args.dataset_fps} FPS"
        )

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

    reset_options = build_reset_options(args)
    env: gym.Env | None = None
    dataset: LeRobotDataset | None = None
    gamepad: RealtimeGamepadController | None = None

    try:
        env = gym.make(
            mosim_rl.GAMEPAD_ENV_ID,
            executable_path=executable,
            base_seed=args.seed,
            curriculum_stage=args.curriculum_stage,
            frame_skip=FRAME_SKIP,
            automatic_curriculum=False,
            graphical=True,
            realtime=True,
            windowed_fullscreen=args.windowed_fullscreen,
            log_dir="runs/examples/data-collection-teleop",
        )
        sim = env.unwrapped
        observation, _ = env.reset(seed=args.seed, options=reset_options)

        if not sim.capabilities.get("realtime_control_api", False):
            raise RuntimeError(
                "The Unity player does not support responsive realtime control. "
                "Rebuild _Build/RL/WindowsDevelopment before collecting data."
            )

        # Discover camera metadata from the running graphical player so feature
        # shapes cannot silently drift from Unity.
        cameras = {camera.name: camera for camera in sim.list_virtual_cameras()}
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
            fps=args.dataset_fps,
            features=features,
            robot_type=ROBOT_TYPE,
            use_videos=True,
            image_writer_processes=0,
            image_writer_threads=4,
        )

        control_port = int(sim.capabilities.get("realtime_control_port", sim.port))
        gamepad = RealtimeGamepadController(
            sim.host,
            control_port,
            index=args.controller,
            deadzone=args.deadzone,
            control_hz=CONTROL_HZ,
        ).open()
        print(f"Using controller {args.controller}: {gamepad.name}")
        print_control_layout(args)
        print(
            f"Recording {args.episodes} episode(s), up to {max_steps} frames each "
            f"at {args.dataset_fps} synchronized FPS into {dataset_root}\n"
            f"Dataset actions: action=applied gamepad {GAMEPAD_ACTION_DIM}D, "
            f"action.semantic={len(SEMANTIC_ACTION_NAMES)}D; "
            f"camera={args.camera_mode}, drive={args.drive_mode}"
        )

        stop_requested = False
        episode_index = 0
        needs_reset = False
        camera_names = tuple(CAMERA_FEATURES)
        sample_period = 1.0 / args.dataset_fps
        while episode_index < args.episodes:
            if needs_reset:
                if args.reset_delay:
                    time.sleep(args.reset_delay)
                observation, _ = env.reset(
                    seed=args.seed + episode_index,
                    options=reset_options,
                )
                gamepad.reset()

            reset_requested = False
            next_sample_at = time.monotonic()
            for _ in range(max_steps):
                wait_seconds = next_sample_at - time.monotonic()
                if wait_seconds > 0:
                    time.sleep(wait_seconds)

                rising = gamepad.consume_rising_buttons()
                if "BACK" in rising:
                    stop_requested = True
                    break
                if "START" in rising:
                    reset_requested = True
                    break

                requested = gamepad.snapshot()
                observation, _reward, terminated, truncated, info = (
                    sim.step_realtime_control(
                        requested.gamepad_action,
                        requested.semantic_action,
                        camera_names=camera_names,
                        jpeg_quality=CAMERA_JPEG_QUALITY,
                    )
                )
                done = terminated or truncated
                if not info.get("sample_synchronized", False):
                    raise RuntimeError(
                        f"Unity did not return a synchronized sample: {info.get('error')}"
                    )
                if info.get("control_session") != requested.session:
                    raise RuntimeError("Unity returned a controller sample from another session")

                # Unity returns the exact command that was active when it sampled
                # state and rendered all cameras. It may be newer than `requested`
                # because the independent 50 Hz stream keeps driving while Python
                # prepares the TCP request.
                semantic_action = np.asarray(
                    info.get("semantic_action"), dtype=np.float32
                )
                gamepad_action = np.asarray(
                    info.get("gamepad_action"), dtype=np.float32
                )
                frames = info.get("camera_frames")
                if semantic_action.shape != (len(SEMANTIC_ACTION_NAMES),):
                    raise RuntimeError(
                        "MoSim did not return the applied six-value semantic action "
                        f"(got shape {semantic_action.shape})"
                    )
                if gamepad_action.shape != (GAMEPAD_ACTION_DIM,):
                    raise RuntimeError(
                        "MoSim did not return the applied 25-value gamepad action "
                        f"(got shape {gamepad_action.shape})"
                    )
                if not isinstance(frames, dict) or set(frames) != set(camera_names):
                    raise RuntimeError("MoSim returned an incomplete synchronized camera batch")

                dataset.add_frame(
                    {
                        # This state, these three images, and both actions came
                        # from one Unity update and share one simulator timestamp.
                        "observation.state": extract_local_state(observation),
                        "action": gamepad_action,
                        "action.semantic": semantic_action,
                        "metadata.sample": np.asarray(
                            [info["sim_time"], info["control_sequence"]],
                            dtype=np.float32,
                        ),
                        "task": args.task,
                        **capture_images(frames, cameras),
                    }
                )

                next_sample_at += sample_period
                # Keep the long-run video cadence at the declared FPS after a
                # small one-frame overrun. Drop backlog only when more than a
                # complete sample period was missed.
                if time.monotonic() - next_sample_at > sample_period:
                    next_sample_at = time.monotonic()

                if done:
                    print(
                        f"Episode {episode_index} ended: "
                        f"{info.get('termination_reason', 'environment done')}"
                    )
                    break

            episode_saved = buffered_frame_count(dataset) > 0
            if episode_saved:
                dataset.save_episode()
                print(f"Saved episode {episode_index}")
                episode_index += 1

            if stop_requested:
                print("Back pressed; stopping after saving the current episode.")
                break
            if reset_requested:
                message = "saved the current episode and " if episode_saved else ""
                print(f"Start pressed; {message}resetting.")
            needs_reset = True

    except KeyboardInterrupt:
        print("Recording interrupted.")
        if dataset is not None and buffered_frame_count(dataset) > 0:
            dataset.save_episode()
            print("Saved the partially recorded episode.")
    finally:
        try:
            if gamepad is not None:
                gamepad.close()
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
