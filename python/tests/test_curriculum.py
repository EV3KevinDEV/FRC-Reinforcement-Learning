from __future__ import annotations

from mosim_rl.curriculum import CurriculumManager


def test_promotes_at_seventy_percent_over_one_hundred_subgoals() -> None:
    curriculum = CurriculumManager(stage=1)
    promoted = False
    for success in [True] * 70 + [False] * 30:
        promoted = curriculum.record_subgoal(success) or promoted
    assert promoted
    assert curriculum.stage == 2


def test_fixed_curriculum_never_promotes() -> None:
    curriculum = CurriculumManager(stage=3, automatic=False)
    for _ in range(100):
        curriculum.record_subgoal(True)
    assert curriculum.stage == 3
