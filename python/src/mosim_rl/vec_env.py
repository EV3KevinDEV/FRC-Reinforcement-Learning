from __future__ import annotations

from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np
from stable_baselines3.common.vec_env.base_vec_env import VecEnv, VecEnvStepReturn

from .constants import DEFAULT_NUM_ENVS
from .env import MoSimEnv


class MoSimVecEnv(VecEnv):
    """SB3 VecEnv that batches external Unity workers without Python subprocesses."""

    def __init__(
        self,
        executable_path: str | Path,
        *,
        num_envs: int = DEFAULT_NUM_ENVS,
        base_seed: int = 0,
        log_dir: str | Path = "runs/unity",
        curriculum_stage: int = 0,
        automatic_curriculum: bool = True,
        frame_skip: int = 5,
        graphical_worker: int | None = None,
        realtime_graphical: bool = False,
        action_mode: str = "semantic",
    ) -> None:
        if num_envs <= 0:
            raise ValueError("num_envs must be positive")
        if graphical_worker is not None and not 0 <= graphical_worker < num_envs:
            raise ValueError("graphical_worker must identify an existing environment")
        if realtime_graphical and graphical_worker is None:
            raise ValueError("realtime_graphical requires a graphical_worker")
        self.envs = [
            MoSimEnv(
                executable_path,
                worker_id=index,
                base_seed=base_seed,
                log_dir=log_dir,
                curriculum_stage=curriculum_stage,
                automatic_curriculum=automatic_curriculum,
                frame_skip=frame_skip,
                action_mode=action_mode,
                graphical=index == graphical_worker,
                realtime=realtime_graphical and index == graphical_worker,
                auto_connect=False,
            )
            for index in range(num_envs)
        ]
        for env in self.envs:
            env.start_process()
        try:
            for env in self.envs:
                env.connect()
        except Exception:
            for env in self.envs:
                env.close()
            raise
        super().__init__(
            num_envs, self.envs[0].observation_space, self.envs[0].action_space
        )
        self._actions: np.ndarray | None = None
        self._pending_seeds: list[int | None] = [None] * num_envs
        self._pending_options: list[dict[str, Any]] = [{} for _ in range(num_envs)]

    def reset(self) -> np.ndarray:
        observations = []
        self.reset_infos = []
        for index, env in enumerate(self.envs):
            observation, info = env.reset(
                seed=self._pending_seeds[index], options=self._pending_options[index]
            )
            observations.append(observation)
            self.reset_infos.append(info)
        self._pending_seeds = [None] * self.num_envs
        self._pending_options = [{} for _ in range(self.num_envs)]
        return np.stack(observations)

    def step_async(self, actions: np.ndarray) -> None:
        if self._actions is not None:
            raise RuntimeError("step_async called while another step is pending")
        actions = np.asarray(actions, dtype=np.float32)
        expected_shape = (self.num_envs, *self.action_space.shape)
        if actions.shape != expected_shape:
            raise ValueError(
                f"expected actions shape {expected_shape}, got {actions.shape}"
            )
        self._actions = actions.copy()
        for env, action in zip(self.envs, actions, strict=True):
            env.begin_step(action)

    def step_wait(self) -> VecEnvStepReturn:
        if self._actions is None:
            raise RuntimeError("step_wait called without step_async")
        self._actions = None
        observations: list[np.ndarray] = []
        rewards: list[float] = []
        dones: list[bool] = []
        infos: list[dict[str, Any]] = []
        for env in self.envs:
            observation, reward, terminated, truncated, info = env.finish_step()
            done = terminated or truncated
            if done:
                info["terminal_observation"] = observation.copy()
                info["TimeLimit.truncated"] = bool(truncated and not terminated)
                observation, reset_info = env.reset()
                info["reset_info"] = reset_info
            observations.append(observation)
            rewards.append(reward)
            dones.append(done)
            infos.append(info)
        return (
            np.stack(observations),
            np.asarray(rewards, dtype=np.float32),
            np.asarray(dones, dtype=bool),
            infos,
        )

    def close(self) -> None:
        for env in self.envs:
            env.close()

    def seed(self, seed: int | None = None) -> Sequence[int | None]:
        if seed is None:
            self._pending_seeds = [None] * self.num_envs
        else:
            self._pending_seeds = [seed + index for index in range(self.num_envs)]
        return self._pending_seeds.copy()

    def set_options(
        self, options: list[dict[str, Any]] | dict[str, Any] | None = None
    ) -> None:
        if options is None:
            self._pending_options = [{} for _ in range(self.num_envs)]
        elif isinstance(options, dict):
            self._pending_options = [dict(options) for _ in range(self.num_envs)]
        elif len(options) == self.num_envs:
            self._pending_options = [dict(value) for value in options]
        else:
            raise ValueError("options list must have one entry per environment")

    def get_attr(self, attr_name: str, indices: Any = None) -> list[Any]:
        return [
            getattr(self.envs[index], attr_name) for index in self._get_indices(indices)
        ]

    def set_attr(self, attr_name: str, value: Any, indices: Any = None) -> None:
        for index in self._get_indices(indices):
            setattr(self.envs[index], attr_name, value)

    def env_method(
        self,
        method_name: str,
        *method_args: Any,
        indices: Any = None,
        **method_kwargs: Any,
    ) -> list[Any]:
        return [
            getattr(self.envs[index], method_name)(*method_args, **method_kwargs)
            for index in self._get_indices(indices)
        ]

    def env_is_wrapped(self, wrapper_class: type, indices: Any = None) -> list[bool]:
        return [False for _ in self._get_indices(indices)]

    def get_images(self) -> Sequence[np.ndarray | None]:
        return [None] * self.num_envs

    def _get_indices(self, indices: Any) -> list[int]:
        if indices is None:
            return list(range(self.num_envs))
        if isinstance(indices, (int, np.integer)):
            return [int(indices)]
        if isinstance(indices, Iterable):
            return [int(index) for index in indices]
        raise TypeError(f"unsupported indices: {indices!r}")
