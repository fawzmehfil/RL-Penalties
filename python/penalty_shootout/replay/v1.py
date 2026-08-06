"""Validation for the Stage 7 penalty-replay-v1 JSON format."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any, Mapping


class ReplayValidationError(ValueError):
    """Raised when a replay violates penalty-replay-v1."""


def load_replay(path: str | Path) -> dict[str, Any]:
    replay_path = Path(path)
    try:
        payload = json.loads(replay_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ReplayValidationError(f"Could not read replay: {exc}") from exc
    validate_replay(payload)
    return payload


def validate_replay(payload: Mapping[str, Any]) -> None:
    _require(payload.get("ReplayContractId") == "penalty-replay-v1", "replay contract")
    _require(payload.get("SetContractId") == "penalty-set-v1", "set contract")
    _require(payload.get("InputContractId") == "player-penalty-input-v1", "input contract")
    _require(payload.get("ShotContractId") == "player-shot-v1", "shot contract")
    _require(payload.get("ShotPhysicsId") == "football-flight-v1", "physics contract")
    _require(payload.get("ScenarioSuiteId") == "player-interactive-v1", "scenario contract")
    _require(isinstance(payload.get("SessionId"), str) and payload["SessionId"], "session id")
    _require(_positive_int(payload.get("SessionSeed")), "session seed")
    for field in ("InputConfigHash", "InterceptionModelHash", "TimingModelHash"):
        value = payload.get(field)
        _require(_sha256(value), f"{field} sha256")

    score = payload.get("Score")
    _require(isinstance(score, Mapping), "score")
    _require(score.get("ValidShots") == 5, "five valid shots")
    _require(
        score.get("Goals", 0) + score.get("Saves", 0) + score.get("Misses", 0) == 5,
        "score total",
    )

    attempts = payload.get("Attempts")
    _require(isinstance(attempts, list) and len(attempts) == 5, "five attempts")
    previous_attempt_id = 0
    for expected_index, attempt in enumerate(attempts):
        _require(isinstance(attempt, Mapping), "attempt object")
        _require(attempt.get("SetShotIndex") == expected_index, "shot index order")
        attempt_id = attempt.get("AttemptId")
        _require(_positive_int(attempt_id) and attempt_id > previous_attempt_id, "attempt id order")
        previous_attempt_id = attempt_id
        _require(attempt.get("HasLaunch") is True, "launch event")
        _require(attempt.get("Outcome") in {1, 2, 3, 4, 5, 6}, "terminal outcome")
        _require(isinstance(attempt.get("Request"), Mapping), "shot request")
        frames = attempt.get("Frames")
        commands = attempt.get("KeeperCommands")
        contacts = attempt.get("Contacts")
        _require(isinstance(frames, list) and frames, "fixed frames")
        _require(isinstance(commands, list) and commands, "keeper commands")
        _require(isinstance(contacts, list), "contact events")
        _validate_monotonic(frames, "AttemptTime", "frame time")
        _validate_monotonic(commands, "PhysicsTick", "command tick")

    _require(_all_finite(payload), "finite numeric values")


def _validate_monotonic(rows: list[Any], field: str, label: str) -> None:
    previous = -math.inf
    for row in rows:
        _require(isinstance(row, Mapping), f"{label} row")
        value = row.get(field)
        _require(isinstance(value, (int, float)) and value >= previous, label)
        previous = value


def _all_finite(value: Any) -> bool:
    if isinstance(value, bool) or value is None or isinstance(value, str):
        return True
    if isinstance(value, (int, float)):
        return math.isfinite(value)
    if isinstance(value, Mapping):
        return all(_all_finite(item) for item in value.values())
    if isinstance(value, list):
        return all(_all_finite(item) for item in value)
    return False


def _positive_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value > 0


def _sha256(value: Any) -> bool:
    return (
        isinstance(value, str)
        and len(value) == 64
        and all(character in "0123456789abcdef" for character in value.lower())
    )


def _require(condition: bool, label: str) -> None:
    if not condition:
        raise ReplayValidationError(f"Invalid {label}.")
