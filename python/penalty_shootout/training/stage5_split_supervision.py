from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
import random
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np
import onnxruntime as ort
import torch
from mlagents.trainers.demo_loader import load_demonstration
from torch import nn


DEFAULT_CONTRACT = Path(
    "configs/supervision/"
    "goalkeeper-control-v2-split-supervision-v1.json"
)
DEFAULT_DEMO_DIR = Path(
    "results/demonstrations/"
    "goalkeeper-control-v2-reactive-demo-v1-20k"
)
OBSERVATION_SIZE = 35
CONTINUOUS_ACTIONS = 4
CAN_COMMIT_INDEX = 29
TIMING_FEATURE_INDICES = (29, 31, 32)
TARGET_X_EXTENT = 3.55
TARGET_Y_RANGE = 2.22


@dataclass(frozen=True)
class AlignedEpisode:
    arena_id: int
    episode_ordinal: int
    observations: np.ndarray
    continuous_actions: np.ndarray
    commit_actions: np.ndarray
    commit_allowed: np.ndarray

    @property
    def key(self) -> tuple[int, int]:
        return self.arena_id, self.episode_ordinal

    @property
    def commit_index(self) -> int:
        matches = np.flatnonzero(self.commit_actions == 1)
        if len(matches) != 1:
            raise ValueError(
                f"episode {self.key} has {len(matches)} commits"
            )
        return int(matches[0])


class InterceptionModel(nn.Module):
    def __init__(self, observation_size: int = OBSERVATION_SIZE) -> None:
        super().__init__()
        self.network = nn.Sequential(
            nn.Linear(observation_size, 128),
            nn.ReLU(),
            nn.Linear(128, 128),
            nn.ReLU(),
            nn.Linear(128, CONTINUOUS_ACTIONS),
            nn.Tanh(),
        )

    def forward(self, obs_0: torch.Tensor) -> torch.Tensor:
        return self.network(obs_0)


class CommitTimingModel(nn.Module):
    def __init__(
        self,
        feature_indices: Sequence[int] = TIMING_FEATURE_INDICES,
    ) -> None:
        super().__init__()
        self.register_buffer(
            "feature_indices",
            torch.tensor(tuple(feature_indices), dtype=torch.long),
        )
        self.network = nn.Sequential(
            nn.Linear(len(feature_indices), 32),
            nn.ReLU(),
            nn.Linear(32, 32),
            nn.ReLU(),
            nn.Linear(32, 1),
        )

    def forward(self, obs_0: torch.Tensor) -> torch.Tensor:
        features = torch.index_select(obs_0, 1, self.feature_indices)
        return self.network(features)


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def stable_json_sha256(value: Any) -> str:
    encoded = json.dumps(
        value,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def validate_source_manifest(
    demo_dir: Path,
    contract: dict[str, Any],
) -> tuple[dict[str, Any], str]:
    demo_dir = demo_dir.expanduser().resolve()
    manifest_path = demo_dir / "manifest.json"
    if not manifest_path.is_file():
        raise FileNotFoundError(
            f"missing validated demonstration manifest: {manifest_path}"
        )
    manifest = load_json(manifest_path)
    expected_identity = {
        "demonstration_contract_id": contract[
            "source_demonstration_contract_id"
        ],
        "behavior_name": contract["behavior_name"],
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "scenario_suite_id": contract["scenario_suite_id"],
    }
    if manifest.get("status") != "passed":
        raise ValueError("source demonstration manifest did not pass")
    for key, expected in expected_identity.items():
        if manifest.get(key) != expected:
            raise ValueError(
                f"source manifest {key} {manifest.get(key)!r} != "
                f"{expected!r}"
            )
    if manifest.get("observation_shapes") != [[contract["observation_size"]]]:
        raise ValueError("source observation shape does not match contract")
    if int(manifest.get("continuous_actions", -1)) != int(
        contract["continuous_actions"]
    ):
        raise ValueError("source continuous action count does not match")
    if manifest.get("discrete_branches") != contract["discrete_branches"]:
        raise ValueError("source discrete branches do not match")
    expected_episodes = (
        int(contract["arena_count"])
        * int(contract["episodes_per_arena"])
    )
    if int(manifest.get("terminal_episodes", -1)) != expected_episodes:
        raise ValueError("source episode count does not match contract")

    entries = manifest.get("demonstration_files", [])
    if len(entries) != int(contract["arena_count"]):
        raise ValueError("source manifest must contain one file per arena")
    expected_names = {
        f"GKCtrlV2A{arena_id:03d}.demo"
        for arena_id in range(int(contract["arena_count"]))
    }
    actual_names = {str(entry.get("path")) for entry in entries}
    if actual_names != expected_names:
        raise ValueError("source demonstration filenames do not match arenas")
    for entry in entries:
        path = demo_dir / str(entry["path"])
        if not path.is_file():
            raise FileNotFoundError(path)
        if int(entry.get("bytes", -1)) != path.stat().st_size:
            raise ValueError(f"source demonstration size changed: {path.name}")
        actual_hash = sha256_file(path)
        if actual_hash != entry.get("sha256"):
            raise ValueError(f"source demonstration hash changed: {path.name}")
    return manifest, sha256_file(manifest_path)


def _observation(info: Any) -> np.ndarray:
    observations = getattr(info, "observations", ())
    if not observations:
        raise ValueError("demonstration row has no observations")
    values = np.asarray(
        observations[0].float_data.data,
        dtype=np.float32,
    )
    if values.shape != (OBSERVATION_SIZE,):
        raise ValueError(f"unexpected observation shape {values.shape}")
    if not np.isfinite(values).all():
        raise ValueError("demonstration contains non-finite observation")
    if np.any(np.abs(values) > 1.000001):
        raise ValueError("demonstration observation is outside [-1, 1]")
    return values


def _commit_allowed(info: Any) -> bool:
    action_mask = list(getattr(info, "action_mask", ()))
    if len(action_mask) >= 2:
        return not bool(action_mask[1])
    return bool(_observation(info)[CAN_COMMIT_INDEX] >= 0.5)


def realign_demo_pairs(
    pairs: Sequence[Any],
    arena_id: int,
) -> list[AlignedEpisode]:
    episodes: list[AlignedEpisode] = []
    pending_info: Any | None = None
    observations: list[np.ndarray] = []
    continuous_actions: list[np.ndarray] = []
    commit_actions: list[int] = []
    commit_allowed: list[bool] = []

    for pair in pairs:
        current_info = pair.agent_info
        if pending_info is not None:
            continuous = np.asarray(
                pair.action_info.continuous_actions,
                dtype=np.float32,
            )
            discrete = np.asarray(
                pair.action_info.discrete_actions,
                dtype=np.int32,
            )
            if continuous.shape != (CONTINUOUS_ACTIONS,):
                raise ValueError(
                    f"unexpected continuous action shape {continuous.shape}"
                )
            if discrete.shape != (1,) or int(discrete[0]) not in (0, 1):
                raise ValueError(
                    f"unexpected discrete action {discrete.tolist()}"
                )
            if not np.isfinite(continuous).all():
                raise ValueError("demonstration contains non-finite action")
            if np.any(np.abs(continuous) > 1.000001):
                raise ValueError("continuous action is outside [-1, 1]")
            observations.append(_observation(pending_info))
            continuous_actions.append(continuous)
            commit_actions.append(int(discrete[0]))
            commit_allowed.append(_commit_allowed(pending_info))

        if bool(current_info.done):
            if not observations:
                raise ValueError("terminal demonstration episode has no actions")
            episode = AlignedEpisode(
                arena_id=arena_id,
                episode_ordinal=len(episodes) + 1,
                observations=np.stack(observations),
                continuous_actions=np.stack(continuous_actions),
                commit_actions=np.asarray(commit_actions, dtype=np.int8),
                commit_allowed=np.asarray(commit_allowed, dtype=bool),
            )
            _validate_aligned_episode(episode)
            episodes.append(episode)
            observations = []
            continuous_actions = []
            commit_actions = []
            commit_allowed = []
            pending_info = None
        else:
            pending_info = current_info

    if pending_info is not None or observations:
        raise ValueError(f"arena {arena_id} ends with a partial episode")
    return episodes


def _validate_aligned_episode(episode: AlignedEpisode) -> None:
    row_count = len(episode.observations)
    if row_count == 0 or any(
        len(values) != row_count
        for values in (
            episode.continuous_actions,
            episode.commit_actions,
            episode.commit_allowed,
        )
    ):
        raise ValueError(f"episode {episode.key} has inconsistent rows")
    commit_index = episode.commit_index
    if not bool(episode.commit_allowed[commit_index]):
        raise ValueError(f"episode {episode.key} commits while masked")


def load_aligned_episodes(
    demo_dir: Path,
    contract: dict[str, Any],
) -> list[AlignedEpisode]:
    episodes: list[AlignedEpisode] = []
    for arena_id in range(int(contract["arena_count"])):
        path = demo_dir / f"GKCtrlV2A{arena_id:03d}.demo"
        behavior_spec, pairs, _ = load_demonstration(str(path))
        observation_shapes = [
            list(spec.shape)
            for spec in behavior_spec.observation_specs
        ]
        if observation_shapes != [[int(contract["observation_size"])]]:
            raise ValueError(f"{path.name} observation spec changed")
        action_spec = behavior_spec.action_spec
        if int(action_spec.continuous_size) != CONTINUOUS_ACTIONS:
            raise ValueError(f"{path.name} continuous action spec changed")
        if list(action_spec.discrete_branches) != [2]:
            raise ValueError(f"{path.name} discrete action spec changed")
        arena_episodes = realign_demo_pairs(pairs, arena_id)
        if len(arena_episodes) != int(contract["episodes_per_arena"]):
            raise ValueError(
                f"arena {arena_id} has {len(arena_episodes)} episodes"
            )
        episodes.extend(arena_episodes)
    return episodes


def split_episode_keys(
    episodes: Sequence[AlignedEpisode],
    contract: dict[str, Any],
) -> dict[tuple[int, int], str]:
    split = contract["split"]
    expected = {
        "train": int(split["train_per_arena"]),
        "validation": int(split["validation_per_arena"]),
        "test": int(split["test_per_arena"]),
    }
    assignments: dict[tuple[int, int], str] = {}
    for arena_id in range(int(contract["arena_count"])):
        arena_episodes = sorted(
            (episode for episode in episodes if episode.arena_id == arena_id),
            key=lambda episode: episode.episode_ordinal,
        )
        if len(arena_episodes) != sum(expected.values()):
            raise ValueError(f"arena {arena_id} cannot satisfy exact split")
        rng = np.random.default_rng(int(contract["split_seed"]) + arena_id)
        permutation = rng.permutation(len(arena_episodes))
        cursor = 0
        for split_name in ("train", "validation", "test"):
            for index in permutation[cursor:cursor + expected[split_name]]:
                assignments[arena_episodes[int(index)].key] = split_name
            cursor += expected[split_name]
    if len(assignments) != len(episodes):
        raise ValueError("episode split has missing or duplicate keys")
    return assignments


def _write_split_dataset(
    path: Path,
    episodes: Sequence[AlignedEpisode],
) -> dict[str, Any]:
    ordered = sorted(episodes, key=lambda episode: episode.key)
    offsets = [0]
    observations: list[np.ndarray] = []
    continuous: list[np.ndarray] = []
    commits: list[np.ndarray] = []
    allowed: list[np.ndarray] = []
    commit_indices: list[int] = []
    for episode in ordered:
        observations.append(episode.observations)
        continuous.append(episode.continuous_actions)
        commits.append(episode.commit_actions)
        allowed.append(episode.commit_allowed)
        commit_indices.append(episode.commit_index)
        offsets.append(offsets[-1] + len(episode.observations))
    np.savez_compressed(
        path,
        observations=np.concatenate(observations).astype(np.float32),
        continuous_actions=np.concatenate(continuous).astype(np.float32),
        commit_actions=np.concatenate(commits).astype(np.int8),
        commit_allowed=np.concatenate(allowed).astype(bool),
        episode_offsets=np.asarray(offsets, dtype=np.int64),
        episode_arena_ids=np.asarray(
            [episode.arena_id for episode in ordered],
            dtype=np.int16,
        ),
        episode_ordinals=np.asarray(
            [episode.episode_ordinal for episode in ordered],
            dtype=np.int16,
        ),
        teacher_commit_indices=np.asarray(commit_indices, dtype=np.int16),
    )
    return {
        "path": path.name,
        "bytes": path.stat().st_size,
        "sha256": sha256_file(path),
        "episodes": len(ordered),
        "decision_rows": offsets[-1],
        "first_decision_commits": sum(
            episode.commit_index == 0 for episode in ordered
        ),
    }


def extract_dataset(
    demo_dir: Path,
    output_dir: Path,
    contract_path: Path = DEFAULT_CONTRACT,
) -> dict[str, Any]:
    contract_path = contract_path.expanduser().resolve()
    contract = load_json(contract_path)
    if contract.get("schema_version") != 1:
        raise ValueError("unsupported split supervision contract")
    demo_dir = demo_dir.expanduser().resolve()
    output_dir = output_dir.expanduser().resolve()
    source_manifest, source_manifest_sha = validate_source_manifest(
        demo_dir,
        contract,
    )
    dataset_manifest_path = output_dir / "dataset-manifest.json"
    if dataset_manifest_path.exists():
        existing = load_json(dataset_manifest_path)
        if (
            existing.get("status") != "passed"
            or existing.get("supervision_contract_id")
            != contract["supervision_contract_id"]
            or existing.get("source_manifest_sha256")
            != source_manifest_sha
        ):
            raise ValueError("existing dataset manifest is not reusable")
        for entry in existing.get("dataset_files", {}).values():
            path = output_dir / entry["path"]
            if not path.is_file() or sha256_file(path) != entry["sha256"]:
                raise ValueError("existing extracted dataset changed")
        return existing
    if output_dir.exists() and any(output_dir.iterdir()):
        raise FileExistsError(
            f"non-empty supervision output has no valid manifest: {output_dir}"
        )
    output_dir.mkdir(parents=True, exist_ok=True)

    episodes = load_aligned_episodes(demo_dir, contract)
    assignments = split_episode_keys(episodes, contract)
    grouped = {
        split_name: [
            episode
            for episode in episodes
            if assignments[episode.key] == split_name
        ]
        for split_name in ("train", "validation", "test")
    }
    split_rows = [
        f"{arena_id}:{ordinal}:{assignments[(arena_id, ordinal)]}"
        for arena_id, ordinal in sorted(assignments)
    ]
    split_sha = hashlib.sha256(
        "\n".join(split_rows).encode("ascii")
    ).hexdigest()
    files = {
        name: _write_split_dataset(output_dir / f"{name}.npz", values)
        for name, values in grouped.items()
    }
    manifest = {
        "schema_version": 1,
        "status": "passed",
        "supervision_contract_id": contract["supervision_contract_id"],
        "source_demonstration_contract_id": contract[
            "source_demonstration_contract_id"
        ],
        "source_manifest_sha256": source_manifest_sha,
        "source_manifest_content_sha256": stable_json_sha256(source_manifest),
        "contract_sha256": sha256_file(contract_path),
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "alignment": contract["alignment"],
        "split_seed": int(contract["split_seed"]),
        "split_assignment_sha256": split_sha,
        "episode_counts": {
            name: len(values) for name, values in grouped.items()
        },
        "dataset_files": files,
        "total_episodes": len(episodes),
        "total_decision_rows": sum(len(item.observations) for item in episodes),
        "first_decision_commits": sum(
            item.commit_index == 0 for item in episodes
        ),
    }
    dataset_manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def _load_split(
    output_dir: Path,
    dataset_manifest: dict[str, Any],
    split_name: str,
) -> dict[str, np.ndarray]:
    entry = dataset_manifest["dataset_files"][split_name]
    path = output_dir / entry["path"]
    if sha256_file(path) != entry["sha256"]:
        raise ValueError(f"extracted {split_name} dataset hash changed")
    with np.load(path, allow_pickle=False) as values:
        return {name: values[name].copy() for name in values.files}


def _set_deterministic(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.set_num_threads(max(1, min(4, os.cpu_count() or 1)))
    torch.use_deterministic_algorithms(True)


def _train_network(
    model: nn.Module,
    train_x: np.ndarray,
    train_y: np.ndarray,
    validation_x: np.ndarray,
    validation_y: np.ndarray,
    *,
    learning_rate: float,
    batch_size: int,
    maximum_epochs: int,
    patience: int,
    seed: int,
    loss_function: nn.Module,
) -> dict[str, Any]:
    train_inputs = torch.from_numpy(train_x.astype(np.float32, copy=False))
    train_targets = torch.from_numpy(train_y.astype(np.float32, copy=False))
    validation_inputs = torch.from_numpy(
        validation_x.astype(np.float32, copy=False)
    )
    validation_targets = torch.from_numpy(
        validation_y.astype(np.float32, copy=False)
    )
    optimizer = torch.optim.Adam(model.parameters(), lr=learning_rate)
    generator = torch.Generator().manual_seed(seed)
    best_state = copy.deepcopy(model.state_dict())
    best_loss = math.inf
    best_epoch = 0
    epochs_without_improvement = 0
    history: list[dict[str, float]] = []

    for epoch in range(1, maximum_epochs + 1):
        model.train()
        permutation = torch.randperm(len(train_inputs), generator=generator)
        total_loss = 0.0
        for start in range(0, len(permutation), batch_size):
            indexes = permutation[start:start + batch_size]
            predictions = model(train_inputs[indexes])
            loss = loss_function(predictions, train_targets[indexes])
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            total_loss += float(loss.detach()) * len(indexes)
        model.eval()
        with torch.no_grad():
            validation_loss = float(
                loss_function(
                    model(validation_inputs),
                    validation_targets,
                )
            )
        train_loss = total_loss / len(train_inputs)
        history.append(
            {
                "epoch": float(epoch),
                "train_loss": train_loss,
                "validation_loss": validation_loss,
            }
        )
        if validation_loss < best_loss - 1e-8:
            best_loss = validation_loss
            best_epoch = epoch
            best_state = copy.deepcopy(model.state_dict())
            epochs_without_improvement = 0
        else:
            epochs_without_improvement += 1
            if epochs_without_improvement >= patience:
                break
    model.load_state_dict(best_state)
    return {
        "best_epoch": best_epoch,
        "epochs_run": len(history),
        "best_validation_loss": best_loss,
        "final_train_loss": history[-1]["train_loss"],
    }


def _precommit_rows(values: dict[str, np.ndarray]) -> np.ndarray:
    selected: list[np.ndarray] = []
    offsets = values["episode_offsets"]
    commits = values["teacher_commit_indices"]
    for episode_index, commit_index in enumerate(commits):
        start = int(offsets[episode_index])
        selected.append(
            np.arange(start, start + int(commit_index) + 1, dtype=np.int64)
        )
    return np.concatenate(selected)


def balanced_timing_rows(
    values: dict[str, np.ndarray],
    seed: int,
) -> tuple[np.ndarray, dict[str, int]]:
    offsets = values["episode_offsets"]
    arenas = values["episode_arena_ids"]
    ordinals = values["episode_ordinals"]
    commits = values["teacher_commit_indices"]
    allowed = values["commit_allowed"]
    labels = values["commit_actions"]
    wait_pools: dict[int, list[int]] = {}
    for arena_id in np.unique(arenas):
        rows: list[int] = []
        for episode_index in np.flatnonzero(arenas == arena_id):
            start = int(offsets[episode_index])
            commit = start + int(commits[episode_index])
            rows.extend(
                row
                for row in range(start, commit)
                if bool(allowed[row]) and int(labels[row]) == 0
            )
        if not rows:
            raise ValueError(f"arena {arena_id} has no legal wait pool")
        wait_pools[int(arena_id)] = rows

    positives: list[int] = []
    negatives: list[int] = []
    same_episode = 0
    fallback = 0
    for episode_index, commit_index in enumerate(commits):
        start = int(offsets[episode_index])
        positive = start + int(commit_index)
        positives.append(positive)
        waits = [
            row
            for row in range(start, positive)
            if bool(allowed[row]) and int(labels[row]) == 0
        ]
        selector = (
            int(arenas[episode_index]) * 13007
            + int(ordinals[episode_index]) * 101
            + seed
        )
        if waits:
            stratum = selector % 4
            if stratum == 0:
                negative = waits[0]
            elif stratum == 1:
                negative = waits[len(waits) // 2]
            elif stratum == 2:
                negative = waits[-1]
            else:
                negative = waits[selector % len(waits)]
            same_episode += 1
        else:
            pool = wait_pools[int(arenas[episode_index])]
            negative = pool[selector % len(pool)]
            fallback += 1
        negatives.append(negative)
    indexes = np.asarray(positives + negatives, dtype=np.int64)
    if len(positives) != len(negatives):
        raise AssertionError("timing dataset is not balanced")
    return indexes, {
        "commit_rows": len(positives),
        "wait_rows": len(negatives),
        "same_episode_wait_rows": same_episode,
        "same_arena_fallback_wait_rows": fallback,
    }


def interception_metrics(
    predictions: np.ndarray,
    targets: np.ndarray,
) -> dict[str, Any]:
    absolute = np.abs(predictions - targets)
    physical_aim = np.sqrt(
        ((predictions[:, 1] - targets[:, 1]) * TARGET_X_EXTENT) ** 2
        + ((predictions[:, 2] - targets[:, 2]) * TARGET_Y_RANGE / 2.0) ** 2
    )
    return {
        "move_mae": float(np.mean(absolute[:, 0])),
        "aim_x_mae": float(np.mean(absolute[:, 1])),
        "aim_y_mae": float(np.mean(absolute[:, 2])),
        "reach_mae": float(np.mean(absolute[:, 3])),
        "physical_aim_error_m": float(np.mean(physical_aim)),
        "finite": bool(np.isfinite(predictions).all()),
        "bounded": bool(np.all(np.abs(predictions) <= 1.000001)),
        "rows": int(len(predictions)),
    }


def timing_sequence_metrics(
    values: dict[str, np.ndarray],
    probabilities: np.ndarray,
    threshold: float,
) -> dict[str, Any]:
    probabilities = np.asarray(probabilities, dtype=np.float64).reshape(-1)
    offsets = values["episode_offsets"]
    teacher = values["teacher_commit_indices"]
    allowed = values["commit_allowed"]
    predicted_count = 0
    within_one = 0
    premature = 0
    late = 0
    absolute_errors: list[int] = []
    raw_masked_predictions = int(
        np.sum((probabilities >= threshold) & (~allowed.astype(bool)))
    )
    for episode_index, teacher_index in enumerate(teacher):
        start = int(offsets[episode_index])
        end = int(offsets[episode_index + 1])
        local_hits = np.flatnonzero(
            (probabilities[start:end] >= threshold)
            & allowed[start:end].astype(bool)
        )
        if not len(local_hits):
            continue
        predicted = int(local_hits[0])
        error = predicted - int(teacher_index)
        predicted_count += 1
        absolute_errors.append(abs(error))
        if abs(error) <= 1:
            within_one += 1
        elif error < -1:
            premature += 1
        else:
            late += 1
    episode_count = len(teacher)
    return {
        "episodes": int(episode_count),
        "predicted_commits": int(predicted_count),
        "commit_coverage": predicted_count / episode_count,
        "within_one_decision_rate": within_one / episode_count,
        "premature_rate": premature / episode_count,
        "late_rate": late / episode_count,
        "mean_absolute_decision_error": (
            float(np.mean(absolute_errors)) if absolute_errors else None
        ),
        "raw_masked_positive_rows": raw_masked_predictions,
        "masked_predictions": 0,
        "repeated_commits": 0,
        "threshold": float(threshold),
    }


def offline_gate(
    interception: dict[str, Any],
    timing: dict[str, Any],
    thresholds: dict[str, Any],
) -> dict[str, Any]:
    checks = {
        "move_mae": interception["move_mae"]
        <= float(thresholds["maximum_move_mae"]),
        "aim_x_mae": interception["aim_x_mae"]
        <= float(thresholds["maximum_aim_x_mae"]),
        "aim_y_mae": interception["aim_y_mae"]
        <= float(thresholds["maximum_aim_y_mae"]),
        "physical_aim_error": interception["physical_aim_error_m"]
        <= float(thresholds["maximum_physical_aim_error_m"]),
        "reach_mae": interception["reach_mae"]
        <= float(thresholds["maximum_reach_mae"]),
        "finite_outputs": bool(interception["finite"]),
        "bounded_outputs": bool(interception["bounded"]),
        "commit_coverage": timing["commit_coverage"]
        >= float(thresholds["minimum_commit_coverage"]),
        "commit_timing": timing["within_one_decision_rate"]
        >= float(thresholds["minimum_within_one_decision_rate"]),
        "premature_rate": timing["premature_rate"]
        <= float(thresholds["maximum_premature_rate"]),
        "late_rate": timing["late_rate"]
        <= float(thresholds["maximum_late_rate"]),
        "masked_predictions": timing["masked_predictions"]
        <= int(thresholds["maximum_masked_predictions"]),
        "repeated_commits": timing["repeated_commits"]
        <= int(thresholds["maximum_repeated_commits"]),
    }
    return {
        "passed": all(checks.values()),
        "checks": checks,
        "failed_checks": [name for name, passed in checks.items() if not passed],
    }


def select_timing_threshold(
    values: dict[str, np.ndarray],
    probabilities: np.ndarray,
    contract: dict[str, Any],
) -> tuple[float, dict[str, Any]]:
    settings = contract["timing_model"]
    gates = contract["offline_gates"]
    thresholds = np.arange(
        float(settings["threshold_minimum"]),
        float(settings["threshold_maximum"]) + 0.0001,
        float(settings["threshold_increment"]),
    )
    candidates = [
        timing_sequence_metrics(values, probabilities, float(threshold))
        for threshold in thresholds
    ]

    def passes(item: dict[str, Any]) -> bool:
        return (
            item["commit_coverage"] >= gates["minimum_commit_coverage"]
            and item["within_one_decision_rate"]
            >= gates["minimum_within_one_decision_rate"]
            and item["premature_rate"] <= gates["maximum_premature_rate"]
            and item["late_rate"] <= gates["maximum_late_rate"]
            and item["masked_predictions"]
            <= gates["maximum_masked_predictions"]
        )

    passing = [item for item in candidates if passes(item)]
    pool = passing or candidates
    selected = max(
        pool,
        key=lambda item: (
            item["within_one_decision_rate"],
            item["commit_coverage"],
            -item["premature_rate"],
            -item["late_rate"],
            item["threshold"],
        ),
    )
    return float(selected["threshold"]), selected


def _predict(model: nn.Module, observations: np.ndarray) -> np.ndarray:
    model.eval()
    outputs: list[np.ndarray] = []
    with torch.no_grad():
        for start in range(0, len(observations), 4096):
            batch = torch.from_numpy(
                observations[start:start + 4096].astype(
                    np.float32,
                    copy=False,
                )
            )
            outputs.append(model(batch).cpu().numpy())
    return np.concatenate(outputs)


def _export_onnx(
    model: nn.Module,
    path: Path,
    output_name: str,
) -> float:
    model.eval()
    example = torch.zeros((2, OBSERVATION_SIZE), dtype=torch.float32)
    torch.onnx.export(
        model,
        example,
        str(path),
        input_names=["obs_0"],
        output_names=[output_name],
        dynamic_axes={"obs_0": {0: "batch"}, output_name: {0: "batch"}},
        opset_version=13,
    )
    torch_output = model(example).detach().numpy()
    session = ort.InferenceSession(
        str(path),
        providers=["CPUExecutionProvider"],
    )
    onnx_output = session.run(
        [output_name],
        {"obs_0": example.numpy()},
    )[0]
    return float(np.max(np.abs(torch_output - onnx_output)))


def train_split_models(
    output_dir: Path,
    contract_path: Path = DEFAULT_CONTRACT,
    seed: int = 1,
) -> dict[str, Any]:
    output_dir = output_dir.expanduser().resolve()
    contract_path = contract_path.expanduser().resolve()
    contract = load_json(contract_path)
    dataset_manifest_path = output_dir / "dataset-manifest.json"
    if not dataset_manifest_path.is_file():
        raise FileNotFoundError(dataset_manifest_path)
    dataset_manifest = load_json(dataset_manifest_path)
    model_manifest_path = output_dir / "model-manifest.json"
    if model_manifest_path.exists():
        raise FileExistsError(model_manifest_path)
    _set_deterministic(seed)
    train = _load_split(output_dir, dataset_manifest, "train")
    validation = _load_split(output_dir, dataset_manifest, "validation")
    test = _load_split(output_dir, dataset_manifest, "test")

    interception = InterceptionModel()
    train_rows = _precommit_rows(train)
    validation_rows = _precommit_rows(validation)
    test_rows = _precommit_rows(test)
    interception_settings = contract["interception_model"]
    interception_training = _train_network(
        interception,
        train["observations"][train_rows],
        train["continuous_actions"][train_rows],
        validation["observations"][validation_rows],
        validation["continuous_actions"][validation_rows],
        learning_rate=float(interception_settings["learning_rate"]),
        batch_size=int(interception_settings["batch_size"]),
        maximum_epochs=int(interception_settings["maximum_epochs"]),
        patience=int(interception_settings["early_stopping_patience"]),
        seed=seed,
        loss_function=nn.SmoothL1Loss(),
    )
    interception_test_predictions = _predict(
        interception,
        test["observations"][test_rows],
    )
    interception_test = interception_metrics(
        interception_test_predictions,
        test["continuous_actions"][test_rows],
    )

    timing = CommitTimingModel(contract["timing_model"]["observation_indices"])
    timing_train_rows, timing_sampling = balanced_timing_rows(train, seed)
    timing_validation_rows, timing_validation_sampling = balanced_timing_rows(
        validation,
        seed,
    )
    timing_settings = contract["timing_model"]
    timing_training = _train_network(
        timing,
        train["observations"][timing_train_rows],
        train["commit_actions"][timing_train_rows].reshape(-1, 1),
        validation["observations"][timing_validation_rows],
        validation["commit_actions"][timing_validation_rows].reshape(-1, 1),
        learning_rate=float(timing_settings["learning_rate"]),
        batch_size=int(timing_settings["batch_size"]),
        maximum_epochs=int(timing_settings["maximum_epochs"]),
        patience=int(timing_settings["early_stopping_patience"]),
        seed=seed + 1,
        loss_function=nn.BCEWithLogitsLoss(),
    )
    validation_probabilities = torch.sigmoid(
        torch.from_numpy(_predict(timing, validation["observations"]))
    ).numpy()
    commit_threshold, timing_validation = select_timing_threshold(
        validation,
        validation_probabilities,
        contract,
    )
    test_probabilities = torch.sigmoid(
        torch.from_numpy(_predict(timing, test["observations"]))
    ).numpy()
    timing_test = timing_sequence_metrics(
        test,
        test_probabilities,
        commit_threshold,
    )
    gate = offline_gate(
        interception_test,
        timing_test,
        contract["offline_gates"],
    )

    model_dir = output_dir / "models"
    model_dir.mkdir(parents=True, exist_ok=False)
    interception_pt = model_dir / "goalkeeper-interception-v1.pt"
    timing_pt = model_dir / "goalkeeper-commit-timing-v1.pt"
    interception_onnx = model_dir / "goalkeeper-interception-v1.onnx"
    timing_onnx = model_dir / "goalkeeper-commit-timing-v1.onnx"
    torch.save(interception.state_dict(), interception_pt)
    torch.save(timing.state_dict(), timing_pt)
    interception_parity = _export_onnx(
        interception,
        interception_onnx,
        "continuous_actions",
    )
    timing_parity = _export_onnx(
        timing,
        timing_onnx,
        "commit_logit",
    )

    manifest = {
        "schema_version": 1,
        "status": "passed" if gate["passed"] else "failed",
        "supervision_contract_id": contract["supervision_contract_id"],
        "behavior_name": contract["behavior_name"],
        "observation_spec_id": contract["observation_spec_id"],
        "action_spec_id": contract["action_spec_id"],
        "training_seed": int(seed),
        "dataset_manifest_sha256": sha256_file(dataset_manifest_path),
        "split_assignment_sha256": dataset_manifest[
            "split_assignment_sha256"
        ],
        "commit_threshold": commit_threshold,
        "models": {
            "interception": {
                "model_id": interception_settings["model_id"],
                "path": str(interception_onnx.relative_to(output_dir)),
                "sha256": sha256_file(interception_onnx),
                "input": "obs_0",
                "output": "continuous_actions",
                "pytorch_path": str(interception_pt.relative_to(output_dir)),
                "onnx_parity_max_absolute_error": interception_parity,
            },
            "timing": {
                "model_id": timing_settings["model_id"],
                "path": str(timing_onnx.relative_to(output_dir)),
                "sha256": sha256_file(timing_onnx),
                "input": "obs_0",
                "output": "commit_logit",
                "output_transform": "sigmoid",
                "feature_indices": timing_settings["observation_indices"],
                "pytorch_path": str(timing_pt.relative_to(output_dir)),
                "onnx_parity_max_absolute_error": timing_parity,
            },
        },
        "training": {
            "interception": interception_training,
            "timing": timing_training,
            "timing_sampling": timing_sampling,
            "timing_validation_sampling": timing_validation_sampling,
        },
        "offline_evaluation": {
            "interception_test": interception_test,
            "timing_validation": timing_validation,
            "timing_test": timing_test,
            "gate": gate,
        },
    }
    model_manifest_path.write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def run_offline_handoff(
    demo_dir: Path,
    output_dir: Path,
    contract_path: Path,
    seed: int,
) -> dict[str, Any]:
    extract_dataset(demo_dir, output_dir, contract_path)
    return train_split_models(output_dir, contract_path, seed)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Build and validate Stage 5.6 split supervised models."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command in ("extract", "offline"):
        child = subparsers.add_parser(command)
        child.add_argument("--demo-dir", type=Path, default=DEFAULT_DEMO_DIR)
        child.add_argument("--output-dir", type=Path, required=True)
        child.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
        if command == "offline":
            child.add_argument("--seed", type=int, default=1)
    train = subparsers.add_parser("train")
    train.add_argument("--output-dir", type=Path, required=True)
    train.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    train.add_argument("--seed", type=int, default=1)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        if args.command == "extract":
            manifest = extract_dataset(
                args.demo_dir,
                args.output_dir,
                args.contract,
            )
        elif args.command == "train":
            manifest = train_split_models(
                args.output_dir,
                args.contract,
                args.seed,
            )
        else:
            manifest = run_offline_handoff(
                args.demo_dir,
                args.output_dir,
                args.contract,
                args.seed,
            )
    except Exception as exception:
        print(f"FAIL: {exception}")
        return 1
    print(json.dumps(manifest, indent=2))
    if args.command in {"offline", "train"} and manifest["status"] != "passed":
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
