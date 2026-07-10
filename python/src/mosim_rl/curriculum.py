from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field


STAGE_NAMES = (
    "drive_leave",
    "preloaded_l1_l2",
    "preloaded_all_levels",
    "short_cycle",
    "official_match",
)


@dataclass(slots=True)
class CurriculumManager:
    stage: int = 0
    automatic: bool = True
    promotion_window: int = 100
    promotion_threshold: float = 0.70
    _history: deque[bool] = field(init=False)

    def __post_init__(self) -> None:
        self.stage = max(0, min(self.stage, len(STAGE_NAMES) - 1))
        self._history = deque(maxlen=self.promotion_window)

    @property
    def name(self) -> str:
        return STAGE_NAMES[self.stage]

    def record_subgoal(self, success: bool) -> bool:
        self._history.append(bool(success))
        if (
            self.automatic
            and self.stage < len(STAGE_NAMES) - 1
            and len(self._history) == self.promotion_window
            and sum(self._history) / len(self._history) >= self.promotion_threshold
        ):
            self.stage += 1
            self._history.clear()
            return True
        return False

    def reset_options(self) -> dict[str, int | str]:
        return {"curriculum_stage": self.stage, "scenario": self.name}
