# Robot virtual cameras

The virtual-camera tool attaches named, on-demand sensor cameras to robot prefabs. A camera stays disabled during normal rendering and renders only when the RL client requests a frame. The capture path uses Unity's supported render-request API for the project's Universal Render Pipeline (URP), with manual `Camera.Render` as the built-in-pipeline fallback.[^unity-render-request][^unity-camera-render]

## Configure a robot

1. Open the Unity project.
2. Select **MoSimulator > RL > Virtual Camera Tool**.
3. Choose the robot metadata asset and, when available, its primary or alternate prefab.
4. Choose a mount transform. Position and rotation are local to that transform.
5. Enter a unique camera ID, output size, vertical field of view, and clip planes.
6. Select **Add virtual camera**.

The tool edits the selected prefab asset, so review and commit that prefab change with the scripts. IDs are case-sensitive and may contain letters, numbers, `_`, and `-`. The supported output range is 16–640 pixels wide and 16–480 pixels high; `320 x 180` is the default.

The **Configured cameras** section shows every camera already attached to the selected prefab and can remove a camera that the tool created. Camera metadata returned over the API expresses position in metres and rotation in Euler degrees relative to the robot root.

## Capture from Python

Build and launch the graphical development player. The normal Dedicated Server worker is started with `-nographics`, which Unity documents as not initializing a graphics device, so it can list camera metadata but cannot render frames.[^unity-player-arguments]

```python
from mosim_rl import MoSimEnv

env = MoSimEnv(
    executable_path="_Build/RL/LinuxDevelopment/MoSimRL.x86_64",
    graphical=True,
)

try:
    env.reset(seed=7)

    for camera in env.list_virtual_cameras():
        print(camera.name, camera.width, camera.height, camera.robot_position)

    frame = env.get_virtual_camera_frame("front", jpeg_quality=85)
    output = frame.save("runs/cameras/front.jpg")
    print(output, frame.sequence, frame.sim_time)
finally:
    env.close()
```

`VirtualCameraFrame.image_bytes` contains the decoded JPEG, so Pillow, OpenCV, PyTorch, or another image stack can consume it without first decoding base64. The package itself does not require an image-library dependency.

### Live OpenCV preview

Install the optional camera dependency and launch the graphical development player through the preview command:

```bash
python -m pip install -e './python[camera]'
mosim-camera-preview --camera frontLeft
```

The command discovers the cameras on the active robot, requests JPEG frames through the same RL protocol used by policies, decodes them with OpenCV, and displays a scaled live view. The window overlays the camera ID, capture sequence, and simulation time. Press `Q`, `Esc`, or close the window to stop both the viewer and its Unity player. Use `--fps`, `--scale`, and `--jpeg-quality` to adjust the preview.

With `MoSimVecEnv`, query only the graphical worker and only after `step_wait` has completed:

```python
frames = vec_env.env_method(
    "get_virtual_camera_frame",
    "front",
    jpeg_quality=80,
    indices=0,
)
frame = frames[0]
```

Camera capture does not advance physics. The response reports the current simulation time and a monotonically increasing sequence number for that camera instance. A request made while a policy step is pending is rejected to preserve the protocol's one-outstanding-request rule.

## Limits and errors

- Frames use JPEG because every wire frame must remain below the protocol's 1 MiB limit. A camera JPEG above 700,000 bytes is rejected as `camera_frame_too_large` before base64 expansion.
- JPEG quality must be an integer from 1 through 95.
- Capture from a headless worker returns `camera_rendering_unavailable`.
- Missing or duplicate IDs return `camera_not_found` or `camera_name_ambiguous`.
- The API returns RGB JPEG only. Depth, segmentation, visual-policy observations, and configurable capture rates remain future work.

[^unity-camera-render]: Unity Technologies. “[Camera.Render](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.Render.html).” *Unity 2022.3 Scripting API*. Accessed August 11, 2026.
[^unity-render-request]: Unity Technologies. “[RenderPipeline.SubmitRenderRequest](https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Rendering.RenderPipeline.SubmitRenderRequest.html).” *Unity 2023.2 Scripting API*. Accessed August 11, 2026.
[^unity-player-arguments]: Unity Technologies. “[Unity Standalone Player command line arguments](https://docs.unity3d.com/2023.2/Documentation/Manual/PlayerCommandLineArguments.html).” *Unity 2023.2 Manual*. Accessed August 11, 2026.
