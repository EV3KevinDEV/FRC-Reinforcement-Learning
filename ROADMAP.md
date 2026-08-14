# MoSimulator RL roadmap

V1 deliberately proves the state-based, blue-alliance Team 118 coral-cycling pipeline before widening the task.

## Visual perception

- [x] Add an editor tool for configurable virtual cameras mounted to robot prefabs, including robot-relative transforms, resolution, field of view, clip planes, and unique IDs.
- [x] Add on-demand RGB JPEG retrieval to the RL protocol and typed Python environment API.
- Add scheduled capture rates and buffered frame delivery for policies that need a fixed sensor cadence.
- Add an image-observation mode for RGB and optional depth/segmentation frames while retaining the current state-vector mode for debugging and baseline comparisons.
- Add batched camera observations to the vectorized environment, then add a visual encoder policy and image-observation tests.
- Add camera calibration, lighting/occlusion randomization, and sim-to-real validation so visual policies do not depend on privileged simulator coordinates.

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
