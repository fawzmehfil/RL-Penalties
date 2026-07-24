"""Scripted goalkeeper baselines for Stage 2."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np


ACTION_COUNT = 9


@dataclass(frozen=True)
class StandCenter:
    """Always hold position from the reset center."""

    def act(self, action_mask: np.ndarray | None = None) -> int:
        return 0


@dataclass
class RandomLegal:
    """Uniformly sample one currently legal discrete goalkeeper action."""

    seed: int = 20260724

    def __post_init__(self) -> None:
        self._rng = np.random.default_rng(self.seed)

    def act(self, action_mask: np.ndarray | None = None) -> int:
        legal = np.arange(ACTION_COUNT, dtype=np.int32)
        if action_mask is not None:
            # ML-Agents marks unavailable discrete actions as True.
            mask = np.asarray(action_mask, dtype=bool)
            legal = legal[~mask]
        if len(legal) == 0:
            return 0
        return int(self._rng.choice(legal))
