from __future__ import annotations

import base64
import binascii
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True, slots=True)
class VirtualCameraInfo:
    """Configuration and robot-relative pose of a Unity sensor camera."""

    name: str
    width: int
    height: int
    vertical_fov_degrees: float
    near_clip: float
    far_clip: float
    robot_position: tuple[float, float, float]
    robot_rotation_euler: tuple[float, float, float]

    @classmethod
    def from_payload(cls, payload: dict[str, Any]) -> VirtualCameraInfo:
        return cls(
            name=_required_string(payload, "name"),
            width=_positive_int(payload, "width"),
            height=_positive_int(payload, "height"),
            vertical_fov_degrees=_finite_float(payload, "vertical_fov_degrees"),
            near_clip=_finite_float(payload, "near_clip"),
            far_clip=_finite_float(payload, "far_clip"),
            robot_position=_vector3(payload, "robot_position"),
            robot_rotation_euler=_vector3(payload, "robot_rotation_euler"),
        )


@dataclass(frozen=True, slots=True)
class VirtualCameraFrame:
    """One decoded JPEG frame captured by a Unity sensor camera."""

    name: str
    width: int
    height: int
    encoding: str
    media_type: str
    image_bytes: bytes
    sequence: int
    sim_time: float

    @classmethod
    def from_payload(cls, payload: dict[str, Any]) -> VirtualCameraFrame:
        encoding = _required_string(payload, "encoding")
        media_type = _required_string(payload, "media_type")
        if encoding != "jpeg" or media_type != "image/jpeg":
            raise ValueError(
                f"unsupported camera frame format: {encoding!r} / {media_type!r}"
            )

        encoded_image = _required_string(payload, "image_base64")
        try:
            image_bytes = base64.b64decode(encoded_image, validate=True)
        except (ValueError, binascii.Error) as exc:
            raise ValueError("image_base64 is not valid base64") from exc
        if not image_bytes:
            raise ValueError("camera frame is empty")

        sequence = payload.get("sequence")
        if isinstance(sequence, bool) or not isinstance(sequence, int) or sequence < 0:
            raise ValueError("sequence must be a non-negative integer")

        return cls(
            name=_required_string(payload, "name"),
            width=_positive_int(payload, "width"),
            height=_positive_int(payload, "height"),
            encoding=encoding,
            media_type=media_type,
            image_bytes=image_bytes,
            sequence=sequence,
            sim_time=_finite_float(payload, "sim_time"),
        )

    def save(self, path: str | Path) -> Path:
        """Write the JPEG bytes to *path* and return the resolved path."""

        destination = Path(path).expanduser().resolve()
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(self.image_bytes)
        return destination


def _required_string(payload: dict[str, Any], key: str) -> str:
    value = payload.get(key)
    if not isinstance(value, str) or not value:
        raise ValueError(f"{key} must be a non-empty string")
    return value


def _positive_int(payload: dict[str, Any], key: str) -> int:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"{key} must be a positive integer")
    return value


def _finite_float(payload: dict[str, Any], key: str) -> float:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{key} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{key} must be finite")
    return result


def _vector3(payload: dict[str, Any], key: str) -> tuple[float, float, float]:
    value = payload.get(key)
    if not isinstance(value, list) or len(value) != 3:
        raise ValueError(f"{key} must be a three-value array")
    vector = [_finite_float({key: component}, key) for component in value]
    return vector[0], vector[1], vector[2]
