# Gymnasium API examples

These scripts are small, runnable examples of the public `mosim_rl` Python APIs. Run them from the repository root after creating the environment and building the required Unity player:

```bash
conda activate mosim-rl
scripts/build_unity.sh all
```

| Example | Command | Demonstrates |
|---|---|---|
| Random Gymnasium rollout | `python python/examples/01_gymnasium_random_rollout.py --steps 100` | `gym.make`, spaces, `reset`, five-value `step`, episode reset, and cleanup |
| Graphical random rollout | `python python/examples/01_gymnasium_random_rollout.py --graphical --steps 100` | The same Gym API with the graphical development player |
| Virtual-camera frames | `python python/examples/02_virtual_camera_capture.py --camera LimeLightFrontLeft --show` | Camera discovery, JPEG capture, saving frames, and OpenCV decoding |
| Limelight windows | `python python/examples/06_limelight_preview.py` | Separate OpenCV windows for `LimeLightFrontLeft`, `LimelightLeftBack`, and `LimelightRightBack` |
| Vectorized workers | `python python/examples/03_vectorized_rollout.py --num-envs 4 --steps 100` | Batched SB3 `VecEnv` observations, actions, rewards, and automatic resets |
| PPO checkpoint | `python python/examples/04_ppo_policy_rollout.py runs/RUN/ppo_final.zip --vecnormalize runs/RUN/vecnormalize.pkl --graphical` | Loading a trained PPO policy and optional `VecNormalize` statistics |
| Controller driving | `python python/examples/05_controller_driver_control.py --camera-mode field --drive-mode field --windowed-fullscreen` | Physical controller input with fixed-style camera, field-oriented driving, and optional borderless fullscreen |

The standard Gymnasium environment returns:

```python
observation, info = env.reset(seed=0)
observation, reward, terminated, truncated, info = env.step(action)
```

`MoSimVecEnv` follows Stable-Baselines3's vector API instead:

```python
observations = env.reset()
observations, rewards, dones, infos = env.step(actions)
```

Graphical and camera examples use `_Build/RL/LinuxDevelopment/MoSimRL.x86_64`. Headless examples use `_Build/RL/LinuxServer/MoSimRL.x86_64`. Every script accepts `--executable PATH` to override the default. Run one example at a time unless you intentionally want several Unity workers.

For the controller example, connect an SDL-compatible Xbox or PlayStation controller before running it. It samples controls at the normal 50 Hz FRC cadence. Use `--controller 1` to select a second controller, `--deadzone 0.15` to adjust stick drift, `--windowed-fullscreen` for a borderless desktop-sized Unity window, or `--with-preload` to retain the official starting coral. The script prints the complete layout at startup. Coral A/B/X/Y select L1/L2/L3/L4. For algae, B/X select low/high reef pickup, A selects stack pickup while empty or processor while holding algae, and Y selects the barge/net while holding algae; LT runs the intake roller and RT scores. D-pad up/left select the robot and intake modes, D-pad down stows, and D-pad right is unbound; LB/RB auto-align; left-stick click cycles climb; right-stick click flips the camera; Start resets; and Back/Share exits. Overlapping target buttons use the newest press, then fall back to another target that remains held.

See the [environment contract](../../docs/ENVIRONMENT.md) for action/observation definitions and the [virtual-camera guide](../../docs/VIRTUAL_CAMERAS.md) for camera limits.
