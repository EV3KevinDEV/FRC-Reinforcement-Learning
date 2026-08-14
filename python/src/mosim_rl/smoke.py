from __future__ import annotations

import argparse
import math
from pathlib import Path

import numpy as np

from .cli import default_executable, positive_int
from .env import MoSimEnv


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Verify a real headless Unity worker through Gymnasium reset/step"
    )
    parser.add_argument("--executable", type=Path, default=default_executable())
    parser.add_argument("--steps", type=positive_int, default=20)
    parser.add_argument("--seed", type=int, default=2026)
    parser.add_argument(
        "--action-mode", choices=("gamepad", "semantic"), default="gamepad"
    )
    return parser


def verify_runtime(
    executable: Path,
    *,
    steps: int,
    seed: int,
    action_mode: str,
) -> None:
    env = MoSimEnv(
        executable,
        base_seed=seed,
        automatic_curriculum=False,
        action_mode=action_mode,
    )
    try:
        observation, info = env.reset(seed=seed)
        if not env.observation_space.contains(observation):
            raise RuntimeError("reset returned an observation outside the declared space")
        start_time = float(info.get("sim_time", 0.0))
        action = np.zeros(env.action_space.shape, dtype=np.float32)
        for step in range(steps):
            observation, reward, terminated, truncated, info = env.step(action)
            if not env.observation_space.contains(observation):
                raise RuntimeError(
                    f"step {step} returned an observation outside the declared space"
                )
            if not math.isfinite(float(reward)):
                raise RuntimeError(f"step {step} returned a non-finite reward")
            if truncated:
                raise RuntimeError(
                    f"worker truncated at step {step}: {info.get('termination_reason')}"
                )
            if terminated:
                raise RuntimeError(
                    f"episode terminated unexpectedly at step {step}: "
                    f"{info.get('termination_reason')}"
                )

        elapsed = float(info.get("sim_time", 0.0)) - start_time
        decision_dt = float(env.capabilities.get("decision_dt", 0.1))
        fixed_dt = float(env.capabilities.get("fixed_dt", 0.0045))
        expected = steps * decision_dt
        if not math.isclose(elapsed, expected, rel_tol=0.0, abs_tol=fixed_dt + 1e-5):
            raise RuntimeError(
                f"simulation time advanced {elapsed:.6f}s; expected {expected:.6f}s"
            )

        print(
            "MoSim runtime smoke test passed: "
            f"steps={steps} sim_time={elapsed:.3f}s "
            f"observation={observation.shape} action={action.shape} "
            f"score={info.get('score', {}).get('total_points', 0)}",
            flush=True,
        )
    finally:
        env.close()


def main() -> None:
    args = build_parser().parse_args()
    verify_runtime(
        args.executable.resolve(),
        steps=args.steps,
        seed=args.seed,
        action_mode=args.action_mode,
    )


if __name__ == "__main__":
    main()
