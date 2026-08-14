# Provenance, modification, and research-use notice

This file records the origin and modification status of this repository. It is intended to support attribution and license compliance; it is not legal advice or a warranty of publication clearance.

## Upstream source snapshot

This repository is an independently maintained derivative of:

- Project: **MoSimulator Public Repository**
- Copyright text included in the tagged `LICENSE`: **MoSimulator Copyright (C) 2025 Cascade Studios**
- Upstream repository: https://github.com/MoSimulator/MoSimulator-Public
- Source snapshot: tag [`v26.2.0`](https://github.com/MoSimulator/MoSimulator-Public/tree/v26.2.0)
- Source commit: [`9dd3d7d1d04529d82c98d049f2fc273ebb1e7213`](https://github.com/MoSimulator/MoSimulator-Public/commit/9dd3d7d1d04529d82c98d049f2fc273ebb1e7213)
- License at that snapshot: [GNU General Public License version 3](https://github.com/MoSimulator/MoSimulator-Public/blob/v26.2.0/LICENSE)

As of August 11, 2026, the [current upstream `main` branch license](https://github.com/MoSimulator/MoSimulator-Public/blob/main/LICENSE) uses different, restrictive terms. Those later terms are not represented here as the license for this fork's pinned `v26.2.0` source basis. Conversely, this notice does not authorize importing any later upstream material: review the license attached to every newer source snapshot before copying or merging it.

## Modified-work notice

This is not an unmodified or official MoSimulator distribution. EV3KevinDEV and repository contributors made material changes during 2026, including:

- the Gymnasium, Stable-Baselines3, TCP protocol, vector-worker, reward, observation, curriculum, evaluation, and training systems;
- external robot-control and simulator integration changes;
- benchmarking, testing, verification, and build automation;
- TensorBoard and experiment-tracking integration;
- robot-mounted virtual-camera tooling and frame APIs; and
- research, protocol, environment, and usage documentation.

These changes are distributed under GPL-3.0-only, consistent with the repository [`LICENSE`](LICENSE). Copyright in upstream material remains with its original holders; copyright in modifications remains with the respective contributors to the extent copyright subsists.

If an executable is distributed with a paper or artifact package, provide the complete corresponding source and build/install scripts for that exact executable under the GPL. Do not provide only a patch against a changing upstream branch. Preserve this notice, the upstream attribution, and the repository license.

## Generative-AI assistance

Substantial portions of the RL-specific code, tests, tooling, debugging, and documentation were produced or revised with generative-AI assistance, including OpenAI Codex, under maintainer direction. This is sometimes described informally as “vibe coding.” No claim is made that every generated change received independent human review, and this disclosure must not be used as a substitute for validation.

Research authors are responsible for:

- reviewing the code paths and claims material to their experiments;
- documenting the AI system, model/version when known, dates, and tasks as required by their venue;
- reporting which tests and empirical validations were actually performed; and
- not listing an AI system as a human author or implying that it accepts responsibility for the work.

## Third-party rights and non-endorsement

The repository includes or depends on third-party code, packages, fonts, names, robot designs, visual assets, and marks that may carry their own terms. Files containing separate notices remain subject to those notices. The GPL license for the pinned MoSimulator source snapshot does not grant trademark rights or automatically clear every screenshot, extracted asset, trained model, dataset, or publication figure.

This fork is not affiliated with or endorsed by Cascade Studios LLP, FIRST, or any referenced FRC team. MoSimulator, FIRST, FRC, REEFSCAPE, team names, robot names, logos, and related marks belong to their respective owners. Before publication, obtain any permissions required by the venue, institution, asset license, or applicable law.

See [`docs/RESEARCH_PUBLICATION.md`](docs/RESEARCH_PUBLICATION.md) for the publication checklist.
