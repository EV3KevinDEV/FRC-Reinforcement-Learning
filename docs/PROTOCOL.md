# MoSimulator RL protocol v1

The TCP wire command remains a six-value semantic robot command. Python's optional 25-value NitroGen gamepad adapter runs before framing, allowing physical controllers and gamepad-output policies to share the same verified Unity bridge without changing protocol version 1.

The bridge listens only on the configured loopback address. Each TCP frame is a four-byte unsigned big-endian length followed by that many bytes of UTF-8 JSON. The maximum JSON payload is 1 MiB.

Every request has this envelope:

```json
{"v":1,"id":1,"cmd":"ping","payload":{}}
```

Every response echoes `v` and `id`:

```json
{"v":1,"id":1,"ok":true,"error":null,"payload":{}}
```

The server rejects malformed frames, non-positive or stale request IDs, unknown commands, and incompatible protocol versions. A client must have only one outstanding request per connection.

Commands:

- `hello`: negotiate protocol, action/observation dimensions, Team 118, native physics timestep, control timestep, decision timestep, frame skip, and virtual-camera capabilities.
- `reset`: reset the scene using `seed`, `curriculum_stage`, `scenario`, and `frame_skip`.
- `step`: apply one six-value normalized action for `frame_skip` 20 ms control quanta. The bridge runs however many native physics steps are needed and carries fractional-step error forward.
- `list_cameras`: list virtual-camera calibration on the active robot.
- `get_camera_frame`: render one named virtual camera as a base64-encoded JPEG without advancing physics.
- `ping`: liveness and timing metadata.
- `close`: acknowledge and terminate the Unity player.

The socket reader runs on a background thread. It queues raw payloads only; parsing and all Unity object access happen on Unity's main thread.

MoSimulator `v26.2.0` is authored around a roughly 4.5 ms (222.2 Hz) PhysX timestep. Protocol `fixed_dt` reports that native value, `control_dt` is 0.02 seconds, and the default `decision_dt` is `5 * 0.02 = 0.1` seconds. A policy action therefore spans alternating native-step counts while remaining 10 Hz over time. The bridge never enlarges the native physics timestep.

## Virtual-camera commands

`hello` reports `virtual_camera_api: true` and `camera_rendering_available`. The latter is false for the normal `-nographics` Dedicated Server worker.

Camera metadata request:

```json
{"v":1,"id":3,"cmd":"list_cameras","payload":{}}
```

Its response contains zero or more cameras sorted by case-sensitive name:

```json
{
  "v": 1,
  "id": 3,
  "ok": true,
  "error": null,
  "payload": {
    "virtual_camera_api": true,
    "camera_rendering_available": true,
    "cameras": [
      {
        "name": "front",
        "width": 640,
        "height": 360,
        "vertical_fov_degrees": 70.0,
        "near_clip": 0.03,
        "far_clip": 50.0,
        "robot_position": [0.0, 0.5, 0.4],
        "robot_rotation_euler": [0.0, 0.0, 0.0]
      }
    ]
  }
}
```

`robot_position` is in metres and `robot_rotation_euler` is in degrees, both relative to the robot root.

Frame request:

```json
{
  "v": 1,
  "id": 4,
  "cmd": "get_camera_frame",
  "payload": {"camera_name": "front", "jpeg_quality": 85}
}
```

Frame response:

```json
{
  "v": 1,
  "id": 4,
  "ok": true,
  "error": null,
  "payload": {
    "virtual_camera_api": true,
    "camera_rendering_available": true,
    "camera_frame": {
      "name": "front",
      "width": 640,
      "height": 360,
      "encoding": "jpeg",
      "media_type": "image/jpeg",
      "image_base64": "/9j/4AAQSkZJRg...",
      "sequence": 1,
      "sim_time": 0.5
    }
  }
}
```

JPEG quality is required to be in `[1, 95]`. Camera-specific errors are `robot_not_ready`, `camera_rendering_unavailable`, `camera_name_required`, `invalid_jpeg_quality`, `camera_not_found`, `camera_name_ambiguous`, `camera_capture_failed`, and `camera_frame_too_large`. See [Robot virtual cameras](VIRTUAL_CAMERAS.md) for editor and Python usage.
