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
    parser.add_argument("--print-every", type=positive_int, default=10)
    parser.add_argument("--executable", type=Path)
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

    reset_options: dict[str, Any] | None = (
        None if args.with_preload else {"scenario": "empty_start"}
    )
    env: gym.Env[np.ndarray, np.ndarray] | None = None

    try:
        # Open the controller first so a missing/disconnected device fails before
        # starting the relatively expensive Unity player.
        with PhysicalGamepad(args.controller, args.deadzone) as gamepad:
            print(f"Using controller {args.controller}: {gamepad.name}")
            print("Controls:")
            print("  Left stick     drive/strafe")
            print("  Right stick X  turn")
            print("  A/B/X/Y        select scoring level L1/L2/L3/L4")
            print("  Left trigger   intake")
            print("  Right trigger  place")
            print("  D-pad down     stow")
            print("  D-pad right    toggle ground/station source")
            print("  Back            exit this example")

            env = gym.make(
                mosim_rl.GAMEPAD_ENV_ID,
                executable_path=executable_path(args.executable, graphical=True),
                base_seed=args.seed,
                curriculum_stage=args.curriculum_stage,
                automatic_curriculum=False,
                graphical=True,
                realtime=True,
                log_dir="runs/examples/controller-driver",
            )
            observation, info = env.reset(seed=args.seed, options=reset_options)
            print(
                f"Gym environment ready: observation={observation.shape}, "
                f"action={env.action_space.shape}"
            )

            step = 0
            while args.steps == 0 or step < args.steps:
                action = gamepad.read()
                pressed = active_button_names(action)
                if "BACK" in pressed:
                    print("Back pressed; stopping.")
                    break

                observation, reward, terminated, truncated, info = env.step(action)
                if step % args.print_every == 0 or pressed or terminated or truncated:
                    semantic = np.asarray(
                        info.get("semantic_action", np.zeros(6, dtype=np.float32))
                    )
                    mechanism = info.get("mechanism", {})
                    score = info.get("score", {}).get("total_points", 0)
                    print(
                        f"step={step:5d} reward={reward:+7.3f} score={score} "
                        f"sticks={action[:4].round(2).tolist()} "
                        f"buttons={pressed} command={semantic.round(2).tolist()} "
                        f"has_coral={mechanism.get('has_coral', False)}"
                    )

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
