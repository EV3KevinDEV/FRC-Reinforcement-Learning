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

- `hello`: negotiate protocol, action/observation dimensions, Team 118, native physics timestep, control timestep, decision timestep, and frame skip.
- `reset`: reset the scene using `seed`, `curriculum_stage`, `scenario`, and `frame_skip`.
- `step`: apply one six-value normalized action for `frame_skip` 20 ms control quanta. The bridge runs however many native physics steps are needed and carries fractional-step error forward.
- `ping`: liveness and timing metadata.
- `close`: acknowledge and terminate the Unity player.

The socket reader runs on a background thread. It queues raw payloads only; parsing and all Unity object access happen on Unity's main thread.

MoSimulator `v26.2.0` is authored around a roughly 4.5 ms (222.2 Hz) PhysX timestep. Protocol `fixed_dt` reports that native value, `control_dt` is 0.02 seconds, and the default `decision_dt` is `5 * 0.02 = 0.1` seconds. A policy action therefore spans alternating native-step counts while remaining 10 Hz over time. The bridge never enlarges the native physics timestep.
