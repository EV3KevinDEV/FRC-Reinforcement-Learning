from __future__ import annotations

import socket
import struct

import pytest

from mosim_rl.constants import MAX_FRAME_BYTES, PROTOCOL_VERSION
from mosim_rl.protocol import (
    ProtocolClient,
    ProtocolError,
    encode_frame,
    recv_frame,
    send_frame,
)


def test_frame_round_trip() -> None:
    left, right = socket.socketpair()
    try:
        message = {"v": 1, "id": 7, "cmd": "ping", "payload": {"snowman": "☃"}}
        left.sendall(encode_frame(message))
        assert recv_frame(right) == message
    finally:
        left.close()
        right.close()


@pytest.mark.parametrize("length", [0, MAX_FRAME_BYTES + 1])
def test_rejects_invalid_frame_lengths(length: int) -> None:
    left, right = socket.socketpair()
    try:
        left.sendall(struct.pack(">I", length))
        with pytest.raises(ProtocolError, match="invalid frame length"):
            recv_frame(right)
    finally:
        left.close()
        right.close()


def test_rejects_malformed_utf8_json() -> None:
    left, right = socket.socketpair()
    try:
        left.sendall(struct.pack(">I", 2) + b"\xff}")
        with pytest.raises(ProtocolError, match="UTF-8 JSON"):
            recv_frame(right)
    finally:
        left.close()
        right.close()


@pytest.mark.parametrize(
    ("response", "error"),
    [
        ({"v": PROTOCOL_VERSION, "id": 99, "ok": True, "payload": {}}, "stale"),
        ({"v": PROTOCOL_VERSION + 1, "id": 1, "ok": True, "payload": {}}, "version"),
    ],
)
def test_client_rejects_stale_or_incompatible_response(
    response: dict, error: str
) -> None:
    client_socket, server_socket = socket.socketpair()
    client = ProtocolClient("127.0.0.1", 0, timeout=1.0, worker_id=0)
    client._socket = client_socket
    try:
        client.begin_request("ping", {})
        assert recv_frame(server_socket)["id"] == 1
        send_frame(server_socket, response)
        with pytest.raises(ProtocolError, match=error):
            client.finish_request()
    finally:
        client.close()
        server_socket.close()
