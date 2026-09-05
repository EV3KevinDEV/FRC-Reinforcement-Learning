"""Drive the graphical Gymnasium environment with a physical controller."""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

import gymnasium as gym
import numpy as np

import mosim_rl
from mosim_rl.physical_gamepad import PhysicalGamepad, active_button_names
from _common import executable_path, positive_int

CONTROL_FRAME_SKIP = 1
DEFAULT_PRINT_EVERY = 50


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--controller", type=int, default=0)
    parser.add_argument("--deadzone", type=float, default=0.12)
    parser.add_argument(
        "--steps",
        type=int,
        default=0,
        help="number of control steps; 0 runs until Back, Ctrl-C, or window close",
    )
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=4)
    parser.add_argument(
        "--print-every",
        type=positive_int,
        default=DEFAULT_PRINT_EVERY,
        help="periodic status interval in 50 Hz control steps",
    )
    parser.add_argument("--executable", type=Path)
    parser.add_argument(
        "--windowed-fullscreen",
        action="store_true",
        help="launch Unity as a borderless desktop-sized fullscreen window",
    )
    parser.add_argument(
        "--camera-mode",
        choices=("field", "robot", "third-person", "driver-station"),
        default="field",
        help="field is the fixed-style third-person view; robot follows robot heading",
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
        help="start with the official preloaded coral instead of an empty robot",
    )
    args = parser.parse_args()
    if args.controller < 0:
        parser.error("--controller must be non-negative")
    if not 0 <= args.deadzone < 1:
        parser.error("--deadzone must be in [0, 1)")
    if args.steps < 0:
        parser.error("--steps must be non-negative")

    reset_options: dict[str, Any] = {
        "camera_mode": args.camera_mode,
        "drive_mode": args.drive_mode,
    }
    if not args.with_preload:
        reset_options["scenario"] = "empty_start"
    env: gym.Env[np.ndarray, np.ndarray] | None = None

    try:
        # Open the controller first so a missing/disconnected device fails before
        # starting the relatively expensive Unity player.
        with PhysicalGamepad(args.controller, args.deadzone) as gamepad:
            print(f"Using controller {args.controller}: {gamepad.name}")
            print("Controls:")
            display_mode = (
                "windowed fullscreen"
                if args.windowed_fullscreen
                else "1280x720 window"
            )
            print(f"  Display        {display_mode}")
            print("  Control rate   50 Hz (20 ms)")
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
            print("  LB/RB           hold auto-align left/right")
            print("  Left stick     click unbound (climber disabled)")
            print("  Right stick    click to flip robot camera")
            print("  Start           reset the episode")
            print("  Back            exit this example")

            env = gym.make(
                mosim_rl.GAMEPAD_ENV_ID,
                executable_path=executable_path(args.executable, graphical=True),
                base_seed=args.seed,
                curriculum_stage=args.curriculum_stage,
                frame_skip=CONTROL_FRAME_SKIP,
                automatic_curriculum=False,
                graphical=True,
                realtime=True,
                windowed_fullscreen=args.windowed_fullscreen,
                log_dir="runs/examples/controller-driver",
            )
            observation, info = env.reset(seed=args.seed, options=reset_options)
            print(
                f"Gym environment ready: observation={observation.shape}, "
                f"action={env.action_space.shape}"
            )

            step = 0
            previous_pressed: set[str] = set()
            while args.steps == 0 or step < args.steps:
                action = gamepad.read()
                pressed = set(active_button_names(action))
                rising = pressed - previous_pressed
                if "BACK" in pressed:
                    print("Back pressed; stopping.")
                    break
                if "START" in rising:
                    print("Start pressed; resetting the episode.")
                    observation, info = env.reset(options=reset_options)
                    previous_pressed = pressed
                    continue

                observation, reward, terminated, truncated, info = env.step(action)
                buttons_changed = pressed != previous_pressed
                if (
                    step % args.print_every == 0
                    or buttons_changed
                    or terminated
                    or truncated
                ):
                    semantic = np.asarray(
                        info.get("semantic_action", np.zeros(6, dtype=np.float32))
                    )
                    mechanism = info.get("mechanism", {})
                    score = info.get("score", {}).get("total_points", 0)
                    print(
                        f"step={step:5d} reward={reward:+7.3f} score={score} "
                        f"sticks={action[:4].round(2).tolist()} "
                        f"buttons={sorted(pressed)} command={semantic.round(2).tolist()} "
                        f"has_coral={mechanism.get('has_coral', False)}"
                    )

                previous_pressed = pressed
                step += 1
                if terminated or truncated:
                    print(f"episode ended: {info.get('termination_reason')}; resetting")
                    observation, info = env.reset(options=reset_options)
    except KeyboardInterrupt:
        print("Controller example stopped.")
    finally:
        if env is not None:
            env.close()


if __name__ == "__main__":
    main()
