from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from gymnasium.utils.env_checker import check_env

from .cli import default_executable, development_executable, positive_int
from .env import MoSimEnv
from .physical_gamepad import active_button_names
from .random_gamepad import RandomGamepadActor


def main() -> None:
    parser = argparse.ArgumentParser(description="Run a random MoSimulator rollout")
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--steps", type=positive_int, default=2_000)
    parser.add_argument(
        "--episodes", type=int, default=0, help="0 uses only the --steps bound"
    )
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=0)
    parser.add_argument("--print-every", type=positive_int, default=100)
    parser.add_argument("--check-env", action="store_true")
    parser.add_argument(
        "--action-mode", choices=("gamepad", "semantic"), default="gamepad"
    )
    parser.add_argument(
        "--graphical",
        action="store_true",
        help="open a real-time third-person Unity window",
    )
    args = parser.parse_args()
    if args.episodes < 0:
        parser.error("--episodes must be non-negative")
    executable = args.executable or (
        development_executable() if args.graphical else default_executable()
    )
    env = MoSimEnv(
        executable,
        base_seed=args.seed,
        curriculum_stage=args.curriculum_stage,
        automatic_curriculum=False,
        action_mode=args.action_mode,
        graphical=args.graphical,
        realtime=args.graphical,
    )
    gamepad_actor = RandomGamepadActor(args.seed)
    try:
        if args.check_env:
            check_env(env, skip_render_check=True)
        observation, info = env.reset(seed=args.seed)
        completed = 0
        for step in range(args.steps):
            action = (
                gamepad_actor.sample()
                if args.action_mode == "gamepad"
                else env.action_space.sample()
            )
            observation, reward, terminated, truncated, info = env.step(action)
            if step % args.print_every == 0:
                suffix = (
                    f" buttons={active_button_names(action)}"
                    if args.action_mode == "gamepad"
                    else ""
                )
                print(
                    f"step={step} reward={reward:+.3f} sim={info.get('sim_time', 0):.1f} "
                    f"score={info.get('score', {}).get('total_points', 0)} "
                    f"command={np.asarray(info.get('semantic_action', action)).round(2).tolist()}"
                    f"{suffix}"
                )
            if terminated or truncated:
                completed += 1
                print(
                    f"episode={completed} step={step} reward={reward:.3f} "
                    f"reason={info.get('termination_reason')} score={info.get('score', {})}"
                )
                if args.episodes and completed >= args.episodes:
                    break
                observation, info = env.reset()
                gamepad_actor.reset(args.seed + completed)
        if args.episodes and completed < args.episodes:
            print(
                f"stopped at --steps={args.steps} after {completed}/{args.episodes} episodes"
            )
    finally:
        env.close()


if __name__ == "__main__":
    main()
