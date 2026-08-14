"""Step several Unity workers through the Stable-Baselines3 VecEnv API."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np

from mosim_rl import MoSimVecEnv
from _common import executable_path, positive_int


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--num-envs", type=positive_int, default=2)
    parser.add_argument("--steps", type=positive_int, default=100)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=0)
    parser.add_argument("--executable", type=Path)
    parser.add_argument(
        "--graphical",
        action="store_true",
        help="render worker 0 and keep the remaining workers headless",
    )
    args = parser.parse_args()

    env = MoSimVecEnv(
        executable_path(args.executable, graphical=args.graphical),
        num_envs=args.num_envs,
        base_seed=args.seed,
        curriculum_stage=args.curriculum_stage,
        automatic_curriculum=False,
        action_mode="gamepad",
        graphical_worker=0 if args.graphical else None,
        log_dir="runs/examples/vectorized",
    )
    env.action_space.seed(args.seed)
    env.seed(args.seed)

    try:
        observations = env.reset()
        print(f"observations={observations.shape}")
        print(f"batched_actions=({env.num_envs}, {env.action_space.shape[0]})")
        episode_count = 0
        for step in range(args.steps):
            actions = np.stack(
                [env.action_space.sample() for _ in range(env.num_envs)]
            )
            observations, rewards, dones, infos = env.step(actions)
            episode_count += int(dones.sum())
            if step % 10 == 0 or dones.any():
                scores = [info.get("score", {}).get("total_points", 0) for info in infos]
                print(
                    f"step={step:4d} mean_reward={rewards.mean():+7.3f} "
                    f"scores={scores} completed={episode_count}"
                )
    finally:
        env.close()


if __name__ == "__main__":
    main()
