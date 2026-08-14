from __future__ import annotations

import argparse
import math
import time
from pathlib import Path

import numpy as np

from .cli import development_executable
from .env import MoSimEnv


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


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Preview a MoSimulator robot virtual camera with OpenCV"
    )
    parser.add_argument("--executable", type=Path, default=development_executable())
    parser.add_argument(
        "--camera",
        help="case-sensitive virtual camera ID; defaults to the first configured camera",
    )
    parser.add_argument("--fps", type=positive_float, default=10.0)
    parser.add_argument("--scale", type=positive_float, default=2.0)
    parser.add_argument("--jpeg-quality", type=jpeg_quality, default=85)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=0)
    parser.add_argument("--no-overlay", action="store_true")
    args = parser.parse_args()

    try:
        import cv2
    except ImportError as exc:
        raise SystemExit(
            "OpenCV is required. Install it with: "
            "python -m pip install -e './python[camera]'"
        ) from exc

    env: MoSimEnv | None = None
    window_name = "MoSimulator virtual camera"
    try:
        env = MoSimEnv(
            args.executable,
            base_seed=args.seed,
            curriculum_stage=args.curriculum_stage,
            automatic_curriculum=False,
            graphical=True,
            realtime=True,
            log_dir="runs/camera-preview",
            auto_connect=False,
        )
        env.start_process()
        env.connect()
        env.reset(seed=args.seed)
        cameras = env.list_virtual_cameras()
        if not cameras:
            raise RuntimeError("the active robot has no configured virtual cameras")

        available = {camera.name: camera for camera in cameras}
        camera_name = args.camera or cameras[0].name
        if camera_name not in available:
            names = ", ".join(sorted(available))
            raise RuntimeError(
                f"virtual camera {camera_name!r} was not found; available: {names}"
            )

        camera = available[camera_name]
        print(
            f"Previewing {camera.name!r} at {camera.width}x{camera.height}, "
            f"{args.fps:g} FPS. Press Q or Esc to close."
        )
        cv2.namedWindow(window_name, cv2.WINDOW_AUTOSIZE)
        frame_period = 1.0 / args.fps
        next_frame_at = time.monotonic()

        while True:
            frame = env.get_virtual_camera_frame(
                camera_name,
                jpeg_quality=args.jpeg_quality,
            )
            encoded = np.frombuffer(frame.image_bytes, dtype=np.uint8)
            image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
            if image is None:
                raise RuntimeError("OpenCV could not decode the camera JPEG")

            if not args.no_overlay:
                label = (
                    f"{camera_name}  frame {frame.sequence}  "
                    f"sim {frame.sim_time:.2f}s"
                )
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

            if args.scale != 1.0:
                image = cv2.resize(
                    image,
                    None,
                    fx=args.scale,
                    fy=args.scale,
                    interpolation=cv2.INTER_LINEAR,
                )
            cv2.imshow(window_name, image)

            next_frame_at = max(next_frame_at + frame_period, time.monotonic())
            delay_ms = max(1, int((next_frame_at - time.monotonic()) * 1_000))
            key = cv2.waitKey(delay_ms) & 0xFF
            if key in {27, ord("q"), ord("Q")}:
                break
            if cv2.getWindowProperty(window_name, cv2.WND_PROP_VISIBLE) < 1:
                break
    except KeyboardInterrupt:
        pass
    finally:
        cv2.destroyAllWindows()
        if env is not None:
            env.close()


if __name__ == "__main__":
    main()
