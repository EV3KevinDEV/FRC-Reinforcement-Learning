from __future__ import annotations

import argparse
from pathlib import Path

from stable_baselines3 import PPO
from stable_baselines3.common.vec_env import VecNormalize

from .cli import default_executable, development_executable, positive_int
from .vec_env import MoSimVecEnv


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Evaluate a MoSimulator PPO checkpoint"
    )
    parser.add_argument("checkpoint", type=Path)
    parser.add_argument("--vecnormalize", type=Path)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--episodes", type=positive_int, default=5)
    parser.add_argument("--seed", type=int, default=1000)
    parser.add_argument(
        "--action-mode", choices=("gamepad", "semantic"), default="gamepad"
    )
    parser.add_argument("--graphical", action="store_true")
    args = parser.parse_args()
    executable = args.executable or (
        development_executable() if args.graphical else default_executable()
    )

    base_env = MoSimVecEnv(
        executable,
        num_envs=1,
        base_seed=args.seed,
        curriculum_stage=4,
        automatic_curriculum=False,
        action_mode=args.action_mode,
        graphical_worker=0 if args.graphical else None,
        realtime_graphical=args.graphical,
        log_dir="runs/evaluate/unity",
    )
    env = base_env
    if args.vecnormalize:
        env = VecNormalize.load(args.vecnormalize, base_env)
        env.training = False
        env.norm_reward = False
    model = PPO.load(args.checkpoint, env=env)
    observations = env.reset()
    completed = 0
    try:
        while completed < args.episodes:
            actions, _ = model.predict(observations, deterministic=True)
            observations, _, dones, infos = env.step(actions)
            for done, info in zip(dones, infos, strict=True):
                if done:
                    completed += 1
                    score = info.get("score", {}).get("total_points", 0)
                    print(
                        f"episode={completed} score={score} reason={info.get('termination_reason')}"
                    )
    finally:
        env.close()


if __name__ == "__main__":
    main()
