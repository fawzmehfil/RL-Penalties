import json

import pytest

from penalty_shootout.replay import ReplayValidationError, load_replay, validate_replay


def _valid_replay():
    attempts = []
    for index in range(5):
        attempts.append(
            {
                "SetShotIndex": index,
                "AttemptId": index + 1,
                "Request": {"TimingQuality": 0.8},
                "HasLaunch": True,
                "Outcome": 1 if index < 3 else 2,
                "Frames": [
                    {"AttemptTime": 0.0, "PhysicsTick": 0},
                    {"AttemptTime": 0.02, "PhysicsTick": 1},
                ],
                "KeeperCommands": [
                    {"PhysicsTick": 0},
                    {"PhysicsTick": 1},
                ],
                "Contacts": [],
            }
        )
    return {
        "ReplayContractId": "penalty-replay-v1",
        "SetContractId": "penalty-set-v1",
        "InputContractId": "player-penalty-input-v1",
        "ShotContractId": "player-shot-v1",
        "ShotPhysicsId": "football-flight-v1",
        "ScenarioSuiteId": "player-interactive-v1",
        "SessionId": "abc",
        "SessionSeed": 123,
        "InputConfigHash": "a" * 64,
        "InterceptionModelHash": "b" * 64,
        "TimingModelHash": "c" * 64,
        "Score": {"ValidShots": 5, "Goals": 3, "Saves": 2, "Misses": 0},
        "Attempts": attempts,
    }


def test_replay_v1_round_trip(tmp_path):
    path = tmp_path / "replay.json"
    path.write_text(json.dumps(_valid_replay()), encoding="utf-8")
    replay = load_replay(path)
    assert replay["Score"]["Goals"] == 3


def test_replay_v1_rejects_incomplete_set():
    replay = _valid_replay()
    replay["Attempts"].pop()
    with pytest.raises(ReplayValidationError, match="five attempts"):
        validate_replay(replay)


def test_replay_v1_rejects_non_monotonic_frames():
    replay = _valid_replay()
    replay["Attempts"][0]["Frames"][1]["AttemptTime"] = -1.0
    with pytest.raises(ReplayValidationError, match="frame time"):
        validate_replay(replay)
