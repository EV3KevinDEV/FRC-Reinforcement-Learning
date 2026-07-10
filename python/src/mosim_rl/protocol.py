from __future__ import annotations

import json
import socket
import struct
import threading
from dataclasses import dataclass
from typing import Any

from .constants import MAX_FRAME_BYTES, PROTOCOL_VERSION


class ProtocolError(RuntimeError):
    """Raised when a peer violates the MoSimulator RL wire protocol."""


class TransportClosed(ConnectionError):
    """Raised when the simulator closes a connection mid-message."""


def encode_frame(message: dict[str, Any]) -> bytes:
    payload = json.dumps(message, separators=(",", ":"), allow_nan=False).encode(
        "utf-8"
    )
    if not payload or len(payload) > MAX_FRAME_BYTES:
        raise ProtocolError(f"invalid payload length: {len(payload)}")
    return struct.pack(">I", len(payload)) + payload


def _recv_exact(sock: socket.socket, size: int) -> bytes:
    chunks: list[bytes] = []
    remaining = size
    while remaining:
        chunk = sock.recv(remaining)
        if not chunk:
            raise TransportClosed("connection closed while receiving a frame")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def recv_frame(sock: socket.socket) -> dict[str, Any]:
    (length,) = struct.unpack(">I", _recv_exact(sock, 4))
    if length <= 0 or length > MAX_FRAME_BYTES:
        raise ProtocolError(f"invalid frame length: {length}")
    try:
        message = json.loads(_recv_exact(sock, length).decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError("frame is not valid UTF-8 JSON") from exc
    if not isinstance(message, dict):
        raise ProtocolError("top-level frame must be an object")
    return message


def send_frame(sock: socket.socket, message: dict[str, Any]) -> None:
    sock.sendall(encode_frame(message))


@dataclass(slots=True)
class PendingRequest:
    request_id: int
    command: str


class ProtocolClient:
    """Synchronous request client with split send/receive support for VecEnv."""

    def __init__(
        self,
        host: str,
        port: int,
        *,
        timeout: float,
        worker_id: int,
    ) -> None:
        self.host = host
        self.port = port
        self.timeout = timeout
        self.worker_id = worker_id
        self._socket: socket.socket | None = None
        self._next_request_id = 1
        self._pending: PendingRequest | None = None
        self._lock = threading.Lock()

    @property
    def connected(self) -> bool:
        return self._socket is not None

    def connect(self) -> dict[str, Any]:
        self.close()
        sock = socket.create_connection((self.host, self.port), self.timeout)
        sock.settimeout(self.timeout)
        self._socket = sock
        return self.request(
            "hello",
            {
                "client": "mosim-rl-python",
                "worker_id": self.worker_id,
                "action_dim": 6,
                "observation_dim": 62,
            },
        )

    def begin_request(self, command: str, payload: dict[str, Any]) -> int:
        with self._lock:
            if self._socket is None:
                raise TransportClosed("client is not connected")
            if self._pending is not None:
                raise ProtocolError("a request is already pending")
            request_id = self._next_request_id
            self._next_request_id += 1
            send_frame(
                self._socket,
                {
                    "v": PROTOCOL_VERSION,
                    "id": request_id,
                    "cmd": command,
                    "payload": payload,
                },
            )
            self._pending = PendingRequest(request_id, command)
            return request_id

    def finish_request(self) -> dict[str, Any]:
        with self._lock:
            if self._socket is None or self._pending is None:
                raise ProtocolError("there is no pending request")
            pending = self._pending
            self._pending = None
            response = recv_frame(self._socket)
        if response.get("v") != PROTOCOL_VERSION:
            raise ProtocolError("protocol version mismatch")
        if response.get("id") != pending.request_id:
            raise ProtocolError(
                f"stale response id {response.get('id')}; expected {pending.request_id}"
            )
        if response.get("ok") is not True:
            raise ProtocolError(str(response.get("error", "simulator request failed")))
        payload = response.get("payload", {})
        if not isinstance(payload, dict):
            raise ProtocolError("response payload must be an object")
        return payload

    def request(
        self,
        command: str,
        payload: dict[str, Any],
        *,
        timeout: float | None = None,
    ) -> dict[str, Any]:
        sock = self._socket
        previous_timeout = sock.gettimeout() if sock is not None else None
        if sock is not None and timeout is not None:
            sock.settimeout(timeout)
        try:
            self.begin_request(command, payload)
            return self.finish_request()
        finally:
            if sock is not None and timeout is not None and self._socket is sock:
                sock.settimeout(previous_timeout)

    def close(self) -> None:
        with self._lock:
            sock, self._socket = self._socket, None
            self._pending = None
        if sock is not None:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            sock.close()
