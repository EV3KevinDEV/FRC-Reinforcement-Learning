"""Display the configured Team 118 Limelight cameras in separate windows."""

from __future__ import annotations

import argparse
import math
import time
from pathlib import Path

import numpy as np

from mosim_rl import MoSimEnv
from _common import executable_path


DEFAULT_CAMERA_IDS = (
    "LimeLightFrontLeft",
    "LimelightLeftBack",
    "LimelightRightBack",
    "front",
    "back"
)


def positive_float(value: str) -> float:
    parsed = float(value)
    if not math.isfinite(parsed) or parsed <= 0:
        raise argparse.ArgumentTypeError("value must be positive")
    return parsed


def jpeg_quality(value: str) -> int:
    parsed = int(value)
    if not 1 <= parsed <= 95:
        raise argparse.ArgumentTypeError("JPEG quality must be between 1 and 95")
    return parsed


def decode_image(cv2, image_bytes: bytes):
    encoded = np.frombuffer(image_bytes, dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if image is None:
        raise RuntimeError("OpenCV could not decode a Limelight JPEG")
    return image


def add_overlay(cv2, image, camera_name: str, sequence: int, sim_time: float) -> None:
    label = f"{camera_name}  frame {sequence}  sim {sim_time:.2f}s"
    cv2.putText(
        image,
        label,
        (8, 20),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        (0, 0, 0),
        3,
        cv2.LINE_AA,
    )
    cv2.putText(
        image,
        label,
        (8, 20),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        (0, 255, 255),
        1,
        cv2.LINE_AA,
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--camera",
        dest="camera_ids",
        action="append",
        help=(
            "camera ID to display; repeat for multiple cameras. "
            "Defaults to the three Team 118 Limelights."
        ),
    )
    parser.add_argument("--fps", type=positive_float, default=10.0)
    parser.add_argument("--scale", type=positive_float, default=2.0)
    parser.add_argument("--jpeg-quality", type=jpeg_quality, default=85)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--no-overlay", action="store_true")
    args = parser.parse_args()

    camera_ids = tuple(args.camera_ids or DEFAULT_CAMERA_IDS)
    if len(set(camera_ids)) != len(camera_ids):
        parser.error("each --camera ID must be unique")

    try:
        import cv2
    except ImportError as exc:
        raise SystemExit(
            "OpenCV is required. Install it with: "
            "python -m pip install -e './python[camera]'"
        ) from exc

    env: MoSimEnv | None = None
    windows = {
        camera_id: f"Limelight ({camera_id})" for camera_id in camera_ids
    }
    try:
        env = MoSimEnv(
            executable_path(args.executable, graphical=True),
            base_seed=args.seed,
            automatic_curriculum=False,
            graphical=True,
            realtime=True,
            log_dir="runs/examples/limelight-unity",
            auto_connect=False,
        )
        env.start_process()
        env.connect()
        env.reset(seed=args.seed)

        available = {camera.name: camera for camera in env.list_virtual_cameras()}
        missing = [camera_id for camera_id in camera_ids if camera_id not in available]
        if missing:
            names = ", ".join(sorted(available)) or "<none>"
            raise RuntimeError(
                f"Configured camera(s) not found: {', '.join(missing)}; "
                f"available cameras: {names}. "
                "Rebuild the graphical player after prefab changes with "
                "'scripts/build_unity.sh development'."
            )

        for camera_id in camera_ids:
            camera = available[camera_id]
            print(
                f"Previewing {camera.name!r} at {camera.width}x{camera.height}. "
                "Close either window or press Q/Esc to exit."
            )
            cv2.namedWindow(windows[camera_id], cv2.WINDOW_NORMAL)

        frame_period = 1.0 / args.fps
        next_frame_at = time.monotonic()
        while True:
            for camera_id in camera_ids:
                frame = env.get_virtual_camera_frame(
                    camera_id,
                    jpeg_quality=args.jpeg_quality,
                )
                image = decode_image(cv2, frame.image_bytes)
                if not args.no_overlay:
                    add_overlay(cv2, image, camera_id, frame.sequence, frame.sim_time)
                if args.scale != 1.0:
                    image = cv2.resize(
                        image,
                        None,
                        fx=args.scale,
                        fy=args.scale,
                        interpolation=cv2.INTER_LINEAR,
                    )
                cv2.imshow(windows[camera_id], image)

            next_frame_at = max(next_frame_at + frame_period, time.monotonic())
            delay_ms = max(1, int((next_frame_at - time.monotonic()) * 1_000))
            key = cv2.waitKey(delay_ms) & 0xFF
            if key in {27, ord("q"), ord("Q")}:
                break
            if any(
                cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1
                for window in windows.values()
            ):
                break
    except KeyboardInterrupt:
        pass
    finally:
        cv2.destroyAllWindows()
        if env is not None:
            env.close()


if __name__ == "__main__":
    main()
