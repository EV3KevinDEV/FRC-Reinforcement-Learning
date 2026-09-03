from __future__ import annotations

import json
import secrets
import socket
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass

import numpy as np

from .constants import GAMEPAD_ACTION_DIM, PROTOCOL_VERSION
from .gamepad import GamepadActionAdapter
from .physical_gamepad import PhysicalGamepad, active_button_names


MAX_CONTROL_DATAGRAM_BYTES = 4096


@dataclass(frozen=True, slots=True)
class RealtimeGamepadSample:
    """One controller sample sent to Unity's low-latency control endpoint."""

    session: str
    sequence: int
    captured_at: float
    gamepad_action: np.ndarray
    semantic_action: np.ndarray


def encode_realtime_control(
    *,
    session: str,
    sequence: int,
    active: bool,
    gamepad_action: np.ndarray | None = None,
    semantic_action: np.ndarray | None = None,
) -> bytes:
    """Encode one self-contained UDP controller datagram."""

    if not session:
        raise ValueError("session must be non-empty")
    if sequence <= 0:
        raise ValueError("sequence must be positive")

    message: dict[str, object] = {
        "v": PROTOCOL_VERSION,
        "session": session,
        "sequence": sequence,
        "active": active,
    }
    if active:
        raw = np.asarray(gamepad_action, dtype=np.float32)
        semantic = np.asarray(semantic_action, dtype=np.float32)
        if raw.shape != (GAMEPAD_ACTION_DIM,):
            raise ValueError(
                f"expected gamepad action shape {(GAMEPAD_ACTION_DIM,)}, got {raw.shape}"
            )
        if semantic.shape != (6,):
            raise ValueError(
                f"expected semantic action shape {(6,)}, got {semantic.shape}"
            )
        message["gamepad_action"] = raw.tolist()
        message["action"] = semantic.tolist()

    payload = json.dumps(message, separators=(",", ":"), allow_nan=False).encode(
        "utf-8"
    )
    if len(payload) > MAX_CONTROL_DATAGRAM_BYTES:
        raise ValueError(f"realtime control datagram is too large: {len(payload)}")
    return payload


class RealtimeGamepadController:
    """Poll a physical controller and stream commands independently at 50 Hz."""

    def __init__(
        self,
        host: str,
        port: int,
        *,
        index: int = 0,
        deadzone: float = 0.12,
        control_hz: float = 50.0,
    ) -> None:
        if not host:
            raise ValueError("host must be non-empty")
        if not 1 <= port <= 65535:
            raise ValueError("port must be in [1, 65535]")
        if not 1.0 <= control_hz <= 250.0:
            raise ValueError("control_hz must be in [1, 250]")

        self.host = host
        self.port = port
        self.index = index
        self.deadzone = deadzone
        self.control_hz = control_hz
        self.session = secrets.token_hex(12)

        self._condition = threading.Condition()
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None
        self._name: str | None = None
        self._latest: RealtimeGamepadSample | None = None
        self._error: BaseException | None = None
        self._pending_rising: set[str] = set()
        self._reset_requested = 0
        self._reset_completed = 0

    @property
    def name(self) -> str:
        with self._condition:
            if self._name is None:
                raise RuntimeError("realtime gamepad is not open")
            return self._name

    def open(self, timeout: float = 5.0) -> RealtimeGamepadController:
        if self._thread is not None:
            return self
        self._stop.clear()
        self._thread = threading.Thread(
            target=self._run,
            daemon=True,
            name="MoSim realtime gamepad",
        )
        self._thread.start()
        self._wait_for(lambda: self._latest is not None, timeout)
        return self

    def snapshot(self) -> RealtimeGamepadSample:
        with self._condition:
            self._raise_thread_error()
            if self._latest is None:
                raise RuntimeError("realtime gamepad has no sample")
            sample = self._latest
            return RealtimeGamepadSample(
                session=sample.session,
                sequence=sample.sequence,
                captured_at=sample.captured_at,
                gamepad_action=sample.gamepad_action.copy(),
                semantic_action=sample.semantic_action.copy(),
            )

    def consume_rising_buttons(self) -> set[str]:
        """Return button presses accumulated between dataset samples."""

        with self._condition:
            self._raise_thread_error()
            rising = set(self._pending_rising)
            self._pending_rising.clear()
            return rising

    def reset(self, timeout: float = 2.0) -> None:
        """Reset stateful button mappings at an episode boundary."""

        with self._condition:
            self._raise_thread_error()
            self._reset_requested += 1
            requested = self._reset_requested
            self._pending_rising.clear()
            self._condition.notify_all()
        self._wait_for(lambda: self._reset_completed >= requested, timeout)

    def close(self) -> None:
        thread, self._thread = self._thread, None
        if thread is None:
            return
        self._stop.set()
        with self._condition:
            self._condition.notify_all()
        thread.join(timeout=2.0)
        if thread.is_alive():
            raise RuntimeError("realtime gamepad thread did not stop")

    def _wait_for(self, predicate: Callable[[], bool], timeout: float) -> None:
        deadline = time.monotonic() + timeout
        with self._condition:
            while not predicate():
                self._raise_thread_error()
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("timed out waiting for realtime gamepad")
                self._condition.wait(remaining)
            self._raise_thread_error()

    def _raise_thread_error(self) -> None:
        if self._error is not None:
            raise RuntimeError(f"realtime gamepad failed: {self._error}") from self._error

    def _run(self) -> None:
        sock: socket.socket | None = None
        sequence = 0
        try:
            with PhysicalGamepad(self.index, self.deadzone) as gamepad:
                sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                adapter = GamepadActionAdapter()
                previous_pressed: set[str] = set()
                reset_seen = 0
                period = 1.0 / self.control_hz
                next_tick = time.monotonic()

                with self._condition:
                    self._name = gamepad.name
                    self._condition.notify_all()

                while not self._stop.is_set():
                    with self._condition:
                        requested_reset = self._reset_requested
                    if requested_reset != reset_seen:
                        adapter.reset()
                        previous_pressed.clear()
                        reset_seen = requested_reset

                    raw = np.asarray(gamepad.read(), dtype=np.float32)
                    semantic = adapter.to_semantic(raw)
                    sequence += 1
                    captured_at = time.monotonic()
                    sock.sendto(
                        encode_realtime_control(
                            session=self.session,
                            sequence=sequence,
                            active=True,
                            gamepad_action=raw,
                            semantic_action=semantic,
                        ),
                        (self.host, self.port),
                    )

                    pressed = set(active_button_names(raw))
                    with self._condition:
                        self._pending_rising.update(pressed - previous_pressed)
                        self._latest = RealtimeGamepadSample(
                            session=self.session,
                            sequence=sequence,
                            captured_at=captured_at,
                            gamepad_action=raw.copy(),
                            semantic_action=semantic.copy(),
                        )
                        self._reset_completed = reset_seen
                        self._condition.notify_all()
                    previous_pressed = pressed

                    next_tick += period
                    now = time.monotonic()
                    if next_tick < now:
                        next_tick = now
                    self._stop.wait(max(0.0, next_tick - now))
        except BaseException as exc:
            with self._condition:
                self._error = exc
                self._condition.notify_all()
        finally:
            if sock is not None:
                try:
                    sequence += 1
                    sock.sendto(
                        encode_realtime_control(
                            session=self.session,
                            sequence=sequence,
                            active=False,
                        ),
                        (self.host, self.port),
                    )
                except OSError:
                    pass
                sock.close()

    def __enter__(self) -> RealtimeGamepadController:
        return self.open()

    def __exit__(self, *_args: object) -> None:
        self.close()
