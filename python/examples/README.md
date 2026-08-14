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
| Virtual-camera frames | `python python/examples/02_virtual_camera_capture.py --camera frontLeft --show` | Camera discovery, JPEG capture, saving frames, and OpenCV decoding |
| Vectorized workers | `python python/examples/03_vectorized_rollout.py --num-envs 4 --steps 100` | Batched SB3 `VecEnv` observations, actions, rewards, and automatic resets |
| PPO checkpoint | `python python/examples/04_ppo_policy_rollout.py runs/RUN/ppo_final.zip --vecnormalize runs/RUN/vecnormalize.pkl --graphical` | Loading a trained PPO policy and optional `VecNormalize` statistics |
| Controller driving | `python python/examples/05_controller_driver_control.py` | Physical controller input through the registered Gymnasium gamepad environment |

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

For the controller example, connect an SDL-compatible Xbox or PlayStation controller before running it. Use `--controller 1` to select a second controller, `--deadzone 0.15` to adjust stick drift, or `--with-preload` to retain the official starting coral. Press the controller's **Back/Share** button or `Ctrl-C` to exit.

See the [environment contract](../../docs/ENVIRONMENT.md) for action/observation definitions and the [virtual-camera guide](../../docs/VIRTUAL_CAMERAS.md) for camera limits.
