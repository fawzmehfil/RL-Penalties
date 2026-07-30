"""Stage 3 goalkeeper benchmark runner and report aggregation."""

from __future__ import annotations

import argparse
import csv
import json
import math
import platform
import time
import uuid
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.environment_parameters_channel import (
    EnvironmentParametersChannel,
)
from mlagents_envs.side_channel.raw_bytes_channel import RawBytesChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel


BEHAVIOR_NAME = "GoalkeeperState-v0"
ROBUST_BEHAVIOR_NAME = "GoalkeeperRobust-v0"
CONTROL_BEHAVIOR_NAME = "GoalkeeperControl-v1"
CONTROL_V2_BEHAVIOR_NAME = "GoalkeeperControl-v2"
CONTROL_BEHAVIOR_NAMES = {
    CONTROL_BEHAVIOR_NAME,
    CONTROL_V2_BEHAVIOR_NAME,
}
SUPPORTED_BEHAVIOR_NAMES = {
    BEHAVIOR_NAME,
    ROBUST_BEHAVIOR_NAME,
    *CONTROL_BEHAVIOR_NAMES,
}
OBSERVATION_SHAPES = [[24]]
DISCRETE_BRANCHES = [9]
CONTROL_OBSERVATION_SHAPES = [[32]]
CONTROL_V2_OBSERVATION_SHAPES = [[35]]
CONTROL_DISCRETE_BRANCHES = [2]
CONTROL_CONTINUOUS_ACTIONS = 4
STAGE3_TELEMETRY_CHANNEL_ID = uuid.UUID("b8d7b5b3-bfa6-4c46-9a3f-2e34d9fd7a31")
STAGE3_TELEMETRY_FLAG = "--stage3-benchmark-telemetry"

ACTION_NAMES = [
    "Hold",
    "ShuffleLeft",
    "ShuffleRight",
    "DiveLeftLow",
    "DiveLeftMiddle",
    "DiveLeftHigh",
    "DiveRightLow",
    "DiveRightMiddle",
    "DiveRightHigh",
]

GOAL_HALF_WIDTH = 3.66
BALL_RADIUS = 0.11
CROSSBAR_LOWER_EDGE = 2.44
TARGET_X_EXTENT = GOAL_HALF_WIDTH - BALL_RADIUS
TARGET_Y_MIN = BALL_RADIUS
TARGET_Y_MAX = CROSSBAR_LOWER_EDGE - BALL_RADIUS
GRAVITY_Y = -9.81
STAGE5_DIAGNOSTIC_THRESHOLDS = {
    "minimum_save_rate": 0.20,
    "minimum_glove_contact_rate": 0.25,
    "minimum_glove_save_rate": 0.12,
    "minimum_peak_reach_extension_mean": 0.65,
    "minimum_high_shot_save_rate": 0.15,
    "maximum_first_commit_aim_error_m": 1.0,
    "maximum_immediate_commit_rate": 0.10,
    "maximum_premature_commit_rate": 0.15,
    "maximum_late_commit_rate": 0.15,
    "minimum_timely_commit_rate": 0.70,
    "maximum_first_commit_reach_shortfall": 0.20,
    "maximum_policy_action_override_count": 0,
    "maximum_command_clamp_count": 0,
    "maximum_invalids": 0,
    "maximum_timeouts": 0,
    "maximum_action_mask_violations": 0,
}
STAGE5_IMITATION_THRESHOLDS = {
    **STAGE5_DIAGNOSTIC_THRESHOLDS,
    "minimum_save_rate": 0.35,
    "minimum_commit_rate": 0.85,
    "minimum_glove_contact_rate": 0.40,
    "minimum_glove_save_rate": 0.20,
    "minimum_high_shot_save_rate": 0.30,
    "maximum_first_commit_aim_error_m": 0.75,
}


@dataclass(frozen=True)
class BenchmarkConfig:
    schema_version: int
    benchmark_id: str
    environment_id: str
    behavior_name: str
    observation_spec_id: str
    reward_spec_id: str
    action_spec_id: str
    scenario_suite_id: str
    arena_count: int
    attempts_per_arena: int
    total_attempts: int
    stage2_lesson: int
    master_seed: int
    primary_metric: str
    save_outcomes: tuple[str, ...]
    failure_outcomes: tuple[str, ...]
    observation_shapes: tuple[tuple[int, ...], ...] = ((24,),)
    discrete_branches: tuple[int, ...] = (9,)
    continuous_actions: int = 0
    stage5_lesson: int = 0
    motor_profile_id: str | None = None
    stage5_gate_profile: str = "control-v2"
    environment_parameters: dict[str, float] = field(default_factory=dict)


class GoalkeeperPolicy:
    name = "policy"
    policy_type = "scripted"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        raise NotImplementedError

    def reset(self) -> None:
        pass

    def reset_agents(self, agent_ids: np.ndarray) -> None:
        pass

    def action_tuple(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None,
        config: BenchmarkConfig,
    ) -> ActionTuple:
        if config.continuous_actions != 0:
            raise RuntimeError(
                f"{self.name} does not support {config.continuous_actions} "
                "continuous actions"
            )
        return ActionTuple(
            discrete=self.act_batch(observations, action_mask, agent_ids)
        )

    @staticmethod
    def _legal_actions(action_mask: np.ndarray | None, row: int) -> np.ndarray:
        legal = np.arange(len(ACTION_NAMES), dtype=np.int32)
        if action_mask is not None:
            legal = legal[~np.asarray(action_mask[row], dtype=bool)]
        if len(legal) == 0:
            return np.asarray([0], dtype=np.int32)
        return legal

    @staticmethod
    def _sanitize(
        actions: Iterable[int],
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        output = np.asarray(list(actions), dtype=np.int32).reshape(-1)
        if action_mask is None:
            output[(output < 0) | (output >= len(ACTION_NAMES))] = 0
            return output.reshape(-1, 1)

        for row, action in enumerate(output):
            if (
                action < 0
                or action >= len(ACTION_NAMES)
                or bool(action_mask[row, action])
            ):
                output[row] = 0
        return output.reshape(-1, 1)


class StandCenterPolicy(GoalkeeperPolicy):
    name = "stand_center"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        return np.zeros((len(observations), 1), dtype=np.int32)


class RandomLegalPolicy(GoalkeeperPolicy):
    name = "random_legal"

    def __init__(self, seed: int = 20260724) -> None:
        self._rng = np.random.default_rng(seed)

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        actions = []
        for row in range(len(observations)):
            legal = self._legal_actions(action_mask, row)
            actions.append(int(self._rng.choice(legal)))
        return np.asarray(actions, dtype=np.int32).reshape(-1, 1)


class ReactiveSidePolicy(GoalkeeperPolicy):
    name = "reactive_side"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        actions = []
        for row, obs in enumerate(observations):
            ball_x = float(obs[0]) * 5.0
            ball_y = float(obs[1]) * 4.0
            keeper_x = float(obs[9]) * 3.1
            flight_time = float(obs[21])
            side_delta = ball_x - keeper_x
            if flight_time > 0.12 and abs(side_delta) > 0.25:
                actions.append(dive_action_for_target(ball_x, ball_y))
            elif side_delta < -0.15:
                actions.append(1)
            elif side_delta > 0.15:
                actions.append(2)
            else:
                actions.append(0)
        return self._sanitize(actions, action_mask)


class LinearInterceptPolicy(GoalkeeperPolicy):
    name = "linear_intercept"

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        actions = []
        for obs in observations:
            position = np.asarray(
                [float(obs[0]) * 5.0, float(obs[1]) * 4.0, float(obs[2]) * 11.0],
                dtype=np.float32,
            )
            velocity = np.asarray(
                [
                    float(obs[3]) * 25.0,
                    float(obs[4]) * 25.0,
                    float(obs[5]) * 25.0,
                ],
                dtype=np.float32,
            )
            if velocity[2] < -0.1:
                t = -position[2] / velocity[2]
                if 0.0 <= t <= 1.5:
                    target_x = position[0] + velocity[0] * t
                    target_y = position[1] + velocity[1] * t + 0.5 * GRAVITY_Y * t * t
                    actions.append(dive_action_for_target(float(target_x), float(target_y)))
                    continue
            actions.append(0)
        return self._sanitize(actions, action_mask)


class HybridGoalkeeperPolicy(GoalkeeperPolicy):
    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        raise RuntimeError(
            f"{self.name} requires the Stage 5 hybrid action contract"
        )

    def hybrid_actions(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> tuple[np.ndarray, np.ndarray]:
        raise NotImplementedError

    def action_tuple(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None,
        config: BenchmarkConfig,
    ) -> ActionTuple:
        if (
            config.continuous_actions != CONTROL_CONTINUOUS_ACTIONS
            or list(config.discrete_branches) != CONTROL_DISCRETE_BRANCHES
        ):
            raise RuntimeError(
                f"{self.name} requires the goalkeeper-hybrid-v1 action spec"
            )
        continuous, discrete = self.hybrid_actions(
            observations,
            action_mask,
            agent_ids,
        )
        return ActionTuple(continuous=continuous, discrete=discrete)

    @staticmethod
    def _sanitize_commit(
        commit: np.ndarray,
        action_mask: np.ndarray | None,
    ) -> np.ndarray:
        output = np.asarray(commit, dtype=np.int32).reshape(-1)
        output[(output < 0) | (output >= 2)] = 0
        if action_mask is not None:
            disabled = np.asarray(action_mask, dtype=bool)
            for row, action in enumerate(output):
                if disabled[row, action]:
                    output[row] = 0
        return output.reshape(-1, 1)


class StandCenterV1Policy(HybridGoalkeeperPolicy):
    name = "stand_center_v1"

    def hybrid_actions(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> tuple[np.ndarray, np.ndarray]:
        continuous = np.zeros(
            (len(observations), CONTROL_CONTINUOUS_ACTIONS),
            dtype=np.float32,
        )
        continuous[:, 3] = -1.0
        discrete = np.zeros((len(observations), 1), dtype=np.int32)
        return continuous, discrete


class RandomHybridV1Policy(HybridGoalkeeperPolicy):
    name = "random_hybrid_v1"

    def __init__(self, seed: int = 20260725) -> None:
        self._rng = np.random.default_rng(seed)

    def hybrid_actions(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> tuple[np.ndarray, np.ndarray]:
        continuous = self._rng.uniform(
            -1.0,
            1.0,
            size=(len(observations), CONTROL_CONTINUOUS_ACTIONS),
        ).astype(np.float32)
        commit = (self._rng.random(len(observations)) < 0.12).astype(np.int32)
        return continuous, self._sanitize_commit(commit, action_mask)


class ReactiveReachV1Policy(HybridGoalkeeperPolicy):
    name = "reactive_reach_v1"

    def __init__(self, commit_horizon: float = 0.62) -> None:
        self.commit_horizon = commit_horizon

    def hybrid_actions(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> tuple[np.ndarray, np.ndarray]:
        continuous = np.zeros(
            (len(observations), CONTROL_CONTINUOUS_ACTIONS),
            dtype=np.float32,
        )
        commit = np.zeros(len(observations), dtype=np.int32)
        for row, obs in enumerate(observations):
            command, should_commit = reactive_reach_command_v1(
                obs,
                commit_horizon=self.commit_horizon,
            )
            continuous[row] = command
            commit[row] = int(should_commit)
        return continuous, self._sanitize_commit(commit, action_mask)


def reactive_reach_command_v1(
    observation: np.ndarray,
    *,
    commit_horizon: float = 0.62,
) -> tuple[np.ndarray, bool]:
    return reactive_reach_command_from_visible_state_v1(
        ball_position=(
            float(observation[0]) * 5.0,
            float(observation[1]) * 4.0,
            float(observation[2]) * 11.0,
        ),
        ball_velocity=(
            float(observation[3]) * 25.0,
            float(observation[4]) * 25.0,
            float(observation[5]) * 25.0,
        ),
        gravity=(0.0, GRAVITY_Y, 0.0),
        keeper_x=float(observation[9]) * 3.1,
        commit_horizon=commit_horizon,
    )


def reactive_reach_command_from_visible_state_v1(
    *,
    ball_position: tuple[float, float, float],
    ball_velocity: tuple[float, float, float],
    gravity: tuple[float, float, float],
    keeper_x: float,
    commit_horizon: float = 0.62,
) -> tuple[np.ndarray, bool]:
    values = (
        *ball_position,
        *ball_velocity,
        *gravity,
        keeper_x,
        commit_horizon,
    )
    if not all(math.isfinite(value) for value in values):
        return np.asarray(
            [0.0, 0.0, 0.0, -1.0],
            dtype=np.float32,
        ), False

    target_x = float(ball_position[0])
    target_y = float(ball_position[1])
    time_to_plane = -1.0
    if ball_velocity[2] < -0.1:
        time_to_plane = float(
            np.clip(
                -ball_position[2] / ball_velocity[2],
                0.0,
                1.5,
            )
        )
        target_x += (
            float(ball_velocity[0]) * time_to_plane
            + 0.5 * float(gravity[0]) * time_to_plane * time_to_plane
        )
        target_y += (
            float(ball_velocity[1]) * time_to_plane
            + 0.5 * float(gravity[1]) * time_to_plane * time_to_plane
        )

    target_y_normalized_value = np.clip(
        (target_y - TARGET_Y_MIN) /
        (TARGET_Y_MAX - TARGET_Y_MIN),
        0.0,
        1.0,
    )
    command = np.asarray(
        [
            np.clip(
                (target_x - keeper_x) / 1.25,
                -1.0,
                1.0,
            ),
            np.clip(
                target_x / TARGET_X_EXTENT,
                -1.0,
                1.0,
            ),
            float(target_y_normalized_value) * 2.0 - 1.0,
            1.0,
        ],
        dtype=np.float32,
    )
    return command, 0.0 <= time_to_plane <= commit_horizon


class OnnxPolicy(GoalkeeperPolicy):
    policy_type = "onnx"

    def __init__(self, model_path: Path) -> None:
        import onnxruntime as ort

        self.model_path = model_path
        self.name = f"onnx:{self._policy_label(model_path)}"
        self._session = ort.InferenceSession(
            str(model_path),
            providers=["CPUExecutionProvider"],
        )
        self._output_name = "deterministic_discrete_actions"
        self._continuous_output_name = "deterministic_continuous_actions"
        self._memory_input_name = "recurrent_in"
        self._memory_output_name = "recurrent_out"
        self._input_names = {input_spec.name for input_spec in self._session.get_inputs()}
        self._memory_by_agent_id: dict[int, np.ndarray] = {}
        self._memory_shape = self._read_memory_shape()
        output_names = {output.name for output in self._session.get_outputs()}
        if self._output_name not in output_names:
            raise RuntimeError(
                f"ONNX model {model_path} does not expose {self._output_name}"
            )
        self._has_continuous_output = (
            self._continuous_output_name in output_names
        )

    def act_batch(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None = None,
    ) -> np.ndarray:
        discrete, _ = self._run_policy(
            observations,
            action_mask,
            agent_ids,
            include_continuous=False,
        )
        return self._sanitize(np.asarray(discrete).reshape(-1), action_mask)

    def action_tuple(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None,
        config: BenchmarkConfig,
    ) -> ActionTuple:
        if config.continuous_actions == 0:
            return ActionTuple(
                discrete=self.act_batch(
                    observations,
                    action_mask,
                    agent_ids,
                )
            )
        if (
            config.continuous_actions != CONTROL_CONTINUOUS_ACTIONS
            or not getattr(self, "_has_continuous_output", False)
        ):
            raise RuntimeError(
                f"ONNX model {self.model_path} does not expose the required "
                f"{config.continuous_actions} continuous actions"
            )
        discrete, continuous = self._run_policy(
            observations,
            action_mask,
            agent_ids,
            include_continuous=True,
            discrete_action_count=sum(config.discrete_branches),
        )
        if continuous is None:
            raise RuntimeError(
                f"ONNX model {self.model_path} returned no continuous actions"
            )
        sanitized_continuous = np.clip(
            np.asarray(continuous, dtype=np.float32),
            -1.0,
            1.0,
        ).reshape(-1, config.continuous_actions)
        sanitized_discrete = HybridGoalkeeperPolicy._sanitize_commit(
            np.asarray(discrete).reshape(-1),
            action_mask,
        )
        return ActionTuple(
            continuous=sanitized_continuous,
            discrete=sanitized_discrete,
        )

    def _run_policy(
        self,
        observations: np.ndarray,
        action_mask: np.ndarray | None,
        agent_ids: np.ndarray | None,
        *,
        include_continuous: bool,
        discrete_action_count: int | None = None,
    ) -> tuple[np.ndarray, np.ndarray | None]:
        mask = self._onnx_enabled_action_mask(
            len(observations),
            action_mask,
            discrete_action_count,
        )
        feed = {
            "obs_0": np.asarray(observations, dtype=np.float32),
            "action_masks": mask,
        }
        output_names = [self._output_name]
        continuous_index = None
        if include_continuous:
            continuous_index = len(output_names)
            output_names.append(self._continuous_output_name)
        normalized_agent_ids = None
        if self.is_recurrent:
            normalized_agent_ids = self._normalize_agent_ids(agent_ids, len(observations))
            feed[self._memory_input_name] = self._memory_batch(normalized_agent_ids)
            output_names.append(self._memory_output_name)
        result = self._session.run(
            output_names,
            feed,
        )
        if self.is_recurrent and normalized_agent_ids is not None:
            self._store_memory_batch(normalized_agent_ids, result[-1])
        continuous = (
            None
            if continuous_index is None
            else np.asarray(result[continuous_index])
        )
        return np.asarray(result[0]), continuous

    @property
    def is_recurrent(self) -> bool:
        return self._memory_input_name in self._input_names

    @staticmethod
    def _policy_label(model_path: Path) -> str:
        if model_path.stem in SUPPORTED_BEHAVIOR_NAMES:
            return model_path.parent.name
        return model_path.stem

    def reset(self) -> None:
        self._memory_by_agent_id.clear()

    def reset_agents(self, agent_ids: np.ndarray) -> None:
        for agent_id in np.asarray(agent_ids, dtype=np.int64).reshape(-1):
            self._memory_by_agent_id.pop(int(agent_id), None)

    def _read_memory_shape(self) -> tuple[int, int] | None:
        if self._memory_input_name not in self._input_names:
            return None
        for input_spec in self._session.get_inputs():
            if input_spec.name != self._memory_input_name:
                continue
            shape = list(input_spec.shape)
            if len(shape) != 3:
                raise RuntimeError(
                    f"Unsupported recurrent input shape for {self.model_path}: {shape}"
                )
            sequence_dim = 1 if not isinstance(shape[1], int) else int(shape[1])
            memory_size = 128 if not isinstance(shape[2], int) else int(shape[2])
            return sequence_dim, memory_size
        return None

    @staticmethod
    def _normalize_agent_ids(
        agent_ids: np.ndarray | None,
        batch_size: int,
    ) -> np.ndarray:
        if agent_ids is None:
            return np.arange(batch_size, dtype=np.int64)
        normalized = np.asarray(agent_ids, dtype=np.int64).reshape(-1)
        if len(normalized) != batch_size:
            raise ValueError(
                f"Expected {batch_size} agent ids for recurrent ONNX batch, got {len(normalized)}"
            )
        return normalized

    def _memory_batch(self, agent_ids: np.ndarray) -> np.ndarray:
        if self._memory_shape is None:
            raise RuntimeError(f"ONNX model {self.model_path} is not recurrent")
        sequence_dim, memory_size = self._memory_shape
        rows = [
            self._memory_by_agent_id.get(
                int(agent_id),
                np.zeros((sequence_dim, memory_size), dtype=np.float32),
            )
            for agent_id in agent_ids
        ]
        return np.stack(rows).astype(np.float32, copy=False)

    def _store_memory_batch(self, agent_ids: np.ndarray, recurrent_out: np.ndarray) -> None:
        memory = np.asarray(recurrent_out, dtype=np.float32)
        for row, agent_id in enumerate(agent_ids):
            self._memory_by_agent_id[int(agent_id)] = memory[row].copy()

    @staticmethod
    def _onnx_enabled_action_mask(
        batch_size: int,
        action_mask: np.ndarray | None,
        discrete_action_count: int | None = None,
    ) -> np.ndarray:
        if action_mask is None:
            count = (
                len(ACTION_NAMES)
                if discrete_action_count is None
                else discrete_action_count
            )
            return np.ones((batch_size, count), dtype=np.float32)

        # ML-Agents Python DecisionSteps masks mark disabled actions as True.
        # Exported ML-Agents ONNX policies expect enabled actions as 1.0.
        return (~np.asarray(action_mask, dtype=bool)).astype(np.float32)


def dive_action_for_target(target_x: float, target_y: float) -> int:
    height = height_band_for_y(target_y)
    if target_x < 0.0:
        return {"low": 3, "middle": 4, "high": 5}[height]
    return {"low": 6, "middle": 7, "high": 8}[height]


def load_benchmark_config(path: Path) -> BenchmarkConfig:
    raw = json.loads(path.read_text(encoding="utf-8"))
    config = BenchmarkConfig(
        schema_version=int(raw["schema_version"]),
        benchmark_id=str(raw["benchmark_id"]),
        environment_id=str(raw["environment_id"]),
        behavior_name=str(raw["behavior_name"]),
        observation_spec_id=str(raw["observation_spec_id"]),
        reward_spec_id=str(raw["reward_spec_id"]),
        action_spec_id=str(raw["action_spec_id"]),
        scenario_suite_id=str(raw["scenario_suite_id"]),
        arena_count=int(raw["arena_count"]),
        attempts_per_arena=int(raw["attempts_per_arena"]),
        total_attempts=int(raw["total_attempts"]),
        stage2_lesson=int(raw["stage2_lesson"]),
        master_seed=int(raw["master_seed"]),
        primary_metric=str(raw["primary_metric"]),
        save_outcomes=tuple(raw["save_outcomes"]),
        failure_outcomes=tuple(raw["failure_outcomes"]),
        observation_shapes=tuple(
            tuple(int(value) for value in shape)
            for shape in raw.get("observation_shapes", OBSERVATION_SHAPES)
        ),
        discrete_branches=tuple(
            int(value) for value in raw.get("discrete_branches", DISCRETE_BRANCHES)
        ),
        continuous_actions=int(raw.get("continuous_actions", 0)),
        stage5_lesson=int(raw.get("stage5_lesson", 0)),
        motor_profile_id=(
            str(raw["motor_profile_id"])
            if raw.get("motor_profile_id") is not None
            else None
        ),
        stage5_gate_profile=str(
            raw.get("stage5_gate_profile", "control-v2")
        ),
        environment_parameters={
            str(key): float(value)
            for key, value in raw.get("environment_parameters", {}).items()
        },
    )
    validate_benchmark_config(config)
    return config


def validate_benchmark_config(config: BenchmarkConfig) -> None:
    if config.schema_version != 1:
        raise ValueError(f"Unsupported benchmark schema: {config.schema_version}")
    if config.behavior_name not in SUPPORTED_BEHAVIOR_NAMES:
        raise ValueError(f"Unsupported behavior: {config.behavior_name}")
    if config.stage5_gate_profile not in {
        "control-v2",
        "imitation-v1",
    }:
        raise ValueError(
            "Unsupported Stage 5 gate profile: "
            f"{config.stage5_gate_profile}"
        )
    is_control = config.behavior_name in CONTROL_BEHAVIOR_NAMES
    expected_observation_shapes = (
        (
            CONTROL_V2_OBSERVATION_SHAPES
            if config.behavior_name == CONTROL_V2_BEHAVIOR_NAME
            else CONTROL_OBSERVATION_SHAPES
        )
        if is_control
        else OBSERVATION_SHAPES
    )
    expected_discrete_branches = (
        CONTROL_DISCRETE_BRANCHES if is_control else DISCRETE_BRANCHES
    )
    expected_continuous_actions = (
        CONTROL_CONTINUOUS_ACTIONS if is_control else 0
    )
    if (
        [list(shape) for shape in config.observation_shapes]
        != expected_observation_shapes
    ):
        raise ValueError(
            f"Unsupported observation shapes: {config.observation_shapes}"
        )
    if list(config.discrete_branches) != expected_discrete_branches:
        raise ValueError(
            f"Unsupported discrete branches: {config.discrete_branches}"
        )
    if config.continuous_actions != expected_continuous_actions:
        raise ValueError(
            f"Unsupported continuous action count: {config.continuous_actions}"
        )
    if (
        config.behavior_name == BEHAVIOR_NAME
        and config.observation_spec_id != "state-v0"
    ):
        raise ValueError("GoalkeeperState-v0 requires state-v0 observations")
    if (
        config.behavior_name == ROBUST_BEHAVIOR_NAME
        and config.observation_spec_id != "state-po-v0"
    ):
        raise ValueError("GoalkeeperRobust-v0 requires state-po-v0 observations")
    if (
        config.behavior_name == CONTROL_BEHAVIOR_NAME
        and config.observation_spec_id != "control-state-v1"
    ):
        raise ValueError(
            "GoalkeeperControl-v1 requires control-state-v1 observations"
        )
    if (
        config.behavior_name == CONTROL_V2_BEHAVIOR_NAME
        and config.observation_spec_id != "control-state-v2"
    ):
        raise ValueError(
            "GoalkeeperControl-v2 requires control-state-v2 observations"
        )
    if (
        is_control
        and config.action_spec_id != "goalkeeper-hybrid-v1"
    ):
        raise ValueError(
            "Goalkeeper control behaviors require goalkeeper-hybrid-v1 actions"
        )
    if (
        is_control
        and config.motor_profile_id != "keeper-control-v1"
    ):
        raise ValueError(
            "Goalkeeper control behaviors require keeper-control-v1 motor"
        )
    if config.arena_count <= 0 or config.attempts_per_arena <= 0:
        raise ValueError("arena_count and attempts_per_arena must be positive")
    if config.total_attempts != config.arena_count * config.attempts_per_arena:
        raise ValueError("total_attempts must equal arena_count * attempts_per_arena")
    if config.stage2_lesson != 3:
        raise ValueError("Stage 3 v0 benchmarks the full Stage 2 lesson 3 range")
    if is_control and config.stage5_lesson != 4:
        raise ValueError("Stage 5 benchmarks require full Stage 5 lesson 4")


def make_policy(
    spec: str,
    seed: int,
    config: BenchmarkConfig | None = None,
) -> GoalkeeperPolicy:
    is_control = (
        config is not None
        and config.behavior_name in CONTROL_BEHAVIOR_NAMES
    )
    if spec == "stand_center":
        return StandCenterV1Policy() if is_control else StandCenterPolicy()
    if spec == "random_legal":
        return RandomHybridV1Policy(seed) if is_control else RandomLegalPolicy(seed)
    if spec == "stand_center_v1":
        return StandCenterV1Policy()
    if spec in {"random_hybrid", "random_hybrid_v1"}:
        return RandomHybridV1Policy(seed)
    if spec in {"reactive_reach", "reactive_reach_v1"}:
        return ReactiveReachV1Policy()
    if spec == "reactive_side":
        return ReactiveReachV1Policy() if is_control else ReactiveSidePolicy()
    if spec == "linear_intercept":
        return ReactiveReachV1Policy() if is_control else LinearInterceptPolicy()
    if spec.startswith("onnx:"):
        model_path = Path(spec.split(":", maxsplit=1)[1]).expanduser().resolve()
        if not model_path.exists():
            raise FileNotFoundError(model_path)
        return OnnxPolicy(model_path)
    raise ValueError(f"Unknown policy spec: {spec}")


def run_policy(
    *,
    build_path: Path,
    config: BenchmarkConfig,
    policy: GoalkeeperPolicy,
    attempts_per_arena: int,
    worker_id: int,
    player_log_path: Path | None = None,
    timeout_wait: int = 120,
    max_environment_steps: int = 2_000_000,
) -> list[dict[str, Any]]:
    telemetry = RawBytesChannel(STAGE3_TELEMETRY_CHANNEL_ID)
    parameters = EnvironmentParametersChannel()
    stats = StatsSideChannel()
    parameters.set_float_parameter("stage2.lesson", float(config.stage2_lesson))
    for key, value in sorted(config.environment_parameters.items()):
        parameters.set_float_parameter(key, float(value))
    additional_args = [
        "-batchmode",
        "-nographics",
        STAGE3_TELEMETRY_FLAG,
        f"--stage3-master-seed={config.master_seed}",
        f"--benchmark-id={config.benchmark_id}",
    ]
    if player_log_path is not None:
        player_log_path.parent.mkdir(parents=True, exist_ok=True)
        additional_args.extend(["-logFile", str(player_log_path.resolve())])

    env = UnityEnvironment(
        file_name=str(build_path),
        worker_id=worker_id,
        seed=config.master_seed,
        no_graphics=True,
        timeout_wait=timeout_wait,
        additional_args=additional_args,
        side_channels=[telemetry, parameters, stats],
    )

    per_arena: dict[int, int] = defaultdict(int)
    episodes: list[dict[str, Any]] = []
    seen_keys: set[tuple[int, int]] = set()

    try:
        env.reset()
        policy.reset()
        behavior_name = require_behavior(env, config)
        for _ in range(max_environment_steps):
            drain_telemetry(
                telemetry,
                policy.name,
                attempts_per_arena,
                per_arena,
                seen_keys,
                episodes,
            )
            if quotas_met(per_arena, config.arena_count, attempts_per_arena):
                break

            decision_steps, terminal_steps = env.get_steps(behavior_name)
            if len(terminal_steps):
                policy.reset_agents(terminal_steps.agent_id)
            if len(decision_steps):
                mask = decision_steps.action_mask[0] if decision_steps.action_mask else None
                actions = policy.act_batch(
                    decision_steps.obs[0],
                    mask,
                    decision_steps.agent_id,
                ) if config.continuous_actions == 0 else None
                action_tuple = (
                    ActionTuple(discrete=actions)
                    if actions is not None
                    else policy.action_tuple(
                        decision_steps.obs[0],
                        mask,
                        decision_steps.agent_id,
                        config,
                    )
                )
                env.set_actions(behavior_name, action_tuple)
            env.step()
        else:
            raise TimeoutError(
                f"{policy.name} did not reach {attempts_per_arena} attempts per arena"
            )

        drain_telemetry(
            telemetry,
            policy.name,
            attempts_per_arena,
            per_arena,
            seen_keys,
            episodes,
        )
        return sorted(episodes, key=lambda item: (item["arena_id"], item["attempt_id"]))
    finally:
        env.close()


def require_behavior(env: UnityEnvironment, config: BenchmarkConfig) -> str:
    behavior_names = list(env.behavior_specs)
    if (
        len(behavior_names) != 1
        or behavior_names[0].split("?", maxsplit=1)[0] != config.behavior_name
    ):
        raise RuntimeError(f"Unexpected behavior names: {behavior_names}")
    behavior_name = behavior_names[0]
    specification = env.behavior_specs[behavior_name]
    branches = list(specification.action_spec.discrete_branches)
    if (
        specification.action_spec.continuous_size != config.continuous_actions
        or branches != list(config.discrete_branches)
    ):
        raise RuntimeError(f"Unexpected action specification: {specification.action_spec}")
    observation_shapes = [
        list(observation.shape) for observation in specification.observation_specs
    ]
    if observation_shapes != [list(shape) for shape in config.observation_shapes]:
        raise RuntimeError(f"Unexpected observation shapes: {observation_shapes}")
    return behavior_name


def drain_telemetry(
    telemetry: RawBytesChannel,
    policy_name: str,
    attempts_per_arena: int,
    per_arena: dict[int, int],
    seen_keys: set[tuple[int, int]],
    episodes: list[dict[str, Any]],
) -> None:
    for raw in telemetry.get_and_clear_received_messages():
        item = json.loads(bytes(raw).decode("utf-8"))
        arena_id = int(item["arena_id"])
        attempt_id = int(item["attempt_id"])
        key = (arena_id, attempt_id)
        if attempt_id > attempts_per_arena or key in seen_keys:
            continue
        seen_keys.add(key)
        per_arena[arena_id] += 1
        item["policy"] = policy_name
        episodes.append(item)


def quotas_met(
    per_arena: dict[int, int],
    arena_count: int,
    attempts_per_arena: int,
) -> bool:
    return all(per_arena.get(arena_id, 0) >= attempts_per_arena for arena_id in range(arena_count))


def aggregate_policy(
    policy: GoalkeeperPolicy,
    episodes: list[dict[str, Any]],
    config: BenchmarkConfig,
    attempts_per_arena: int,
) -> dict[str, Any]:
    total = len(episodes)
    outcomes = Counter(str(item["outcome"]) for item in episodes)
    saves = sum(1 for item in episodes if item["outcome"] in config.save_outcomes)
    goals = outcomes["Goal"]
    invalid = outcomes["Invalid"]
    timeouts = outcomes["Timeout"]
    action_counts = [0] * len(ACTION_NAMES)
    for item in episodes:
        for index, count in enumerate(item.get("accepted_action_counts", [])):
            if index < len(action_counts):
                action_counts[index] += int(count)

    first_dive_episodes = [item for item in episodes if item.get("has_first_dive")]
    wrong_side = sum(1 for item in first_dive_episodes if is_wrong_side(item))
    wrong_height = sum(1 for item in first_dive_episodes if is_wrong_height(item))
    contact_then_goal = sum(
        1 for item in episodes if item["outcome"] == "Goal" and item["goalkeeper_contact"]
    )
    committed_episodes = [
        item for item in episodes if item.get("has_save_commitment")
    ]
    contacted_episodes = [
        item for item in episodes if item.get("goalkeeper_contact")
    ]
    glove_first_contacts = sum(
        first_contact_category(item) == "glove"
        for item in contacted_episodes
    )
    body_first_contacts = sum(
        first_contact_category(item) == "body"
        for item in contacted_episodes
    )
    immediate_commits = sum(
        first_commit_was_immediate(item)
        for item in committed_episodes
    )
    premature_commits = sum(
        bool(item.get("first_commit_was_premature", False))
        for item in committed_episodes
    )
    late_commits = sum(
        bool(item.get("first_commit_was_late", False))
        for item in committed_episodes
    )
    timely_commits = sum(
        bool(item.get("first_commit_was_timely", False))
        for item in committed_episodes
    )
    root_saturation_attempts = sum(
        int(
            item.get(
                "root_target_saturation_count",
                item.get("control_target_clamp_count", 0),
            )
        )
        > 0
        for item in episodes
    )
    command_clamp_attempts = sum(
        int(item.get("control_command_clamp_count", 0)) > 0
        for item in episodes
    )
    glove_saves = sum(
        item["outcome"] in config.save_outcomes and item.get("glove_contact")
        for item in episodes
    )
    glove_first_saves = sum(
        item["outcome"] in config.save_outcomes
        and first_contact_category(item) == "glove"
        for item in episodes
    )
    arm_saves = sum(
        item["outcome"] in config.save_outcomes
        and first_contact_part(item) == "Arm"
        for item in episodes
    )
    body_saves = saves - glove_first_saves - arm_saves
    minimum_glove_distances = [
        float(item["minimum_glove_ball_distance"])
        for item in episodes
        if float(item.get("minimum_glove_ball_distance", -1.0)) >= 0.0
    ]

    expected_total = config.arena_count * attempts_per_arena
    report = {
        "policy": policy.name,
        "policy_type": policy.policy_type,
        "attempts": total,
        "expected_attempts": expected_total,
        "complete": total == expected_total,
        "outcomes": dict(sorted(outcomes.items())),
        "save_rate": rate(saves, total),
        "goal_rate": rate(goals, total),
        "invalid_rate": rate(invalid, total),
        "timeout_rate": rate(timeouts, total),
        "glove_contact_rate": rate(
            sum(1 for item in episodes if item["glove_contact"]), total
        ),
        "goalkeeper_contact_rate": rate(
            sum(1 for item in episodes if item["goalkeeper_contact"]), total
        ),
        "contact_then_goal_rate": rate(contact_then_goal, total),
        "wrong_side_rate": rate(wrong_side, len(first_dive_episodes)),
        "wrong_height_rate": rate(wrong_height, len(first_dive_episodes)),
        "action_mask_violations": sum(
            int(item["action_mask_violations"]) for item in episodes
        ),
        "duplicate_terminal_events": sum(
            int(item["duplicate_terminal_events"]) for item in episodes
        ),
        "action_usage": action_usage(action_counts),
        "by_quadrant": aggregate_by(episodes, quadrant),
        "by_height_band": aggregate_by(episodes, height_band),
        "by_horizontal_band": aggregate_by(episodes, horizontal_band),
        "by_flight_time_band": aggregate_by(episodes, flight_time_band),
        "by_first_dive_action": aggregate_by(episodes, first_dive_action),
    }
    if config.behavior_name in CONTROL_BEHAVIOR_NAMES:
        report.update(
            {
                "commit_rate": rate(len(committed_episodes), total),
                "first_commit_ball_flight_time": numeric_summary(
                    [
                        float(item["first_commit_ball_flight_time"])
                        for item in committed_episodes
                    ]
                ),
                "first_commit_aim_error_m": numeric_summary(
                    [
                        first_commit_aim_error(item)
                        for item in committed_episodes
                    ]
                ),
                "first_commit_visible_time_to_goal_plane": numeric_summary(
                    [
                        float(
                            item.get(
                                "first_commit_visible_time_to_goal_plane",
                                -1.0,
                            )
                        )
                        for item in committed_episodes
                        if float(
                            item.get(
                                "first_commit_visible_time_to_goal_plane",
                                -1.0,
                            )
                        )
                        >= 0.0
                    ]
                ),
                "first_commit_reach_demand": numeric_summary(
                    [
                        float(item.get("first_commit_reach_demand", 0.0))
                        for item in committed_episodes
                    ]
                ),
                "first_commit_reach_extension": numeric_summary(
                    [
                        float(
                            item.get(
                                "first_commit_reach_extension",
                                0.0,
                            )
                        )
                        for item in committed_episodes
                    ]
                ),
                "immediate_commit_rate": rate(immediate_commits, total),
                "premature_commit_rate": rate(
                    premature_commits,
                    total,
                ),
                "late_commit_rate": rate(late_commits, total),
                "timely_commit_rate": rate(timely_commits, total),
                "first_commit_visible_aim_error_m": numeric_summary(
                    [
                        float(item["first_commit_visible_aim_error"])
                        for item in committed_episodes
                        if float(
                            item.get(
                                "first_commit_visible_aim_error",
                                -1.0,
                            )
                        )
                        >= 0.0
                    ]
                ),
                "first_commit_desired_reach": numeric_summary(
                    [
                        float(
                            item.get(
                                "first_commit_desired_reach",
                                0.0,
                            )
                        )
                        for item in committed_episodes
                    ]
                ),
                "first_commit_reach_shortfall": numeric_summary(
                    [
                        float(
                            item.get(
                                "first_commit_reach_shortfall",
                                0.0,
                            )
                        )
                        for item in committed_episodes
                    ]
                ),
                "first_eligible_commit_decision_index": numeric_summary(
                    [
                        float(item["first_eligible_commit_decision_index"])
                        for item in episodes
                        if int(
                            item.get(
                                "first_eligible_commit_decision_index",
                                -1,
                            )
                        )
                        >= 0
                    ]
                ),
                "first_eligible_commit_ball_flight_time": numeric_summary(
                    [
                        float(
                            item["first_eligible_commit_ball_flight_time"]
                        )
                        for item in episodes
                        if float(
                            item.get(
                                "first_eligible_commit_ball_flight_time",
                                -1.0,
                            )
                        )
                        >= 0.0
                    ]
                ),
                "eligible_commit_decisions_before_commit": numeric_summary(
                    [
                        float(
                            item.get(
                                "eligible_commit_decisions_before_commit",
                                0,
                            )
                        )
                        for item in episodes
                    ]
                ),
                "goalkeeper_root_distance_m": numeric_summary(
                    [
                        float(item.get("goalkeeper_root_distance", 0.0))
                        for item in episodes
                    ]
                ),
                "goalkeeper_peak_root_speed_mps": numeric_summary(
                    [
                        float(item.get("goalkeeper_peak_root_speed", 0.0))
                        for item in episodes
                    ]
                ),
                "goalkeeper_peak_reach_extension": numeric_summary(
                    [
                        float(
                            item.get(
                                "goalkeeper_peak_reach_extension",
                                0.0,
                            )
                        )
                        for item in episodes
                    ]
                ),
                "minimum_glove_ball_distance_m": numeric_summary(
                    minimum_glove_distances
                ),
                "control_command_clamp_count": sum(
                    int(item.get("control_command_clamp_count", 0))
                    for item in episodes
                ),
                "control_command_clamp_attempt_rate": rate(
                    command_clamp_attempts,
                    total,
                ),
                "control_target_clamp_count": sum(
                    int(item.get("control_target_clamp_count", 0))
                    for item in episodes
                ),
                "control_target_clamp_attempt_rate": rate(
                    root_saturation_attempts,
                    total,
                ),
                "root_target_saturation_count": sum(
                    int(
                        item.get(
                            "root_target_saturation_count",
                            item.get("control_target_clamp_count", 0),
                        )
                    )
                    for item in episodes
                ),
                "root_target_saturation_attempt_rate": rate(
                    root_saturation_attempts,
                    total,
                ),
                "root_target_saturation_distance_m": numeric_summary(
                    [
                        float(
                            item.get(
                                "root_target_saturation_distance",
                                0.0,
                            )
                        )
                        for item in episodes
                        if float(
                            item.get(
                                "root_target_saturation_distance",
                                0.0,
                            )
                        )
                        > 0.0
                    ]
                ),
                "training_decision_shaping_reward": numeric_summary(
                    [
                        float(
                            item.get(
                                "training_decision_shaping_reward",
                                0.0,
                            )
                        )
                        for item in episodes
                    ]
                ),
                "policy_action_override_count": sum(
                    int(item.get("policy_action_override_count", 0))
                    for item in episodes
                ),
                "accepted_control_decision_count": sum(
                    int(item.get("accepted_control_decision_count", 0))
                    for item in episodes
                ),
                "policy_decision_request_count": sum(
                    int(item.get("policy_decision_request_count", 0))
                    for item in episodes
                ),
                "policy_decision_consumed_count": sum(
                    int(item.get("policy_decision_consumed_count", 0))
                    for item in episodes
                ),
                "policy_decision_discarded_count": sum(
                    int(item.get("policy_decision_discarded_count", 0))
                    for item in episodes
                ),
                "policy_decision_duplicate_request_count": sum(
                    int(
                        item.get(
                            "policy_decision_duplicate_request_count",
                            0,
                        )
                    )
                    for item in episodes
                ),
                "policy_decision_missing_action_count": sum(
                    int(item.get("policy_decision_missing_action_count", 0))
                    for item in episodes
                ),
                "saturated_shot_save_rate": rate(
                    sum(
                        item["outcome"] in config.save_outcomes
                        and int(
                            item.get(
                                "root_target_saturation_count",
                                item.get(
                                    "control_target_clamp_count",
                                    0,
                                ),
                            )
                        )
                        > 0
                        for item in episodes
                    ),
                    root_saturation_attempts,
                ),
                "glove_save_rate": rate(glove_saves, total),
                "glove_first_save_rate": rate(
                    glove_first_saves,
                    total,
                ),
                "arm_save_rate": rate(arm_saves, total),
                "body_save_rate": rate(body_saves, total),
                "glove_first_contact_rate": rate(
                    glove_first_contacts,
                    len(contacted_episodes),
                ),
                "body_first_contact_rate": rate(
                    body_first_contacts,
                    len(contacted_episodes),
                ),
                "control_usage": control_usage(episodes),
                "by_commit_status": aggregate_by(
                    episodes,
                    lambda item: (
                        "committed"
                        if item.get("has_save_commitment")
                        else "no-commit"
                    ),
                ),
                "by_first_commit_aim_region": aggregate_by(
                    committed_episodes,
                    first_commit_aim_region,
                ),
                "by_first_commit_timing_band": aggregate_by(
                    committed_episodes,
                    first_commit_timing_band,
                ),
                "by_first_contact_part": aggregate_by(
                    episodes,
                    first_contact_part,
                ),
            }
        )
        report["stage5_diagnostic_gate"] = stage5_diagnostic_gate(
            report,
            control_version=(
                2
                if config.behavior_name == CONTROL_V2_BEHAVIOR_NAME
                else 1
            ),
            profile=config.stage5_gate_profile,
        )
    return report


def aggregate_by(
    episodes: list[dict[str, Any]],
    classifier: Any,
) -> dict[str, dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for item in episodes:
        grouped[classifier(item)].append(item)
    return {
        key: {
            "attempts": len(items),
            "save_rate": rate(
                sum(item["outcome"] in {"Saved", "BlockedThenOut"} for item in items),
                len(items),
            ),
            "goal_rate": rate(sum(item["outcome"] == "Goal" for item in items), len(items)),
        }
        for key, items in sorted(grouped.items())
    }


def stage5_diagnostic_gate(
    policy: dict[str, Any],
    *,
    control_version: int = 1,
    profile: str = "control-v2",
) -> dict[str, Any]:
    thresholds = (
        STAGE5_IMITATION_THRESHOLDS
        if profile == "imitation-v1"
        else STAGE5_DIAGNOSTIC_THRESHOLDS
    )
    high_save_rate = (
        policy.get("by_height_band", {})
        .get("high", {})
        .get("save_rate", {})
        .get("value", 0.0)
    )
    checks = {
        "save_rate": (
            policy["save_rate"]["value"]
            >= thresholds["minimum_save_rate"]
        ),
        "glove_contact_rate": (
            policy["glove_contact_rate"]["value"]
            >= thresholds[
                "minimum_glove_contact_rate"
            ]
        ),
        "glove_save_rate": (
            policy["glove_save_rate"]["value"]
            >= thresholds["minimum_glove_save_rate"]
        ),
        "peak_reach_extension": (
            policy["goalkeeper_peak_reach_extension"]["mean"]
            >= thresholds[
                "minimum_peak_reach_extension_mean"
            ]
        ),
        "high_shot_save_rate": (
            high_save_rate
            >= thresholds[
                "minimum_high_shot_save_rate"
            ]
        ),
        "first_commit_aim_error": (
            policy["first_commit_aim_error_m"]["mean"]
            <= thresholds[
                "maximum_first_commit_aim_error_m"
            ]
        ),
        "immediate_commit_rate": (
            policy["immediate_commit_rate"]["value"]
            <= thresholds[
                "maximum_immediate_commit_rate"
            ]
        ),
        "premature_commit_rate": (
            policy["premature_commit_rate"]["value"]
            <= thresholds[
                "maximum_premature_commit_rate"
            ]
        ),
        "late_commit_rate": (
            policy["late_commit_rate"]["value"]
            <= thresholds[
                "maximum_late_commit_rate"
            ]
        ),
        "timely_commit_rate": (
            policy["timely_commit_rate"]["value"]
            >= thresholds[
                "minimum_timely_commit_rate"
            ]
        ),
        "first_commit_reach_shortfall": (
            policy["first_commit_reach_shortfall"]["mean"]
            <= thresholds[
                "maximum_first_commit_reach_shortfall"
            ]
        ),
        "policy_action_overrides": (
            policy["policy_action_override_count"]
            <= thresholds[
                "maximum_policy_action_override_count"
            ]
        ),
        "command_clamps": (
            policy["control_command_clamp_count"]
            <= thresholds[
                "maximum_command_clamp_count"
            ]
        ),
        "invalids": (
            policy["invalid_rate"]["successes"]
            <= thresholds["maximum_invalids"]
        ),
        "timeouts": (
            policy["timeout_rate"]["successes"]
            <= thresholds["maximum_timeouts"]
        ),
        "action_mask_violations": (
            policy["action_mask_violations"]
            <= thresholds[
                "maximum_action_mask_violations"
            ]
        ),
    }
    if control_version >= 2:
        for timing_check in (
            "immediate_commit_rate",
            "premature_commit_rate",
            "late_commit_rate",
            "timely_commit_rate",
            "first_commit_reach_shortfall",
        ):
            checks.pop(timing_check)
        requests = policy.get("policy_decision_request_count", 0)
        consumed = policy.get("policy_decision_consumed_count", 0)
        discarded = policy.get("policy_decision_discarded_count", 0)
        accepted = policy.get("accepted_control_decision_count", 0)
        checks.update(
            {
                "decision_request_balance": (
                    requests == consumed + discarded
                ),
                "decision_consumption_balance": consumed == accepted,
                "decision_discard_limit": (
                    discarded <= policy.get("attempts", 0)
                ),
                "duplicate_decision_requests": (
                    policy.get(
                        "policy_decision_duplicate_request_count",
                        0,
                    )
                    == 0
                ),
                "missing_decision_actions": (
                    policy.get("policy_decision_missing_action_count", 0)
                    == 0
                ),
            }
        )
        if profile == "imitation-v1":
            checks["commit_rate"] = (
                policy["commit_rate"]["value"]
                >= thresholds["minimum_commit_rate"]
            )
    failed = [name for name, passed in checks.items() if not passed]
    return {
        "passed": not failed,
        "control_version": control_version,
        "profile": profile,
        "checks_passed": sum(checks.values()),
        "checks_total": len(checks),
        "failed_checks": failed,
        "thresholds": dict(thresholds),
    }


def action_usage(action_counts: list[int]) -> dict[str, dict[str, float | int]]:
    total = sum(action_counts)
    return {
        ACTION_NAMES[index]: {
            "count": count,
            "rate": 0.0 if total == 0 else count / total,
        }
        for index, count in enumerate(action_counts)
    }


def control_usage(
    episodes: list[dict[str, Any]],
) -> dict[str, Any]:
    channels = ("move_x", "aim_x", "aim_y", "reach")
    decisions = sum(
        int(item.get("accepted_control_decision_count", 0))
        for item in episodes
    )
    move_commands = sum(
        int(item.get("control_move_command_count", 0))
        for item in episodes
    )
    reach_commands = sum(
        int(item.get("control_reach_command_count", 0))
        for item in episodes
    )
    absolute_sums = np.zeros(4, dtype=np.float64)
    saturation_counts = np.zeros(4, dtype=np.int64)
    for item in episodes:
        absolute = np.asarray(
            item.get("control_absolute_action_sums", [0.0] * 4),
            dtype=np.float64,
        ).reshape(-1)
        saturation = np.asarray(
            item.get("control_saturation_counts", [0] * 4),
            dtype=np.int64,
        ).reshape(-1)
        absolute_sums[: min(4, len(absolute))] += absolute[:4]
        saturation_counts[: min(4, len(saturation))] += saturation[:4]
    denominator = max(decisions, 1)
    return {
        "accepted_decisions": decisions,
        "move_command_rate": (
            0.0 if decisions == 0 else move_commands / decisions
        ),
        "reach_command_rate": (
            0.0 if decisions == 0 else reach_commands / decisions
        ),
        "channels": {
            channel: {
                "mean_absolute_value": float(absolute_sums[index] / denominator),
                "saturation_count": int(saturation_counts[index]),
                "saturation_rate": (
                    0.0
                    if decisions == 0
                    else float(saturation_counts[index] / decisions)
                ),
            }
            for index, channel in enumerate(channels)
        },
    }


def rate(successes: int, total: int) -> dict[str, float | int]:
    interval = wilson_interval(successes, total)
    return {
        "successes": successes,
        "total": total,
        "value": 0.0 if total == 0 else successes / total,
        "ci95_low": interval[0],
        "ci95_high": interval[1],
    }


def numeric_summary(values: Iterable[float]) -> dict[str, float | int]:
    finite = np.asarray(
        [
            float(value)
            for value in values
            if math.isfinite(float(value))
        ],
        dtype=np.float64,
    )
    if len(finite) == 0:
        return {
            "count": 0,
            "mean": 0.0,
            "minimum": 0.0,
            "maximum": 0.0,
        }
    return {
        "count": int(len(finite)),
        "mean": float(np.mean(finite)),
        "minimum": float(np.min(finite)),
        "maximum": float(np.max(finite)),
    }


def wilson_interval(successes: int, total: int, z: float = 1.959963984540054) -> tuple[float, float]:
    if total <= 0:
        return (0.0, 0.0)
    phat = successes / total
    denominator = 1.0 + z * z / total
    centre = phat + z * z / (2.0 * total)
    spread = z * math.sqrt((phat * (1.0 - phat) + z * z / (4.0 * total)) / total)
    return ((centre - spread) / denominator, (centre + spread) / denominator)


def target_vector(item: dict[str, Any]) -> dict[str, float]:
    return item["requested_target_local"]


def target_y_normalized(target_y: float) -> float:
    return max(0.0, min(1.0, (target_y - TARGET_Y_MIN) / (TARGET_Y_MAX - TARGET_Y_MIN)))


def height_band_for_y(target_y: float) -> str:
    normalized = target_y_normalized(target_y)
    if normalized < 1.0 / 3.0:
        return "low"
    if normalized < 2.0 / 3.0:
        return "middle"
    return "high"


def height_band(item: dict[str, Any]) -> str:
    return height_band_for_y(float(target_vector(item)["y"]))


def horizontal_band(item: dict[str, Any]) -> str:
    normalized = max(-1.0, min(1.0, float(target_vector(item)["x"]) / TARGET_X_EXTENT))
    if normalized < -0.5:
        return "left"
    if normalized < 0.0:
        return "left-center"
    if normalized < 0.5:
        return "right-center"
    return "right"


def quadrant(item: dict[str, Any]) -> str:
    side = "left" if float(target_vector(item)["x"]) < 0.0 else "right"
    normalized_y = target_y_normalized(float(target_vector(item)["y"]))
    vertical = "low" if normalized_y < 0.5 else "high"
    return f"{vertical}-{side}"


def flight_time_band(item: dict[str, Any]) -> str:
    value = float(
        item.get("sampled_shot_flight_time", item["ball_flight_time"])
    )
    if value <= 0.55:
        return "fast"
    if value <= 0.70:
        return "medium"
    return "slow"


def first_dive_action(item: dict[str, Any]) -> str:
    return str(item["first_accepted_dive_action"] or "none")


def first_commit_aim_region(item: dict[str, Any]) -> str:
    aim = item.get("first_commit_aim") or {"x": 0.0, "y": 0.0}
    side = "left" if float(aim["x"]) < 0.0 else "right"
    aim_y = float(aim["y"])
    vertical = (
        "low"
        if aim_y < -1.0 / 3.0
        else "middle"
        if aim_y < 1.0 / 3.0
        else "high"
    )
    return f"{vertical}-{side}"


def first_commit_aim_error(item: dict[str, Any]) -> float:
    aim = item.get("first_commit_aim") or {"x": 0.0, "y": 0.0}
    aim_x = float(aim["x"])
    aim_y = float(aim["y"])
    aimed_local = np.asarray(
        [
            np.clip(aim_x, -1.0, 1.0) * TARGET_X_EXTENT,
            TARGET_Y_MIN
            + np.clip((aim_y + 1.0) * 0.5, 0.0, 1.0)
            * (TARGET_Y_MAX - TARGET_Y_MIN),
        ],
        dtype=np.float64,
    )
    target = target_vector(item)
    target_local = np.asarray(
        [float(target["x"]), float(target["y"])],
        dtype=np.float64,
    )
    return float(np.linalg.norm(aimed_local - target_local))


def first_commit_was_immediate(item: dict[str, Any]) -> bool:
    if "first_commit_was_immediate" in item:
        return bool(item["first_commit_was_immediate"])
    return float(item.get("first_commit_ball_flight_time", -1.0)) <= 0.06


def first_commit_timing_band(item: dict[str, Any]) -> str:
    time_to_plane = float(
        item.get("first_commit_visible_time_to_goal_plane", -1.0)
    )
    if time_to_plane < 0.0:
        return "unavailable"
    if time_to_plane > 0.72:
        return "early"
    if time_to_plane < 0.35:
        return "late"
    return "in-window"


def first_contact_part(item: dict[str, Any]) -> str:
    return str(item.get("first_goalkeeper_contact_part") or "None")


def first_contact_category(item: dict[str, Any]) -> str:
    part = first_contact_part(item)
    if part in {"LeftGlove", "RightGlove"}:
        return "glove"
    if part in {"Arm", "TorsoOrHead", "Leg"}:
        return "body"
    return "none"


def is_wrong_side(item: dict[str, Any]) -> bool:
    target_side = "left" if float(target_vector(item)["x"]) < 0.0 else "right"
    action = first_dive_action(item)
    if action == "none":
        return False
    action_side = "left" if "Left" in action else "right" if "Right" in action else "none"
    return action_side != target_side


def is_wrong_height(item: dict[str, Any]) -> bool:
    action = first_dive_action(item)
    if action == "none":
        return False
    expected = height_band(item)
    actual = (
        "low"
        if action.endswith("Low")
        else "middle"
        if action.endswith("Middle")
        else "high"
        if action.endswith("High")
        else "none"
    )
    return actual != expected


def flatten_episode(item: dict[str, Any]) -> dict[str, Any]:
    row = {}
    for key, value in item.items():
        if isinstance(value, dict) and {"x", "y", "z"} <= set(value):
            row[f"{key}_x"] = value["x"]
            row[f"{key}_y"] = value["y"]
            row[f"{key}_z"] = value["z"]
        elif key == "accepted_action_counts":
            for index, count in enumerate(value):
                row[f"accepted_action_count_{index}"] = count
        elif isinstance(value, list):
            for index, count in enumerate(value):
                row[f"{key}_{index}"] = count
        else:
            row[key] = value
    return row


def write_episodes_csv(path: Path, episodes: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    rows = [flatten_episode(item) for item in episodes]
    if not rows:
        path.write_text("", encoding="utf-8")
        return
    fieldnames = sorted({key for row in rows for key in row})
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def write_summary(path: Path, report: dict[str, Any]) -> None:
    lines = [
        f"# {report['benchmark_id']}",
        "",
        f"Run: `{report['run_id']}`",
        f"Generated: `{report['generated_at']}`",
        "",
        "| Policy | Attempts | Save Rate | Goal Rate | Invalid | Timeout |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for policy in report["policies"]:
        lines.append(
            "| {policy} | {attempts} | {save:.3f} | {goal:.3f} | {invalid:.3f} | {timeout:.3f} |".format(
                policy=policy["policy"],
                attempts=policy["attempts"],
                save=policy["save_rate"]["value"],
                goal=policy["goal_rate"]["value"],
                invalid=policy["invalid_rate"]["value"],
                timeout=policy["timeout_rate"]["value"],
            )
        )
    if report["behavior_name"] in CONTROL_BEHAVIOR_NAMES:
        lines.extend(
            [
                "",
                "## Stage 5 diagnostic",
                "",
                "| Policy | Gate | Glove Contact | Glove Save | High Save | Timely | Immediate | Early | Late | Aim Error (m) | Peak Reach | Reach Gap | Overrides | Clamps |",
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
            ]
        )
        for policy in report["policies"]:
            gate = policy["stage5_diagnostic_gate"]
            high_save = (
                policy["by_height_band"]
                .get("high", {})
                .get("save_rate", {})
                .get("value", 0.0)
            )
            lines.append(
                "| {policy} | {passed}/{total} | {glove:.3f} | {glove_save:.3f} | {high:.3f} | {timely:.3f} | {immediate:.3f} | {premature:.3f} | {late:.3f} | {aim:.3f} | {reach:.3f} | {reach_gap:.3f} | {overrides} | {clamps} |".format(
                    policy=policy["policy"],
                    passed=gate["checks_passed"],
                    total=gate["checks_total"],
                    glove=policy["glove_contact_rate"]["value"],
                    glove_save=policy["glove_save_rate"]["value"],
                    high=high_save,
                    timely=policy["timely_commit_rate"]["value"],
                    immediate=policy["immediate_commit_rate"]["value"],
                    premature=policy["premature_commit_rate"]["value"],
                    late=policy["late_commit_rate"]["value"],
                    aim=policy["first_commit_aim_error_m"]["mean"],
                    reach=policy[
                        "goalkeeper_peak_reach_extension"
                    ]["mean"],
                    reach_gap=policy[
                        "first_commit_reach_shortfall"
                    ]["mean"],
                    overrides=policy["policy_action_override_count"],
                    clamps=policy["control_command_clamp_count"],
                )
            )
        if report["behavior_name"] == CONTROL_V2_BEHAVIOR_NAME:
            lines.extend(
                [
                    "",
                    "## Decision lifecycle",
                    "",
                    "| Policy | Requests | Consumed | Discarded | Accepted | Duplicate | Missing |",
                    "|---|---:|---:|---:|---:|---:|---:|",
                ]
            )
            for policy in report["policies"]:
                lines.append(
                    "| {policy} | {requests} | {consumed} | {discarded} | {accepted} | {duplicate} | {missing} |".format(
                        policy=policy["policy"],
                        requests=policy["policy_decision_request_count"],
                        consumed=policy["policy_decision_consumed_count"],
                        discarded=policy["policy_decision_discarded_count"],
                        accepted=policy["accepted_control_decision_count"],
                        duplicate=policy[
                            "policy_decision_duplicate_request_count"
                        ],
                        missing=policy[
                            "policy_decision_missing_action_count"
                        ],
                    )
                )
        selection = report.get("stage5_diagnostic_selection")
        if selection:
            lines.extend(
                [
                    "",
                    "Selected diagnostic checkpoint: "
                    f"`{selection['selected_policy']}` "
                    f"({selection['reason']}).",
                ]
            )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def build_report(
    *,
    config: BenchmarkConfig,
    run_id: str,
    build_path: Path,
    attempts_per_arena: int,
    policy_reports: list[dict[str, Any]],
    started_at: float,
) -> dict[str, Any]:
    full = attempts_per_arena == config.attempts_per_arena
    comparisons = compare_trained_to_baselines(policy_reports)
    passed = bool(comparisons) and all(item["passed"] for item in comparisons) and all(
        policy["invalid_rate"]["successes"] == 0 and policy["timeout_rate"]["successes"] == 0
        for policy in policy_reports
    )
    status = (
        "passed"
        if passed and full
        else "smoke run; full benchmark gate not evaluated"
        if not full
        else "no trained checkpoint policy evaluated"
        if not comparisons
        else "failed"
    )
    report = {
        "schema_version": 1,
        "benchmark_id": config.benchmark_id,
        "environment_id": config.environment_id,
        "behavior_name": config.behavior_name,
        "observation_spec_id": config.observation_spec_id,
        "reward_spec_id": config.reward_spec_id,
        "action_spec_id": config.action_spec_id,
        "scenario_suite_id": config.scenario_suite_id,
        "build": str(build_path),
        "run_id": run_id,
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "duration_seconds": time.time() - started_at,
        "python": platform.python_version(),
        "python_architecture": platform.machine(),
        "arena_count": config.arena_count,
        "attempts_per_arena": attempts_per_arena,
        "total_attempts": config.arena_count * attempts_per_arena,
        "observation_shapes": [list(shape) for shape in config.observation_shapes],
        "continuous_actions": config.continuous_actions,
        "discrete_branches": list(config.discrete_branches),
        "motor_profile_id": config.motor_profile_id,
        "stage5_gate_profile": config.stage5_gate_profile,
        "environment_parameters": dict(sorted(config.environment_parameters.items())),
        "full_benchmark": full,
        "primary_metric": config.primary_metric,
        "minimum_trained_margin_vs_baselines": 0.05,
        "comparisons": comparisons,
        "passed": passed and full,
        "status": status,
        "policies": policy_reports,
    }
    if config.behavior_name in CONTROL_BEHAVIOR_NAMES:
        report["stage5_diagnostic_selection"] = (
            select_stage5_diagnostic_checkpoint(policy_reports)
        )
    return report


def select_stage5_diagnostic_checkpoint(
    policy_reports: list[dict[str, Any]],
) -> dict[str, Any] | None:
    candidates = [
        policy
        for policy in policy_reports
        if policy.get("policy_type") == "onnx"
        and "stage5_diagnostic_gate" in policy
    ]
    if not candidates:
        return None

    passing = [
        policy
        for policy in candidates
        if policy["stage5_diagnostic_gate"]["passed"]
    ]
    pool = passing or candidates
    selected = max(
        pool,
        key=lambda policy: (
            policy["stage5_diagnostic_gate"]["checks_passed"],
            policy["save_rate"]["value"],
            policy["glove_save_rate"]["value"],
            policy["glove_contact_rate"]["value"],
        ),
    )
    return {
        "selected_policy": selected["policy"],
        "passed": selected["stage5_diagnostic_gate"]["passed"],
        "reason": (
            "passed every Stage 5 diagnostic check"
            if passing
            else "best available checkpoint; diagnostic gate not passed"
        ),
        "checks_passed": selected["stage5_diagnostic_gate"][
            "checks_passed"
        ],
        "checks_total": selected["stage5_diagnostic_gate"][
            "checks_total"
        ],
        "failed_checks": selected["stage5_diagnostic_gate"][
            "failed_checks"
        ],
    }


def compare_trained_to_baselines(policy_reports: list[dict[str, Any]]) -> list[dict[str, Any]]:
    baselines = {
        item["policy"]: item
        for item in policy_reports
        if item["policy"]
        in {
            "stand_center",
            "random_legal",
            "stand_center_v1",
            "random_hybrid_v1",
        }
    }
    trained = [item for item in policy_reports if item["policy_type"] == "onnx"]
    comparisons = []
    for policy in trained:
        for baseline_name, baseline in sorted(baselines.items()):
            margin = policy["save_rate"]["value"] - baseline["save_rate"]["value"]
            comparisons.append(
                {
                    "policy": policy["policy"],
                    "baseline": baseline_name,
                    "save_rate_margin": margin,
                    "passed": margin >= 0.05,
                }
            )
    return comparisons


def run_benchmark(args: argparse.Namespace) -> dict[str, Any]:
    started_at = time.time()
    config = load_benchmark_config(args.benchmark)
    attempts_per_arena = args.attempts_per_arena or config.attempts_per_arena
    build_path = args.build.expanduser().resolve()
    if not build_path.exists():
        raise FileNotFoundError(build_path)

    run_id = args.run_id or f"stage3-{datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')}"
    output_dir = args.output_root / run_id
    output_dir.mkdir(parents=True, exist_ok=True)

    all_episodes: list[dict[str, Any]] = []
    policy_reports: list[dict[str, Any]] = []
    for index, policy_spec in enumerate(args.policy):
        policy = make_policy(
            policy_spec,
            config.master_seed + index,
            config,
        )
        episodes = run_policy(
            build_path=build_path,
            config=config,
            policy=policy,
            attempts_per_arena=attempts_per_arena,
            worker_id=args.worker_id_start + index,
            player_log_path=output_dir / "player_logs" / f"{safe_name(policy.name)}.log",
            timeout_wait=args.timeout_wait,
            max_environment_steps=args.max_environment_steps,
        )
        all_episodes.extend(episodes)
        policy_reports.append(
            aggregate_policy(policy, episodes, config, attempts_per_arena)
        )

    report = build_report(
        config=config,
        run_id=run_id,
        build_path=build_path,
        attempts_per_arena=attempts_per_arena,
        policy_reports=policy_reports,
        started_at=started_at,
    )
    write_episodes_csv(output_dir / "episodes.csv", all_episodes)
    (output_dir / "report.json").write_text(
        json.dumps(report, indent=2) + "\n",
        encoding="utf-8",
    )
    write_summary(output_dir / "summary.md", report)
    if args.canonical_report is not None:
        args.canonical_report.parent.mkdir(parents=True, exist_ok=True)
        args.canonical_report.write_text(
            json.dumps(compact_report(report), indent=2) + "\n",
            encoding="utf-8",
        )
    return report


def compact_report(report: dict[str, Any]) -> dict[str, Any]:
    return {
        "schema_version": report["schema_version"],
        "benchmark_id": report["benchmark_id"],
        "environment_id": report["environment_id"],
        "behavior_name": report["behavior_name"],
        "observation_spec_id": report["observation_spec_id"],
        "reward_spec_id": report["reward_spec_id"],
        "action_spec_id": report.get("action_spec_id", "goalkeeper-discrete-v0"),
        "scenario_suite_id": report.get("scenario_suite_id", "on-target-v0"),
        "run_id": report["run_id"],
        "generated_at": report["generated_at"],
        "full_benchmark": report["full_benchmark"],
        "arena_count": report["arena_count"],
        "attempts_per_arena": report["attempts_per_arena"],
        "total_attempts": report["total_attempts"],
        "observation_shapes": report.get("observation_shapes", OBSERVATION_SHAPES),
        "continuous_actions": report.get("continuous_actions", 0),
        "discrete_branches": report.get("discrete_branches", DISCRETE_BRANCHES),
        "motor_profile_id": report.get("motor_profile_id"),
        "stage5_gate_profile": report.get(
            "stage5_gate_profile",
            "control-v2",
        ),
        "environment_parameters": report.get("environment_parameters", {}),
        "primary_metric": report["primary_metric"],
        "comparisons": report["comparisons"],
        "stage5_diagnostic_selection": report.get(
            "stage5_diagnostic_selection"
        ),
        "passed": report["passed"],
        "status": report["status"],
        "policies": [
            {
                "policy": policy["policy"],
                "policy_type": policy["policy_type"],
                "attempts": policy["attempts"],
                "expected_attempts": policy["expected_attempts"],
                "complete": policy["complete"],
                "outcomes": policy["outcomes"],
                "save_rate": policy["save_rate"],
                "goal_rate": policy["goal_rate"],
                "invalid_rate": policy["invalid_rate"],
                "timeout_rate": policy["timeout_rate"],
                "glove_contact_rate": policy["glove_contact_rate"],
                "goalkeeper_contact_rate": policy["goalkeeper_contact_rate"],
                "contact_then_goal_rate": policy["contact_then_goal_rate"],
                "wrong_side_rate": policy["wrong_side_rate"],
                "wrong_height_rate": policy["wrong_height_rate"],
                "action_mask_violations": policy["action_mask_violations"],
                "duplicate_terminal_events": policy["duplicate_terminal_events"],
                "action_usage": policy["action_usage"],
                "by_quadrant": policy["by_quadrant"],
                "by_height_band": policy["by_height_band"],
                "by_horizontal_band": policy["by_horizontal_band"],
                "by_flight_time_band": policy["by_flight_time_band"],
                "by_first_dive_action": policy["by_first_dive_action"],
                **(
                    {
                        "commit_rate": policy["commit_rate"],
                        "first_commit_ball_flight_time":
                            policy["first_commit_ball_flight_time"],
                        "first_commit_aim_error_m":
                            policy["first_commit_aim_error_m"],
                        "first_commit_visible_time_to_goal_plane":
                            policy[
                                "first_commit_visible_time_to_goal_plane"
                            ],
                        "first_commit_reach_demand":
                            policy["first_commit_reach_demand"],
                        "first_commit_reach_extension":
                            policy["first_commit_reach_extension"],
                        "immediate_commit_rate":
                            policy["immediate_commit_rate"],
                        "premature_commit_rate":
                            policy["premature_commit_rate"],
                        "late_commit_rate":
                            policy["late_commit_rate"],
                        "timely_commit_rate":
                            policy["timely_commit_rate"],
                        "first_commit_visible_aim_error_m":
                            policy["first_commit_visible_aim_error_m"],
                        "first_commit_desired_reach":
                            policy["first_commit_desired_reach"],
                        "first_commit_reach_shortfall":
                            policy["first_commit_reach_shortfall"],
                        "first_eligible_commit_decision_index":
                            policy[
                                "first_eligible_commit_decision_index"
                            ],
                        "first_eligible_commit_ball_flight_time":
                            policy[
                                "first_eligible_commit_ball_flight_time"
                            ],
                        "eligible_commit_decisions_before_commit":
                            policy[
                                "eligible_commit_decisions_before_commit"
                            ],
                        "goalkeeper_root_distance_m":
                            policy["goalkeeper_root_distance_m"],
                        "goalkeeper_peak_root_speed_mps":
                            policy["goalkeeper_peak_root_speed_mps"],
                        "goalkeeper_peak_reach_extension":
                            policy["goalkeeper_peak_reach_extension"],
                        "minimum_glove_ball_distance_m":
                            policy["minimum_glove_ball_distance_m"],
                        "control_command_clamp_count":
                            policy["control_command_clamp_count"],
                        "control_command_clamp_attempt_rate":
                            policy[
                                "control_command_clamp_attempt_rate"
                            ],
                        "control_target_clamp_count":
                            policy["control_target_clamp_count"],
                        "control_target_clamp_attempt_rate":
                            policy["control_target_clamp_attempt_rate"],
                        "root_target_saturation_count":
                            policy["root_target_saturation_count"],
                        "root_target_saturation_attempt_rate":
                            policy[
                                "root_target_saturation_attempt_rate"
                            ],
                        "root_target_saturation_distance_m":
                            policy[
                                "root_target_saturation_distance_m"
                            ],
                        "training_decision_shaping_reward":
                            policy[
                                "training_decision_shaping_reward"
                            ],
                        "policy_action_override_count":
                            policy["policy_action_override_count"],
                        "saturated_shot_save_rate":
                            policy["saturated_shot_save_rate"],
                        "glove_save_rate":
                            policy["glove_save_rate"],
                        "glove_first_save_rate":
                            policy["glove_first_save_rate"],
                        "arm_save_rate":
                            policy["arm_save_rate"],
                        "body_save_rate":
                            policy["body_save_rate"],
                        "glove_first_contact_rate":
                            policy["glove_first_contact_rate"],
                        "body_first_contact_rate":
                            policy["body_first_contact_rate"],
                        "control_usage": policy["control_usage"],
                        "by_commit_status": policy["by_commit_status"],
                        "by_first_commit_aim_region":
                            policy["by_first_commit_aim_region"],
                        "by_first_commit_timing_band":
                            policy["by_first_commit_timing_band"],
                        "by_first_contact_part":
                            policy["by_first_contact_part"],
                        "stage5_diagnostic_gate":
                            policy["stage5_diagnostic_gate"],
                    }
                    if "commit_rate" in policy
                    else {}
                ),
            }
            for policy in report["policies"]
        ],
    }


def safe_name(value: str) -> str:
    return "".join(character if character.isalnum() else "_" for character in value)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--benchmark",
        type=Path,
        default=Path("configs/benchmarks/goalkeeper-state-v0-id-20k.json"),
    )
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument(
        "--policy",
        action="append",
        default=[],
        help=(
            "Policy spec: stand_center, random_legal, reactive_side, "
            "linear_intercept, stand_center_v1, random_hybrid_v1, "
            "reactive_reach_v1, or onnx:path"
        ),
    )
    parser.add_argument("--attempts-per-arena", type=int)
    parser.add_argument("--run-id")
    parser.add_argument("--output-root", type=Path, default=Path("results/evaluations"))
    parser.add_argument("--canonical-report", type=Path)
    parser.add_argument("--worker-id-start", type=int, default=120)
    parser.add_argument("--timeout-wait", type=int, default=120)
    parser.add_argument("--max-environment-steps", type=int, default=2_000_000)
    args = parser.parse_args()
    if not args.policy:
        args.policy = ["stand_center", "random_legal"]
    return args


def main() -> int:
    report = run_benchmark(parse_args())
    print(json.dumps(compact_report(report), indent=2))
    return 0 if report["status"] != "failed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
