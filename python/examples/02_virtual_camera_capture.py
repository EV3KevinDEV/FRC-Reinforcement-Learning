"""List virtual cameras, capture JPEG frames, and optionally display them."""

from __future__ import annotations

import argparse
import time
from pathlib import Path

import numpy as np

from mosim_rl import MoSimEnv
from _common import executable_path, positive_int


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", help="camera ID; defaults to the first camera")
    parser.add_argument("--frames", type=positive_int, default=10)
    parser.add_argument("--fps", type=positive_int, default=10)
    parser.add_argument("--jpeg-quality", type=int, choices=range(1, 96), default=85)
    parser.add_argument("--output", type=Path, default=Path("runs/examples/cameras"))
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--show", action="store_true", help="also open an OpenCV window")
    args = parser.parse_args()

    cv2 = None
    if args.show:
        try:
            import cv2 as imported_cv2
        except ImportError as exc:
            raise SystemExit("install OpenCV with: pip install -e './python[camera]'") from exc
        cv2 = imported_cv2

    env = MoSimEnv(
        executable_path(args.executable, graphical=True),
        graphical=True,
        realtime=True,
        automatic_curriculum=False,
        log_dir="runs/examples/camera-unity",
        auto_connect=False,
    )
    try:
        env.start_process()
        env.connect()
        env.reset(seed=0)
        cameras = env.list_virtual_cameras()
        if not cameras:
            raise RuntimeError("the active robot has no virtual cameras")
        print("Available cameras:")
        for camera in cameras:
            print(
                f"  {camera.name}: {camera.width}x{camera.height}, "
                f"FOV={camera.vertical_fov_degrees:.1f} degrees, "
                f"robot_position={camera.robot_position}"
            )

        camera_name = args.camera or cameras[0].name
        if camera_name not in {camera.name for camera in cameras}:
            raise RuntimeError(f"camera {camera_name!r} is not configured")

        frame_period = 1.0 / args.fps
        for _ in range(args.frames):
            started_at = time.monotonic()
            frame = env.get_virtual_camera_frame(
                camera_name,
                jpeg_quality=args.jpeg_quality,
            )
            destination = frame.save(
                args.output / f"{camera_name}-{frame.sequence:06d}.jpg"
            )
            print(f"saved {destination} at sim_time={frame.sim_time:.2f}")

            if cv2 is not None:
                encoded = np.frombuffer(frame.image_bytes, dtype=np.uint8)
                image = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
                if image is None:
                    raise RuntimeError("OpenCV could not decode the JPEG")
                cv2.imshow(f"Virtual camera: {camera_name}", image)
                if cv2.waitKey(1) & 0xFF in {27, ord("q"), ord("Q")}:
                    break

            remaining = frame_period - (time.monotonic() - started_at)
            if remaining > 0:
                time.sleep(remaining)
    finally:
        if cv2 is not None:
            cv2.destroyAllWindows()
        env.close()


if __name__ == "__main__":
    main()
