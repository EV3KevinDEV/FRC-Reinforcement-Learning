from __future__ import annotations

import numpy as np
from gymnasium import spaces

import mosim_rl.vec_env as vec_module


def test_step_async_sends_every_action_before_receiving(monkeypatch) -> None:
    events: list[tuple[str, int]] = []

    class FakeEnv:
        def __init__(self, executable_path, *, worker_id: int, **kwargs) -> None:
            self.worker_id = worker_id
            self.graphical = kwargs["graphical"]
            self.realtime = kwargs["realtime"]
            self.render_mode = None
            self.action_space = spaces.Box(-1.0, 1.0, shape=(6,), dtype=np.float32)
            self.observation_space = spaces.Box(
                -1.0, 1.0, shape=(62,), dtype=np.float32
            )

        def start_process(self) -> None:
            pass

        def connect(self) -> None:
            pass

        def reset(self, **kwargs):
            return np.zeros(62, dtype=np.float32), {"worker_id": self.worker_id}

        def begin_step(self, action: np.ndarray) -> None:
            events.append(("begin", self.worker_id))

        def finish_step(self):
            assert events[:3] == [("begin", 0), ("begin", 1), ("begin", 2)]
            events.append(("finish", self.worker_id))
            return (
                np.zeros(62, dtype=np.float32),
                0.0,
                False,
                False,
                {"worker_id": self.worker_id},
            )

        def close(self) -> None:
            pass

    monkeypatch.setattr(vec_module, "MoSimEnv", FakeEnv)
    env = vec_module.MoSimVecEnv(
        "unused", num_envs=3, graphical_worker=1, realtime_graphical=True
    )
    try:
        assert [worker.graphical for worker in env.envs] == [False, True, False]
        assert [worker.realtime for worker in env.envs] == [False, True, False]
        env.step_async(np.zeros((3, 6), dtype=np.float32))
        observations, rewards, dones, infos = env.step_wait()
        assert observations.shape == (3, 62)
        assert rewards.shape == dones.shape == (3,)
        assert [info["worker_id"] for info in infos] == [0, 1, 2]
    finally:
        env.close()
