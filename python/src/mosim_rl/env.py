from __future__ import annotations

import time
from pathlib import Path
from typing import Any

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from .constants import (
    ACTION_HIGH,
    ACTION_LOW,
    DEFAULT_CONNECT_TIMEOUT,
    DEFAULT_FRAME_SKIP,
    DEFAULT_HOST,
    DEFAULT_STEP_TIMEOUT,
    GAMEPAD_ACTION_HIGH,
    GAMEPAD_ACTION_LOW,
    GAMEPAD_ACTIVE_MASK,
    NITROGEN_BUTTONS,
    OBSERVATION_DIM,
    PROTOCOL_VERSION,
)
from .curriculum import CurriculumManager
from .gamepad import GamepadActionAdapter
from .launcher import UnityWorkerProcess, reserve_tcp_port
from .observation import ObservationEncoder
from .protocol import ProtocolClient, ProtocolError, TransportClosed
from .rewards import RewardCalculator


class MoSimEnv(gym.Env[np.ndarray, np.ndarray]):
    metadata = {"render_modes": []}

    def __init__(
        self,
        executable_path: str | Path | None = None,
        *,
        host: str = DEFAULT_HOST,
        port: int | None = None,
        worker_id: int = 0,
        base_seed: int = 0,
        frame_skip: int = DEFAULT_FRAME_SKIP,
        step_timeout: float = DEFAULT_STEP_TIMEOUT,
        connect_timeout: float = DEFAULT_CONNECT_TIMEOUT,
        reset_timeout: float = 120.0,
        log_dir: str | Path = "runs/unity",
        curriculum_stage: int = 0,
        automatic_curriculum: bool = True,
        action_mode: str = "semantic",
        graphical: bool = False,
        realtime: bool = False,
        auto_connect: bool = True,
        client: ProtocolClient | Any | None = None,
    ) -> None:
        super().__init__()
        self.render_mode = None
        if action_mode not in {"semantic", "gamepad"}:
            raise ValueError("action_mode must be 'semantic' or 'gamepad'")
        self.action_mode = action_mode
        self.action_space = spaces.Box(
            GAMEPAD_ACTION_LOW if action_mode == "gamepad" else ACTION_LOW,
            GAMEPAD_ACTION_HIGH if action_mode == "gamepad" else ACTION_HIGH,
            dtype=np.float32,
        )
        self.observation_space = spaces.Box(
            low=-1.0, high=1.0, shape=(OBSERVATION_DIM,), dtype=np.float32
        )
        self.host = host
        self.port = port or reserve_tcp_port(host)
        self.worker_id = worker_id
        self.base_seed = base_seed
        self.frame_skip = frame_skip
        self.step_timeout = step_timeout
        self.connect_timeout = connect_timeout
        self.reset_timeout = reset_timeout
        self.executable_path = (
            Path(executable_path).resolve() if executable_path else None
        )
        self.log_dir = Path(log_dir).resolve()
        self.graphical = graphical
        self.realtime = realtime
        if realtime and not graphical:
            raise ValueError("realtime mode requires graphical=True")
        self.curriculum = CurriculumManager(
            stage=curriculum_stage, automatic=automatic_curriculum
        )
        self.encoder = ObservationEncoder()
        self.reward_calculator = RewardCalculator()
        self.gamepad_adapter = GamepadActionAdapter()
        self._worker: UnityWorkerProcess | None = None
        self._client = client
        self._connected = client is not None
        self._capabilities: dict[str, Any] = {}
        self._episode_index = 0
        self._previous_action = np.zeros(6, dtype=np.float32)
        self._pending_action: np.ndarray | None = None
        self._pending_policy_action: np.ndarray | None = None
        self._last_raw_state: dict[str, Any] = {}
        self._last_observation = np.zeros(OBSERVATION_DIM, dtype=np.float32)
        self._needs_restart = False
        if auto_connect and client is None:
            self.start_process()
            self.connect()

    @property
    def unity_process(self) -> UnityWorkerProcess | None:
        return self._worker

    @property
    def capabilities(self) -> dict[str, Any]:
        capabilities = dict(self._capabilities)
        capabilities["wire_action_dim"] = int(capabilities.get("action_dim", 6))
        capabilities["policy_action_dim"] = int(self.action_space.shape[0])
        capabilities["action_mode"] = self.action_mode
        return capabilities

    def start_process(self) -> None:
        if self.executable_path is None:
            return
        if self._worker is None:
            self._worker = UnityWorkerProcess(
                executable=self.executable_path,
                worker_id=self.worker_id,
                port=self.port,
                log_dir=self.log_dir,
                seed=self.base_seed + self.worker_id,
                graphical=self.graphical,
                realtime=self.realtime,
            )
        self._worker.start()

    def connect(self) -> None:
        if self._client is not None and self._connected:
            return
        deadline = time.monotonic() + self.connect_timeout
        last_error: Exception | None = None
        while time.monotonic() < deadline:
            if self._worker is not None:
                self._worker.assert_running()
            client = ProtocolClient(
                self.host,
                self.port,
                timeout=self.step_timeout,
                worker_id=self.worker_id,
            )
            try:
                capabilities = client.connect()
                if int(capabilities.get("action_dim", 6)) != 6:
                    raise ProtocolError("Unity action schema is incompatible")
                if int(capabilities.get("observation_dim", 62)) != 62:
                    raise ProtocolError("Unity observation schema is incompatible")
                self._client = client
                self._capabilities = dict(capabilities)
                self._connected = True
                return
            except (OSError, ProtocolError, TransportClosed) as exc:
                last_error = exc
                client.close()
                time.sleep(0.1)
        raise TimeoutError(
            f"failed to connect to Unity worker {self.worker_id} at "
            f"{self.host}:{self.port}: {last_error}"
        )

    def _restart(self) -> None:
        if self._client is not None:
            self._client.close()
        self._client = None
        self._connected = False
        self._capabilities = {}
        if self._worker is not None:
            self._worker.stop()
        self.start_process()
        self.connect()
        self._needs_restart = False

    def reset(
        self,
        *,
        seed: int | None = None,
        options: dict[str, Any] | None = None,
    ) -> tuple[np.ndarray, dict[str, Any]]:
        super().reset(seed=seed)
        if self._needs_restart:
            self._restart()
        elif not self._connected:
            self.start_process()
            self.connect()
        assert self._client is not None
        episode_seed = (
            int(seed)
            if seed is not None
            else self.base_seed + self.worker_id * 100_000 + self._episode_index
        )
        reset_options = self.curriculum.reset_options()
        reset_options.update(options or {})
        payload = self._client.request(
            "reset",
            {
                "seed": episode_seed,
                "frame_skip": self.frame_skip,
                **reset_options,
            },
            timeout=self.reset_timeout,
        )
        raw_state = payload.get("state")
        if not isinstance(raw_state, dict):
            raise ProtocolError("reset response did not contain a state object")
        self._previous_action = np.zeros(6, dtype=np.float32)
        self.gamepad_adapter.reset()
        self._last_raw_state = raw_state
        self._last_observation = self.encoder.encode(raw_state, self._previous_action)
        self.reward_calculator.reset(raw_state)
        self._episode_index += 1
        info = self._build_info(payload, raw_state)
        info["seed"] = episode_seed
        return self._last_observation.copy(), info

    def begin_step(self, action: np.ndarray) -> None:
        if self._pending_action is not None:
            raise RuntimeError("step already pending")
        policy_action = np.asarray(action, dtype=np.float32)
        if policy_action.shape != self.action_space.shape:
            raise ValueError(
                f"expected action shape {self.action_space.shape}, got {policy_action.shape}"
            )
        policy_action = np.clip(
            policy_action, self.action_space.low, self.action_space.high
        )
        semantic_action = (
            self.gamepad_adapter.to_semantic(policy_action)
            if self.action_mode == "gamepad"
            else policy_action
        )
        assert self._client is not None
        self._client.begin_request(
            "step", {"action": semantic_action.tolist(), "frame_skip": self.frame_skip}
        )
        self._pending_action = semantic_action
        self._pending_policy_action = policy_action.copy()

    def finish_step(
        self,
    ) -> tuple[np.ndarray, float, bool, bool, dict[str, Any]]:
        if self._pending_action is None:
            raise RuntimeError("no step pending")
        action, self._pending_action = self._pending_action, None
        policy_action, self._pending_policy_action = self._pending_policy_action, None
        try:
            assert self._client is not None
            payload = self._client.finish_request()
            raw_state = payload.get("state")
            if not isinstance(raw_state, dict):
                raise ProtocolError("step response did not contain a state object")
            events = payload.get("events", {})
            if not isinstance(events, dict):
                events = {}
            reward_result = self.reward_calculator.calculate(raw_state, action, events)
            observation = self.encoder.encode(raw_state, action)
            self._last_raw_state = raw_state
            self._last_observation = observation
            self._previous_action = action.copy()
            if events.get("cycle_success", False):
                self.curriculum.record_subgoal(True)
            elif events.get("cycle_failed", False):
                self.curriculum.record_subgoal(False)
            info = self._build_info(payload, raw_state)
            info["action_mode"] = self.action_mode
            info["semantic_action"] = action.copy()
            if policy_action is not None and self.action_mode == "gamepad":
                info["gamepad_action"] = policy_action.copy()
                info["gamepad_active_mask"] = GAMEPAD_ACTIVE_MASK.copy()
            info["reward_terms"] = reward_result.terms
            info["termination_reason"] = reward_result.reason
            return (
                observation.copy(),
                reward_result.total,
                reward_result.terminated,
                False,
                info,
            )
        except (OSError, ProtocolError, TransportClosed, TimeoutError) as exc:
            self._needs_restart = True
            info = {
                "worker_id": self.worker_id,
                "protocol_version": PROTOCOL_VERSION,
                "action_mode": self.action_mode,
                "termination_reason": "worker_error",
                "error": str(exc),
                "reward_terms": {"worker_error": -25.0},
            }
            return self._last_observation.copy(), -25.0, False, True, info

    def step(
        self, action: np.ndarray
    ) -> tuple[np.ndarray, float, bool, bool, dict[str, Any]]:
        self.begin_step(action)
        return self.finish_step()

    def _build_info(
        self, payload: dict[str, Any], raw_state: dict[str, Any]
    ) -> dict[str, Any]:
        info = dict(payload.get("info", {}))
        match = raw_state.get("match", {})
        task = raw_state.get("task", {})
        mechanism = raw_state.get("mechanism", {})
        physics = raw_state.get("physics", {})
        events = payload.get("events", {})
        if not isinstance(events, dict):
            events = {}
        info.update(
            {
                "worker_id": self.worker_id,
                "protocol_version": PROTOCOL_VERSION,
                "curriculum_stage": self.curriculum.stage,
                "curriculum_name": self.curriculum.name,
                "target_level": task.get("target_level", 1),
                "score": match.get("score", {}),
                "score_delta": match.get("score_delta", 0.0),
                "sim_time": match.get("sim_time", 0.0),
                "game_state": match.get("game_state", "Auto"),
                "match_complete": bool(match.get("match_complete", False)),
                "cycle_success": bool(events.get("cycle_success", False)),
                "mechanism": dict(mechanism) if isinstance(mechanism, dict) else {},
                "physics": dict(physics) if isinstance(physics, dict) else {},
            }
        )
        if self.action_mode == "gamepad":
            info["gamepad_active_mask"] = GAMEPAD_ACTIVE_MASK.copy()
            info["gamepad_buttons"] = NITROGEN_BUTTONS
        return info

    def close(self) -> None:
        if self._client is not None:
            try:
                if self._connected:
                    self._client.request("close", {})
            except (OSError, ProtocolError, TransportClosed, TimeoutError):
                pass
            self._client.close()
        self._client = None
        self._connected = False
        self._capabilities = {}
        if self._worker is not None:
            self._worker.stop()
