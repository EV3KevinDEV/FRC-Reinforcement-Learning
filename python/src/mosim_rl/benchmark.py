from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

import numpy as np
import yaml

from .cli import default_executable, positive_int
from .vec_env import MoSimVecEnv


def benchmark(
    executable: Path, count: int, decisions: int, seed: int
) -> dict[str, float | int]:
    env = MoSimVecEnv(
        executable,
        num_envs=count,
        base_seed=seed,
        curriculum_stage=4,
        automatic_curriculum=False,
        log_dir=Path("runs/benchmark") / f"workers-{count}",
    )
    try:
        env.reset()
        actions = np.zeros((count, 6), dtype=np.float32)
        started = time.perf_counter()
        for _ in range(decisions):
            env.step(actions)
        elapsed = time.perf_counter() - started
        total_steps = count * decisions
        rss = sum(
            worker.rss_bytes for worker in env.get_attr("unity_process") if worker
        )
        return {
            "workers": count,
            "elapsed_seconds": elapsed,
            "aggregate_steps_per_second": total_steps / elapsed,
            "rss_gib": rss / 1024**3,
        }
    finally:
        env.close()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Benchmark parallel Unity worker counts"
    )
    parser.add_argument("--executable", type=Path, default=default_executable())
    parser.add_argument("--workers", type=int, nargs="+", default=[1, 2, 4, 8, 12, 16])
    parser.add_argument("--decisions", type=positive_int, default=100)
    parser.add_argument("--seed", type=int, default=10_000)
    parser.add_argument(
        "--output", type=Path, default=Path("runs/benchmark/results.json")
    )
    args = parser.parse_args()
    results: list[dict[str, object]] = []
    for count in args.workers:
        try:
            results.append(benchmark(args.executable, count, args.decisions, args.seed))
        except Exception as exc:
            results.append({"workers": count, "error": str(exc)})
    successful = [
        result for result in results if "aggregate_steps_per_second" in result
    ]
    if not successful:
        raise RuntimeError(f"all benchmark worker counts failed: {results}")
    selected = int(
        max(successful, key=lambda item: float(item["aggregate_steps_per_second"]))[
            "workers"
        ]
    )
    report = {"selected_workers": selected, "results": results}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    runtime_config = Path(__file__).resolve().parents[2] / "config" / "runtime.yaml"
    runtime_config.parent.mkdir(parents=True, exist_ok=True)
    runtime_config.write_text(
        yaml.safe_dump(
            {"selected_workers": selected, "source": str(args.output)},
            sort_keys=False,
        )
    )
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
