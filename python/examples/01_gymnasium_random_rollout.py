"""Create the registered Gymnasium environment and run random actions."""

from __future__ import annotations

import argparse
from pathlib import Path

import gymnasium as gym

import mosim_rl
from _common import executable_path, positive_int


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steps", type=positive_int, default=100)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=0)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--graphical", action="store_true")
    parser.add_argument(
        "--semantic",
        action="store_true",
        help="use the legacy six-value action instead of the recommended gamepad action",
    )
    args = parser.parse_args()

    env_id = mosim_rl.ENV_ID if args.semantic else mosim_rl.GAMEPAD_ENV_ID
    executable = executable_path(args.executable, graphical=args.graphical)
    env = gym.make(
        env_id,
        executable_path=executable,
        base_seed=args.seed,
        curriculum_stage=args.curriculum_stage,
        automatic_curriculum=False,
        graphical=args.graphical,
        realtime=args.graphical,
        log_dir="runs/examples/random-rollout",
    )
    env.action_space.seed(args.seed)

    try:
        observation, info = env.reset(seed=args.seed)
        print(f"environment={env_id}")
        print(f"observation_space={env.observation_space}")
        print(f"action_space={env.action_space}")
        print(f"initial_observation_shape={observation.shape}")

        for step in range(args.steps):
            action = env.action_space.sample()
            observation, reward, terminated, truncated, info = env.step(action)
            if step % 10 == 0 or terminated or truncated:
                score = info.get("score", {}).get("total_points", 0)
                print(
                    f"step={step:4d} reward={reward:+7.3f} "
                    f"sim={info.get('sim_time', 0.0):6.2f} score={score}"
                )
            if terminated or truncated:
                print(f"episode ended: {info.get('termination_reason')}")
                observation, info = env.reset()
    finally:
        env.close()


if __name__ == "__main__":
    main()
