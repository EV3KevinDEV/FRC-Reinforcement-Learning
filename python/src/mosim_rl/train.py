from __future__ import annotations

import argparse
from datetime import datetime
from pathlib import Path

import torch
import yaml
from stable_baselines3 import PPO
from stable_baselines3.common.vec_env import VecCheckNan, VecMonitor, VecNormalize

from .cli import (
    default_executable,
    development_executable,
    positive_int,
    selected_num_envs,
)
from .tensorboard_server import TensorBoardServer
from .vec_env import MoSimVecEnv


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Train PPO against MoSimulator Reefscape"
    )
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--num-envs", type=positive_int, default=selected_num_envs())
    parser.add_argument("--total-timesteps", type=positive_int, default=100_000)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=0)
    parser.add_argument("--fixed-curriculum", action="store_true")
    parser.add_argument(
        "--action-mode",
        choices=("gamepad", "semantic"),
        default="gamepad",
        help="gamepad emits NitroGen-compatible controls; semantic preserves legacy 6D checkpoints",
    )
    parser.add_argument("--device", default="cuda")
    parser.add_argument("--run-dir", type=Path, default=Path("runs"))
    parser.add_argument(
        "--no-tensorboard",
        action="store_true",
        help="write TensorBoard metrics without starting the local web server",
    )
    parser.add_argument("--tensorboard-host", default="127.0.0.1")
    parser.add_argument("--tensorboard-port", type=positive_int, default=6006)
    parser.add_argument(
        "--graphical",
        action="store_true",
        help="show the exact stepped physics of training worker 0 in a Unity window",
    )
    parser.add_argument(
        "--wandb", action="store_true", help="sync SB3/TensorBoard metrics to W&B"
    )
    parser.add_argument("--wandb-project", default="mosim-reefscape-rl")
    parser.add_argument("--wandb-entity")
    parser.add_argument("--wandb-mode", choices=("online", "offline"), default="online")
    parser.add_argument("--wandb-model-save-freq", type=positive_int, default=50_000)
    return parser


def main() -> None:
    args = build_parser().parse_args()
    args.executable = (
        args.executable
        or (development_executable() if args.graphical else default_executable())
    ).resolve()
    if args.device.startswith("cuda") and not torch.cuda.is_available():
        raise RuntimeError(
            "CUDA training requested, but torch.cuda.is_available() is false"
        )
    run_name = datetime.now().strftime("%Y%m%d-%H%M%S")
    runs_dir = args.run_dir.resolve()
    run_dir = runs_dir / run_name
    run_dir.mkdir(parents=True, exist_ok=False)
    config = {
        "executable": str(args.executable.resolve()),
        "num_envs": args.num_envs,
        "total_timesteps": args.total_timesteps,
        "seed": args.seed,
        "curriculum_stage": args.curriculum_stage,
        "automatic_curriculum": not args.fixed_curriculum,
        "action_mode": args.action_mode,
        "device": args.device,
        "graphical_worker": 0 if args.graphical else None,
        "tensorboard": {
            "server_enabled": not args.no_tensorboard,
            "host": args.tensorboard_host,
            "port": args.tensorboard_port,
        },
        "wandb": {
            "enabled": args.wandb,
            "project": args.wandb_project,
            "entity": args.wandb_entity,
            "mode": args.wandb_mode,
        },
        "ppo": {
            "policy": "MlpPolicy",
            "net_arch": [256, 256, 128],
            "n_steps": 512,
            "batch_size": 512,
            "n_epochs": 10,
            "learning_rate": 3e-4,
            "gamma": 0.999,
            "gae_lambda": 0.95,
            "clip_range": 0.2,
            "ent_coef": 0.01,
        },
    }
    (run_dir / "config.yaml").write_text(yaml.safe_dump(config, sort_keys=False))

    base_env = MoSimVecEnv(
        args.executable,
        num_envs=args.num_envs,
        base_seed=args.seed,
        log_dir=run_dir / "unity",
        curriculum_stage=args.curriculum_stage,
        automatic_curriculum=not args.fixed_curriculum,
        action_mode=args.action_mode,
        graphical_worker=0 if args.graphical else None,
    )
    monitored = VecMonitor(base_env, filename=str(run_dir / "monitor.csv"))
    checked = VecCheckNan(monitored, raise_exception=True)
    env = VecNormalize(checked, norm_obs=False, norm_reward=True, clip_reward=10.0)
    tensorboard = TensorBoardServer(
        log_dir=runs_dir,
        host=args.tensorboard_host,
        port=args.tensorboard_port,
        output_path=run_dir / "tensorboard-server.log",
    )
    wandb_run = None
    callback = None
    try:
        if not args.no_tensorboard:
            tensorboard.start()
            print(f"TensorBoard: {tensorboard.url}", flush=True)
        if args.wandb:
            import wandb
            from wandb.integration.sb3 import WandbCallback

            wandb_run = wandb.init(
                project=args.wandb_project,
                entity=args.wandb_entity,
                name=run_name,
                dir=str(run_dir),
                config=config,
                sync_tensorboard=True,
                save_code=True,
                mode=args.wandb_mode,
                tags=[
                    "mosim",
                    "reefscape",
                    "ppo",
                    "graphical" if args.graphical else "headless",
                ],
            )
            callback = WandbCallback(
                model_save_path=str(run_dir / "wandb-models"),
                model_save_freq=max(args.wandb_model_save_freq // args.num_envs, 1),
                gradient_save_freq=0,
                verbose=1,
            )
        model = PPO(
            "MlpPolicy",
            env,
            policy_kwargs={"net_arch": [256, 256, 128]},
            n_steps=512,
            batch_size=512,
            n_epochs=10,
            learning_rate=3e-4,
            gamma=0.999,
            gae_lambda=0.95,
            clip_range=0.2,
            ent_coef=0.01,
            tensorboard_log=str(run_dir / "tensorboard"),
            seed=args.seed,
            device=args.device,
            verbose=1,
        )
        model.learn(
            total_timesteps=args.total_timesteps,
            callback=callback,
            progress_bar=True,
        )
        model.save(run_dir / "ppo_final")
        env.save(run_dir / "vecnormalize.pkl")
    finally:
        try:
            env.close()
        finally:
            try:
                if wandb_run is not None:
                    wandb_run.finish()
            finally:
                tensorboard.stop()


if __name__ == "__main__":
    main()
