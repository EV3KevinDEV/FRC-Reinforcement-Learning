# MoSimulator RL roadmap

V1 deliberately proves the state-based, blue-alliance Team 118 coral-cycling pipeline before widening the task.

## Visual perception

- Add configurable virtual cameras mounted to each robot, with robot-relative camera transforms, resolution, field of view, and capture rate defined by robot metadata.
- Add an image-observation mode for RGB and optional depth/segmentation frames while retaining the current state-vector mode for debugging and baseline comparisons.
- Extend the RL protocol and vectorized environment to transport camera observations efficiently, then add a visual encoder policy and camera-specific observation tests.
- Add camera calibration, lighting/occlusion randomization, and sim-to-real validation so visual policies do not depend on privileged simulator coordinates.

## Robot adapters

- Add a Team 2910 adapter with the same normalized drive/manipulator contract and robot-specific telemetry mapping.
- Extract Team 118 control into an `IExternalRobotAdapter` interface selected from robot metadata.
- Define a mod-facing adapter package so custom robots can declare setpoints, possession sensors, action mappings, and telemetry without modifying the bridge.
- Add compatibility/version checks for adapter packages and prefab validation in the Unity editor.

## Game coverage

- Add algae acquisition, processor scoring, net scoring, defense-safe task selection, and algae reward terms.
- Add park, shallow-cage, and deep-cage actions and endgame curricula.
- Expand scenario randomization for station timing, loose game pieces, spawn poses, contact, and mechanism variation.
- Add red-alliance mirroring and official multi-robot field occupancy.

## Multi-agent learning

- Expose independent per-robot observations/actions over the versioned protocol.
- Add cooperative alliance rewards, opponent/self-play policies, policy pools, and Elo-style evaluation.
- Support PettingZoo parallel environments while retaining the single-agent Gymnasium API.
- Investigate deterministic replay/state capture for regression and offline learning.

## Imitation learning

- Add teleoperated data recording for observations, actions, rewards, simulator state, timestamps, and episode metadata in a training-ready dataset format.
- Provide dataset validation, deterministic replay, and demonstrations export for behavior cloning and offline reinforcement learning.

## Scale and performance

- Profile physics, field scripts, addressables, and robot mechanisms across 1/2/4/8/12/16 workers.
- Add worker affinity and job-worker tuning based on benchmark results.
- Explore multiple isolated physics scenes only after global singleton and scene-wide lookup dependencies are removed.
- Keep process-level headless vectorization as the supported path; Unity scene replication is not equivalent to Isaac Lab GPU vectorization.
