from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import numpy as np

from .cli import default_executable, positive_int
from .vec_env import MoSimVecEnv


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Soak-test parallel MoSimulator workers"
    )
    parser.add_argument("--executable", type=Path, default=default_executable())
    parser.add_argument("--num-envs", type=positive_int, default=8)
    parser.add_argument("--matches", type=positive_int, default=2)
    parser.add_argument("--seed", type=int, default=20_250)
    parser.add_argument("--output", type=Path, default=Path("runs/soak/results.json"))
    args = parser.parse_args()

    env = MoSimVecEnv(
        args.executable,
        num_envs=args.num_envs,
        base_seed=args.seed,
        curriculum_stage=4,
        automatic_curriculum=False,
        log_dir=args.output.parent / "unity",
    )
    completed = np.zeros(args.num_envs, dtype=np.int64)
    decisions = 0
    started = time.perf_counter()
    try:
        observations = env.reset()
        while np.any(completed < args.matches):
            if not np.isfinite(observations).all():
                raise RuntimeError("non-finite observation detected")
            actions = np.zeros((args.num_envs, 6), dtype=np.float32)
            observations, rewards, dones, infos = env.step(actions)
            if not np.isfinite(rewards).all():
                raise RuntimeError("non-finite reward detected")
            for index, (done, info) in enumerate(zip(dones, infos, strict=True)):
                if info.get("worker_id") != index:
                    raise RuntimeError(
                        f"worker contamination: slot {index} returned {info}"
                    )
                if done and info.get("termination_reason") == "match_complete":
                    completed[index] += 1
            decisions += args.num_envs
        elapsed = time.perf_counter() - started
        result = {
            "workers": args.num_envs,
            "matches_per_worker": args.matches,
            "decisions": decisions,
            "elapsed_seconds": elapsed,
            "aggregate_steps_per_second": decisions / elapsed,
            "completed": completed.tolist(),
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result, indent=2))
    finally:
        env.close()


if __name__ == "__main__":
    main()
