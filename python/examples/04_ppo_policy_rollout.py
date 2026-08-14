"""Load a Stable-Baselines3 PPO checkpoint and roll it out for full episodes."""

from __future__ import annotations

import argparse
from pathlib import Path

from stable_baselines3 import PPO
from stable_baselines3.common.vec_env import VecNormalize

from mosim_rl import MoSimVecEnv
from _common import executable_path, positive_int


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("--vecnormalize", type=Path)
    parser.add_argument("--episodes", type=positive_int, default=3)
    parser.add_argument("--seed", type=int, default=1_000)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--graphical", action="store_true")
    parser.add_argument(
        "--action-mode", choices=("gamepad", "semantic"), default="gamepad"
    )
    args = parser.parse_args()

    base_env = MoSimVecEnv(
        executable_path(args.executable, graphical=args.graphical),
        num_envs=1,
        base_seed=args.seed,
        curriculum_stage=4,
        automatic_curriculum=False,
        action_mode=args.action_mode,
        graphical_worker=0 if args.graphical else None,
        realtime_graphical=args.graphical,
        log_dir="runs/examples/policy-rollout",
    )
    env = base_env
    if args.vecnormalize is not None:
        env = VecNormalize.load(args.vecnormalize, base_env)
        env.training = False
        env.norm_reward = False

    try:
        model = PPO.load(args.checkpoint, env=env)
        observations = env.reset()
        completed = 0
        while completed < args.episodes:
            actions, _ = model.predict(observations, deterministic=True)
            observations, rewards, dones, infos = env.step(actions)
            if dones[0]:
                completed += 1
                info = infos[0]
                score = info.get("score", {}).get("total_points", 0)
                print(
                    f"episode={completed} score={score} "
                    f"reason={info.get('termination_reason')}"
                )
    finally:
        env.close()


if __name__ == "__main__":
    main()
