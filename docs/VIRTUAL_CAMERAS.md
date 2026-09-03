# Add custom cameras to a robot

This guide explains how to mount simulated RGB cameras on a MoSimulator robot prefab, preview their placement, and capture their images from Python. These are on-demand Unity sensor cameras for simulation; they are not physical USB/IP cameras and do not configure CameraServer, NetworkTables, PhotonVision, or a roboRIO.

A configured camera is saved as part of the robot prefab. It follows whichever robot transform it is mounted to, stays disabled during normal rendering, and renders a JPEG only when the RL client asks for one. Camera images are available through a graphical development player, but they are not currently part of the Gymnasium observation.

## Before you start

- Open the project in Unity `2023.2.22f1` and allow it to finish importing and compiling.
- Decide which robot prefab should own the camera. The RL environment currently uses the Team 118 Robonauts robot.
- Make sure the camera has a unique, descriptive ID such as `frontNav`, `rear`, or `intakeView`. IDs are case-sensitive.
- Plan whether the view should remain fixed to the chassis or move with a mechanism. This determines the mount transform you select.

The Team 118 prefab currently contains cameras named `LimeLightFrontLeft`, `LimelightLeftBack`, and `LimelightRightBack`. Camera IDs are case-sensitive; use the exact names shown under **Configured cameras**, or choose a different ID for an additional camera.

## Add a camera in Unity

1. Open **MoSimulator > RL > Virtual Camera Tool**, or press `F8`.
2. In **Robot**, select the metadata entry for the target robot. For this project, select **118 - Robonauts**.
3. If the robot defines an alternate prefab, choose whether to edit it with **Alternate prefab**. A camera added to one prefab is not automatically added to the other.
4. Select **Open and frame robot prefab**. The Scene view opens the editable prefab and frames its root.
5. In **Add camera**, enter the camera settings described below.
6. Leave **Scene placement preview** enabled. Use the colored arrows to move the camera, the rings to rotate it, and the cyan frustum to inspect its view direction and field of view.
7. Select **Add virtual camera**.
8. Confirm that the new entry appears under **Configured cameras**.

The tool immediately edits and saves the selected prefab asset. It creates a child GameObject named `Virtual Camera (<ID>)` with a disabled Unity `Camera` and a `RobotVirtualCamera` component. You do not need to add either component manually.

### Camera settings

| Field | Meaning | Guidance |
|---|---|---|
| **Camera ID** | Name used by the Python and wire APIs | Use only letters, numbers, `_`, and `-`. It must be unique on this prefab and is case-sensitive. |
| **Mount transform** | Robot transform that owns the camera | Use `<Robot Root>` for a chassis-fixed view. Select an arm, wrist, intake, or other child when the camera should move with that mechanism. |
| **Local position** | Position relative to the mount, in metres | Unity uses `+X` right, `+Y` up, and `+Z` forward. Start clear of bumpers and robot geometry. |
| **Local rotation** | Euler rotation relative to the mount, in degrees | The camera looks along its local `+Z` axis. The Scene preview is the safest way to aim it. |
| **Image width/height** | JPEG output resolution | Width may be `16`–`640`; height may be `16`–`480`. Start with `640 x 360`. |
| **Vertical field of view** | Vertical lens angle | `70°` is a useful starting point. Increase it for more coverage or decrease it for a tighter view. |
| **Near clip** | Closest rendered distance, in metres | Start at `0.03`. Raise it only if very close geometry causes problems. |
| **Far clip** | Farthest rendered distance, in metres | Start at `50`. It must be greater than the near clip distance. |

The **Add virtual camera** button remains disabled while a field is invalid. In particular, clip planes must satisfy `0 < near < far`.

### Example: chassis-mounted navigation camera

Use this as a starting point, then adjust it against the actual robot model:

| Field | Example value |
|---|---|
| Camera ID | `frontNav` |
| Mount transform | `<Robot Root>` |
| Local position | `(0, 0.6, 0.5)` |
| Local rotation | `(0, 0, 0)` |
| Image size | `640 x 360` |
| Vertical FOV | `70` |
| Near / far clip | `0.03 / 50` |

Before adding it, inspect the cyan frustum from the side and from above. The origin should not be inside a bumper, frame rail, or mechanism, and the forward arrow should point toward the intended scene.

### Team 118 Limelight cameras

These cameras were added with **MoSimulator > RL > Virtual Camera Tool** and are currently mounted at the robot root. Their positions and rotations are intentionally editable in the Scene view; after changing them, save the prefab and rebuild the graphical player.

| Camera ID | Output | Vertical FOV | Mount |
|---|---|---|---|
| `LimeLightFrontLeft` | `640 x 360` JPEG | `82°` | `<Robot Root>` |
| `LimelightLeftBack` | `640 x 360` JPEG | `82°` | `<Robot Root>` |
| `LimelightRightBack` | `640 x 360` JPEG | `82°` | `<Robot Root>` |

Query the exact robot-relative poses with `list_virtual_cameras()`. The IDs must match capitalization exactly when passed to Python or `mosim-camera-preview`.

## Placement recommendations

- Mount to the robot root when the sensor represents a rigid chassis camera.
- Mount to a moving child transform only when that motion is intentional. Its reported pose and captured view will follow the mechanism.
- Keep the lens just outside visible robot geometry to prevent the frame from being blocked.
- Use separate IDs for purpose-specific views, such as `frontNav`, `intakeView`, and `rearAlign`.
- Start at `640 x 360`, 10 FPS, and JPEG quality 80–85. Increase resolution or quality only when the task needs the extra detail.
- Prefer a moderate field of view. Very wide views cover more area but make distant game pieces occupy fewer pixels.

## Build and preview the camera

The standalone graphical player is a separate build, so every saved robot or prefab edit must be rebuilt before Python can see it. This includes adding, moving, rotating, renaming, or removing a camera, as well as changing its output or lens settings. Editor-only changes and unsaved Play Mode changes do not require a rebuild.

After saving a camera or robot edit, rebuild the graphical development player:

On Linux:

```bash
scripts/build_unity.sh development
```

On native Windows:

```bat
scripts\setup_windows.bat build
```

Install the optional OpenCV dependency once, then start the live preview from the repository root:

```bash
conda activate mosim-rl
python -m pip install -e './python[camera]'
mosim-camera-preview --camera LimeLightFrontLeft
```

The preview command starts the platform's graphical development player, discovers the cameras on the active robot, and opens the requested view. Press `Q`, `Esc`, or close the window to stop. Useful options include:

```bash
mosim-camera-preview --camera LimeLightFrontLeft --fps 15 --scale 2 --jpeg-quality 80
```

To display the three Team 118 Limelights in separate windows, run:

```bash
python python/examples/06_limelight_preview.py
```

The script captures the feeds sequentially, opens one OpenCV window per camera, and exits when any window is closed or you press `Q`/`Esc`. Use `--fps`, `--scale`, `--jpeg-quality`, or `--executable PATH` to adjust it. Select a subset or test a different prefab by repeating `--camera`:

```bash
python python/examples/06_limelight_preview.py \
  --camera LimeLightFrontLeft --camera LimelightLeftBack \
  --fps 15 --scale 3
```

If the player lives somewhere else, pass `--executable PATH`. You can also save a finite sequence of frames with the runnable example:

```bash
python python/examples/02_virtual_camera_capture.py \
  --camera LimeLightFrontLeft --frames 20 --fps 10 --show
```

Images are written under `runs/examples/cameras` by default.

## Capture frames from Python

Use `list_virtual_cameras()` after reset to discover the configuration loaded into the running player. This avoids silently assuming that a build contains a particular prefab edit.

```python
from mosim_rl import MoSimEnv

env = MoSimEnv(
    executable_path="_Build/RL/LinuxDevelopment/MoSimRL.x86_64",
    graphical=True,
)

try:
    env.reset(seed=7)

    cameras = env.list_virtual_cameras()
    for camera in cameras:
        print(
            camera.name,
            camera.width,
            camera.height,
            camera.robot_position,
            camera.robot_rotation_euler,
        )

    frame = env.get_virtual_camera_frame("LimeLightFrontLeft", jpeg_quality=85)
    path = frame.save("runs/cameras/limelight-front-left.jpg")
    print(path, frame.sequence, frame.sim_time)
finally:
    env.close()
```

`VirtualCameraFrame.image_bytes` contains decoded JPEG bytes. Pillow, OpenCV, PyTorch, or another image library can consume them directly. Calling `frame.save(path)` creates missing parent directories and writes the JPEG.

Camera capture does not advance simulation physics. Each response includes the current simulation time and a monotonically increasing sequence number for that camera instance.

### Vectorized environments

Only request images from a graphical worker, and wait until `step_wait()` has completed. The protocol permits only one outstanding request per worker.

```python
frames = vec_env.env_method(
    "get_virtual_camera_frame",
    "LimeLightFrontLeft",
    jpeg_quality=80,
    indices=0,
)
frame = frames[0]
```

## Change or remove a camera

Return to **MoSimulator > RL > Virtual Camera Tool**, select the same robot and prefab, and find the camera under **Configured cameras**. Select **Remove** to delete it safely. To change its ID, mount, output size, or lens settings through the tool, remove it and add it again with the new values.

Rebuild the development player after the edit. Because the prefab is a tracked project asset, review the generated change before committing it:

```bash
git diff -- Assets/Prefabs/Reefscape/Robots/118/118.prefab
```

Use the editor tool instead of hand-editing Unity prefab YAML.

## Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| **Add virtual camera** is disabled | Read the warning above the button. Check the ID characters and uniqueness, image dimensions, and clip-plane order. |
| The tool shows no robot | Wait for Unity compilation to finish, select **Refresh**, and verify that the robot has a `RobotMetadataSO` asset with an editable prefab. |
| `the active robot has no configured virtual cameras` | The wrong robot/prefab may have been edited, or the graphical player was not rebuilt after the prefab change. |
| `camera_not_found` | IDs are case-sensitive. Print `list_virtual_cameras()` and use one of the returned names. |
| `camera_name_ambiguous` | More than one `RobotVirtualCamera` has the same ID, usually after a manual duplicate. Remove or rename the duplicate in the prefab. |
| `camera_rendering_unavailable` | The normal Dedicated Server runs with `-nographics`. Use the graphical development player for capture. A headless worker can still list camera metadata. |
| The image is blocked by the robot | Move the camera outside nearby geometry or choose the correct mount transform. Check the Scene preview from several angles. |
| The view points the wrong way | The camera looks along local `+Z`. Reopen the prefab and use the rotation handle and forward arrow to aim it. |
| `camera_frame_too_large` | Reduce output dimensions and/or JPEG quality. |
| `camera_capture_failed` | Confirm that a graphics device is available and that the current render pipeline supports camera render requests; then inspect the Unity player log for the detailed warning. |
| A camera moves unexpectedly | It is parented to a moving mechanism. Remove it and recreate it on `<Robot Root>` or a rigid chassis transform. |

## Current limits

- Capture requires a graphical player with a graphics device. Dedicated Server workers launched with `-nographics` cannot render frames.[^unity-player-arguments]
- Output is RGB JPEG only. Depth, segmentation masks, and other sensor types are not implemented.
- Width is limited to 640 pixels and height to 480 pixels.
- JPEG quality must be an integer from 1 through 95.
- A JPEG larger than 700,000 bytes is rejected before base64 expansion so the complete wire frame stays below the 1 MiB protocol limit.
- Frames are requested on demand; the camera does not have an independent capture loop.
- Camera pixels are not part of the current Gymnasium observation, and the provided PPO policy is state-based.

## Implementation references

- The editor workflow is implemented in [`Assets/Editor/RobotVirtualCameraTool.cs`](../Assets/Editor/RobotVirtualCameraTool.cs).
- On-demand rendering is implemented in [`Assets/Scripts/RLBridge/RobotVirtualCamera.cs`](../Assets/Scripts/RLBridge/RobotVirtualCamera.cs).
- The Python data types are in [`python/src/mosim_rl/camera.py`](../python/src/mosim_rl/camera.py).
- A complete capture example is in [`python/examples/02_virtual_camera_capture.py`](../python/examples/02_virtual_camera_capture.py).
- The JSON commands and error contract are documented in [the wire protocol](PROTOCOL.md#virtual-camera-commands).

The capture path uses Unity's supported render-request API for the project's Universal Render Pipeline (URP), with `Camera.Render` as the built-in-pipeline fallback.[^unity-render-request][^unity-camera-render]

[^unity-camera-render]: Unity Technologies. “[Camera.Render](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.Render.html).” *Unity 2022.3 Scripting API*. Accessed August 11, 2026.
[^unity-render-request]: Unity Technologies. “[RenderPipeline.SubmitRenderRequest](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Rendering.RenderPipeline.SubmitRenderRequest.html).” *Unity 2023.2 Scripting API*. Accessed August 11, 2026.
[^unity-player-arguments]: Unity Technologies. “[Unity Standalone Player command line arguments](https://docs.unity3d.com/2023.2/Documentation/Manual/PlayerCommandLineArguments.html).” *Unity 2023.2 Manual*. Accessed August 11, 2026.
