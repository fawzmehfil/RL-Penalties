using System;
using System.Text;
using PenaltyShootout.Kernel;
using Unity.MLAgents.SideChannels;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    public static class GoalkeeperBenchmarkTelemetry
    {
        public const string EnableFlag = "--stage3-benchmark-telemetry";
        public const string BenchmarkIdArgument = "--benchmark-id";
        public const string ChannelId = "b8d7b5b3-bfa6-4c46-9a3f-2e34d9fd7a31";

        private static RawBytesChannel channel;
        private static bool registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeIfEnabled()
        {
            if (!CommandLineEnablesTelemetry())
            {
                return;
            }

            EnsureRegistered();
        }

        public static bool CommandLineEnablesTelemetry(string[] args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            foreach (var argument in args)
            {
                if (argument == EnableFlag ||
                    argument.StartsWith(EnableFlag + "=", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Emit(
            AttemptResult result,
            float reward,
            string observationSpecId = null,
            GoalkeeperPartialObservationSettings observationSettings = default)
        {
            if (result == null || !CommandLineEnablesTelemetry())
            {
                return;
            }

            EnsureRegistered();
            var json = CreateJson(result, reward, observationSpecId, observationSettings);
            channel.SendRawBytes(Encoding.UTF8.GetBytes(json));
        }

        public static string CreateJson(
            AttemptResult result,
            float reward,
            string observationSpecId = null,
            GoalkeeperPartialObservationSettings observationSettings = default)
        {
            return JsonUtility.ToJson(
                CreatePayload(result, reward, observationSpecId, observationSettings));
        }

        private static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            channel = new RawBytesChannel(new Guid(ChannelId));
            SideChannelManager.RegisterSideChannel(channel);
            registered = true;
        }

        public static string CurrentBenchmarkId(string[] args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith(BenchmarkIdArgument + "=", StringComparison.Ordinal))
                {
                    return argument.Substring(BenchmarkIdArgument.Length + 1);
                }

                if (argument == BenchmarkIdArgument && index + 1 < args.Length)
                {
                    return args[index + 1];
                }
            }

            return KernelConstants.Stage3BenchmarkId;
        }

        private static Payload CreatePayload(
            AttemptResult result,
            float reward,
            string observationSpecId,
            GoalkeeperPartialObservationSettings observationSettings)
        {
            var firstDive = result.FirstDiveDecisionIndex >= 0;
            return new Payload
            {
                schema_version = 1,
                message_type = "stage3_attempt_result",
                benchmark_id = CurrentBenchmarkId(),
                environment_id = result.EnvironmentId,
                behavior_name =
                    observationSpecId == KernelConstants.GoalkeeperPartialObservationSpecId
                        ? KernelConstants.GoalkeeperRobustBehaviorName
                        : KernelConstants.GoalkeeperStateBehaviorName,
                observation_spec_id = string.IsNullOrEmpty(observationSpecId)
                    ? KernelConstants.GoalkeeperStateObservationSpecId
                    : observationSpecId,
                reward_spec_id = KernelConstants.GoalkeeperSparseRewardSpecId,
                scenario_suite_id = result.ScenarioSuiteId,
                stage4_obs_delay_steps = observationSettings.DelaySteps,
                stage4_ball_position_noise_m =
                    observationSettings.BallPositionNoiseMeters,
                stage4_ball_velocity_noise_mps =
                    observationSettings.BallVelocityNoiseMetersPerSecond,
                stage4_keeper_position_noise_m =
                    observationSettings.GoalkeeperPositionNoiseMeters,
                stage4_dropout_probability = observationSettings.DropoutProbability,
                attempt_id = result.AttemptId,
                arena_id = result.ArenaId,
                seed = result.Seed.ToString(),
                outcome = result.Outcome.ToString(),
                outcome_id = (int)result.Outcome,
                reward = reward,
                saved = result.Outcome == AttemptOutcome.Saved ||
                    result.Outcome == AttemptOutcome.BlockedThenOut,
                attempt_time = result.AttemptTime,
                ball_flight_time = result.BallFlightTime,
                goalkeeper_contact = result.GoalkeeperContact,
                goal_frame_contact = result.GoalFrameContact,
                goalkeeper_contact_count = result.GoalkeeperContactCount,
                goal_frame_contact_count = result.GoalFrameContactCount,
                last_goalkeeper_contact_part = result.LastGoalkeeperContactPart.ToString(),
                last_goalkeeper_contact_part_id = (int)result.LastGoalkeeperContactPart,
                glove_contact = result.GloveContact,
                glove_contact_count = result.GloveContactCount,
                left_glove_contact_count = result.LeftGloveContactCount,
                right_glove_contact_count = result.RightGloveContactCount,
                arm_contact_count = result.ArmContactCount,
                torso_or_head_contact_count = result.TorsoOrHeadContactCount,
                leg_contact_count = result.LegContactCount,
                requested_target_local = VectorPayload.From(result.RequestedTargetLocal),
                has_centre_plane_intersection = result.HasCentrePlaneIntersection,
                measured_centre_plane_intersection_local =
                    VectorPayload.From(result.MeasuredCentrePlaneIntersectionLocal),
                target_error = result.TargetError,
                initial_action = result.InitialAction.ToString(),
                initial_action_id = (int)result.InitialAction,
                last_action = result.LastAction.ToString(),
                last_action_id = (int)result.LastAction,
                has_first_dive = firstDive,
                first_accepted_dive_action = firstDive
                    ? result.FirstAcceptedDiveAction.ToString()
                    : string.Empty,
                first_accepted_dive_action_id = firstDive
                    ? (int)result.FirstAcceptedDiveAction
                    : -1,
                first_dive_decision_index = result.FirstDiveDecisionIndex,
                first_dive_attempt_time = result.FirstDiveAttemptTime,
                first_dive_ball_flight_time = result.FirstDiveBallFlightTime,
                accepted_action_counts = result.AcceptedActionCounts ??
                    new int[KernelConstants.GoalkeeperActionCount],
                action_mask_violations = result.ActionMaskViolations,
                duplicate_terminal_events = result.DuplicateTerminalEvents,
            };
        }

        [Serializable]
        private sealed class Payload
        {
            public int schema_version;
            public string message_type;
            public string benchmark_id;
            public string environment_id;
            public string behavior_name;
            public string observation_spec_id;
            public string reward_spec_id;
            public string scenario_suite_id;
            public int stage4_obs_delay_steps;
            public float stage4_ball_position_noise_m;
            public float stage4_ball_velocity_noise_mps;
            public float stage4_keeper_position_noise_m;
            public float stage4_dropout_probability;
            public long attempt_id;
            public int arena_id;
            public string seed;
            public string outcome;
            public int outcome_id;
            public float reward;
            public bool saved;
            public float attempt_time;
            public float ball_flight_time;
            public bool goalkeeper_contact;
            public bool goal_frame_contact;
            public int goalkeeper_contact_count;
            public int goal_frame_contact_count;
            public string last_goalkeeper_contact_part;
            public int last_goalkeeper_contact_part_id;
            public bool glove_contact;
            public int glove_contact_count;
            public int left_glove_contact_count;
            public int right_glove_contact_count;
            public int arm_contact_count;
            public int torso_or_head_contact_count;
            public int leg_contact_count;
            public VectorPayload requested_target_local;
            public bool has_centre_plane_intersection;
            public VectorPayload measured_centre_plane_intersection_local;
            public float target_error;
            public string initial_action;
            public int initial_action_id;
            public string last_action;
            public int last_action_id;
            public bool has_first_dive;
            public string first_accepted_dive_action;
            public int first_accepted_dive_action_id;
            public int first_dive_decision_index;
            public float first_dive_attempt_time;
            public float first_dive_ball_flight_time;
            public int[] accepted_action_counts;
            public int action_mask_violations;
            public int duplicate_terminal_events;
        }

        [Serializable]
        private struct VectorPayload
        {
            public float x;
            public float y;
            public float z;

            public static VectorPayload From(Vector3 vector)
            {
                return new VectorPayload
                {
                    x = vector.x,
                    y = vector.y,
                    z = vector.z,
                };
            }
        }
    }
}
