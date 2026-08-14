from __future__ import annotations

from pathlib import Path

import numpy as np

from mosim_rl import smoke


class FakeSmokeEnv:
    def __init__(self, executable: Path, **kwargs) -> None:
        del executable, kwargs
        from gymnasium import spaces

        self.observation_space = spaces.Box(-1.0, 1.0, (62,), dtype=np.float32)
        self.action_space = spaces.Box(-1.0, 1.0, (25,), dtype=np.float32)
        self.capabilities = {"decision_dt": 0.1, "fixed_dt": 0.0045}
        self.steps = 0
        self.closed = False

    def reset(self, *, seed: int):
        del seed
        return np.zeros(62, dtype=np.float32), {"sim_time": 0.0}

    def step(self, action: np.ndarray):
        assert action.shape == (25,)
        self.steps += 1
        return (
            np.zeros(62, dtype=np.float32),
            0.0,
            False,
            False,
            {"sim_time": self.steps * 0.1, "score": {"total_points": 0}},
        )

    def close(self) -> None:
        self.closed = True


def test_verify_runtime_checks_reset_step_and_time(monkeypatch, capsys) -> None:
    created: list[FakeSmokeEnv] = []

    def make_env(executable: Path, **kwargs) -> FakeSmokeEnv:
        env = FakeSmokeEnv(executable, **kwargs)
        created.append(env)
        return env

    monkeypatch.setattr(smoke, "MoSimEnv", make_env)
    smoke.verify_runtime(Path("MoSimRL"), steps=20, seed=7, action_mode="gamepad")

    assert created[0].closed is True
    assert "runtime smoke test passed" in capsys.readouterr().out
