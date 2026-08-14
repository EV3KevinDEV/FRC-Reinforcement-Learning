# Research-publication checklist

This checklist helps authors prepare a reproducible and appropriately attributed publication based on this repository. It is not legal advice, and completing it does not replace institutional or legal review.

## Before submission

- [ ] **Pin the software.** Create a tagged release and record the exact Git commit used for every experiment. Do not cite a moving branch.
- [ ] **Archive the release.** Deposit the tagged source in a durable archive such as Zenodo and use the resulting DOI in the paper and `CITATION.cff`.
- [ ] **Verify contributor identity.** Replace the placeholder GitHub-handle author in `CITATION.cff` with verified contributor names and ORCIDs when appropriate; obtain agreement on authorship and contributor order.
- [ ] **Cite both works.** Cite the upstream MoSimulator `v26.2.0` snapshot and this RL fork using the entries in the root [`README.md`](../README.md#citation).
- [ ] **Describe modifications.** State that the environment is a modified research fork rather than an official MoSimulator feature. Reference [`NOTICE.md`](../NOTICE.md).
- [ ] **Report the method.** Record Unity, Python, package, policy, seed, curriculum, action/observation, reward, worker-count, hardware, training-budget, and checkpoint details needed to reproduce the reported results.
- [ ] **Validate the claims.** Run the relevant fast tests, Unity integration tests, environment checker, benchmark, and evaluation seeds. Report actual results; do not convert unexecuted tests or AI suggestions into claims.
- [ ] **Disclose AI assistance.** Follow the target venue's current policy and retain enough information to describe the tools and tasks accurately.
- [ ] **Package GPL artifacts correctly.** If distributing a Unity executable or other GPL-covered binary, distribute the complete corresponding source and build/install scripts for that exact artifact, together with `LICENSE` and `NOTICE.md`.
- [ ] **Audit third-party material.** Review package, font, asset, robot, logo, music, and dataset licenses. Do not assume that a source-code license grants rights to every visual asset or trademark.
- [ ] **Clear publication media.** Obtain permission or replace screenshots, videos, CAD-derived images, logos, and other figures that are not demonstrably cleared for the proposed publication.
- [ ] **Review data and weights.** Document the origin and license of demonstrations, telemetry, trained checkpoints, and generated datasets before depositing them.
- [ ] **Obtain institutional review.** Ask the corresponding author's institution, technology-transfer office, or qualified counsel to review unresolved license, trademark, privacy, export, or sponsorship questions.

## Suggested AI-assistance disclosure

Adapt this statement to what was actually done; do not copy it unchanged if the described review did not occur:

> OpenAI Codex was used to assist with portions of the software implementation, tests, debugging, and documentation. Human authors directed the tasks, selected and integrated outputs, and reviewed and validated the code and experimental claims described in this paper. The AI system was not treated as an author. Tool use was disclosed in accordance with the venue policy in effect at submission.

Record the tool name, model/version when available, access dates, task categories, and the human verification performed. If substantial generated code was not independently reviewed, say so explicitly and narrow the paper's claims accordingly.

## Minimal artifact contents

A publication artifact should contain or link immutably to:

- the tagged source release and exact commit;
- `LICENSE`, `NOTICE.md`, and `CITATION.cff`;
- environment/lock files and build instructions;
- resolved training configuration and seeds;
- evaluation scripts and raw per-seed results;
- model checkpoints and normalization state, if redistribution is cleared;
- a manifest of included third-party data and assets; and
- known limitations, failed runs, and deviations from the documented protocol.
