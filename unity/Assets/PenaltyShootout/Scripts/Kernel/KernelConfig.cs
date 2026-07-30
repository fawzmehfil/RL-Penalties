using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class KernelConstants
    {
        public const string EnvironmentId = "penalty-shootout-kernel-v1";
        public const string ScenarioSuiteId = "on-target-v0";
        public const string ActionSpecId = "goalkeeper-discrete-v0";
        public const string MotorProfileId = "keeper-proxy-hands-v1";
        public const string BehaviorName = "GoalkeeperKernel-v0";
        public const string GoalkeeperStateBehaviorName = "GoalkeeperState-v0";
        public const string GoalkeeperRobustBehaviorName = "GoalkeeperRobust-v0";
        public const string GoalkeeperControlBehaviorName = "GoalkeeperControl-v1";
        public const string GoalkeeperControlV2BehaviorName = "GoalkeeperControl-v2";
        public const string GoalkeeperStateObservationSpecId = "state-v0";
        public const string GoalkeeperPartialObservationSpecId = "state-po-v0";
        public const string GoalkeeperControlObservationSpecId = "control-state-v1";
        public const string GoalkeeperControlV2ObservationSpecId = "control-state-v2";
        public const string GoalkeeperSparseRewardSpecId = "goalkeeper-sparse-v0";
        public const string GoalkeeperControlActionSpecId = "goalkeeper-hybrid-v1";
        public const string GoalkeeperControlMotorProfileId = "keeper-control-v1";
        public const int GoalkeeperStateObservationSize = 24;
        public const int GoalkeeperControlObservationSize = 32;
        public const int GoalkeeperControlV2ObservationSize = 35;
        public const int GoalkeeperActionCount = 9;
        public const string Stage3BenchmarkId = "goalkeeper-state-v0-id-20k";
        public const string Stage4InDistributionBenchmarkId = "goalkeeper-robust-v0-id-20k";
        public const string Stage4DelayNoiseBenchmarkId =
            "goalkeeper-robust-v0-delay-noise-20k";
        public const string Stage4SpeedOodBenchmarkId = "goalkeeper-robust-v0-speed-ood-20k";
        public const string Stage4EdgeOodBenchmarkId = "goalkeeper-robust-v0-edge-ood-20k";
        public const string Stage5InDistributionBenchmarkId =
            "goalkeeper-control-v1-id-20k";
        public const string Stage5SpeedOodBenchmarkId =
            "goalkeeper-control-v1-speed-ood-20k";
        public const string Stage5EdgeOodBenchmarkId =
            "goalkeeper-control-v1-edge-ood-20k";
        public const string Stage5V2InDistributionBenchmarkId =
            "goalkeeper-control-v2-id-20k";
        public const int ManifestSchemaVersion = 2;
        public const int AcceptanceSchemaVersion = 2;

        public const float GoalInsideWidth = 7.32f;
        public const float GoalHalfWidth = GoalInsideWidth * 0.5f;
        public const float CrossbarLowerEdge = 2.44f;
        public const float FrameThickness = 0.12f;
        public const float PenaltyMarkDistance = 11f;
        public const float BallRadius = 0.11f;
        public const float BallMass = 0.43f;
        public const float TargetTolerance = 0.05f;

        public static readonly Vector3 CanonicalLaunch =
            new Vector3(0f, BallRadius, PenaltyMarkDistance);
    }

    [Serializable]
    internal sealed class KernelManifest
    {
        public int schema_version;
        public string environment_id;
        public string unity_editor;
        public string ml_agents_unity;
        public string scenario_suite_id;
        public string goalkeeper_action_spec_id;
        public string goalkeeper_motor_profile_id;
        public ManifestPhysics physics;
        public ManifestGoal goal;
        public ManifestBall ball;
        public ManifestAttempt attempt;
        public ManifestShots shots;
        public ManifestMotor motor;
    }

    [Serializable]
    internal sealed class GoalkeeperStateManifest
    {
        public int schema_version;
        public string environment_id;
        public string behavior_name;
        public string observation_spec_id;
        public string reward_spec_id;
        public string action_spec_id;
        public string scenario_suite_id;
        public int vector_observation_size;
        public int[] discrete_branches;
        public string[] observation_order;
        public string[] excluded_privileged_fields;
        public ManifestPartialObservation partial_observation;
    }

    [Serializable]
    internal sealed class ManifestPartialObservation
    {
        public string source;
        public string delay_application;
        public string noise_application;
        public float[] clamp_range;
        public string[] controlled_by_environment_parameters;
    }

    [Serializable]
    internal sealed class GoalkeeperControlManifest
    {
        public int schema_version;
        public string environment_id;
        public string behavior_name;
        public string observation_spec_id;
        public string reward_spec_id;
        public string action_spec_id;
        public string motor_profile_id;
        public string scenario_suite_id;
        public int vector_observation_size;
        public int continuous_actions;
        public int[] discrete_branches;
        public string[] continuous_action_order;
        public string[] discrete_action_order;
        public string[] observation_order;
        public string[] excluded_privileged_fields;
        public ManifestControlMotor motor;
    }

    [Serializable]
    internal sealed class ManifestControlMotor
    {
        public string[] states;
        public float lateral_limit_m;
        public float maximum_move_speed_m_s;
        public float move_acceleration_m_s2;
        public float move_deceleration_m_s2;
        public float plant_duration_s;
        public float minimum_dive_duration_s;
        public float maximum_dive_duration_s;
        public float recovery_duration_s;
        public float maximum_dive_lateral_displacement_m;
        public float maximum_dive_root_height_m;
        public float maximum_body_roll_degrees;
        public float maximum_midair_aim_correction_m;
        public float plant_reach_fraction;
        public ManifestControlBody body;
        public ManifestControlArms arms;
    }

    [Serializable]
    internal sealed class ManifestControlBody
    {
        public float torso_center_height_m;
        public float torso_forward_m;
        public float[] torso_scale;
        public float torso_forward_lean_degrees;
        public float head_center_height_m;
        public float head_forward_m;
        public float head_diameter_m;
        public float leg_lateral_m;
        public float leg_center_height_m;
        public float leg_forward_m;
        public float[] leg_scale;
        public float leg_splay_degrees;
        public float leg_forward_lean_degrees;
    }

    [Serializable]
    internal sealed class ManifestControlArms
    {
        public string solver;
        public string control;
        public float upper_arm_length_m;
        public float forearm_length_m;
        public float arm_radius_m;
        public float glove_radius_m;
        public float maximum_glove_target_speed_m_s;
        public float glove_separation_m;
    }

    [Serializable]
    internal sealed class ManifestPhysics
    {
        public float fixed_timestep_s;
        public int decision_period_ticks;
        public float[] gravity_m_s2;
        public string curve_model;
        public string noise;
    }

    [Serializable]
    internal sealed class ManifestGoal
    {
        public float inside_width_m;
        public float crossbar_lower_edge_m;
        public float frame_thickness_m;
        public float penalty_mark_distance_m;
    }

    [Serializable]
    internal sealed class ManifestBall
    {
        public float radius_m;
        public float mass_kg;
        public string collision_detection;
    }

    [Serializable]
    internal sealed class ManifestAttempt
    {
        public int reset_stabilization_ticks;
        public float ready_duration_s;
        public float timeout_s;
        public float post_contact_safety_horizon_s;
        public float rest_speed_threshold_m_s;
        public float rest_dwell_s;
        public float[] danger_minimum_m;
        public float[] danger_maximum_m;
    }

    [Serializable]
    internal sealed class ManifestShots
    {
        public float target_x_min;
        public float target_x_max;
        public float target_y_min;
        public float target_y_max;
        public float flight_time_min_s;
        public float flight_time_max_s;
        public float launch_delay_min_s;
        public float launch_delay_max_s;
        public float additional_frame_clearance_m;
        public bool spin_enabled;
        public bool curve_enabled;
    }

    [Serializable]
    internal sealed class ManifestMotor
    {
        public float lateral_limit_m;
        public float maximum_shuffle_speed_m_s;
        public float shuffle_acceleration_m_s2;
        public float shuffle_deceleration_m_s2;
        public float dive_duration_s;
        public float recovery_duration_s;
        public float low_reach_m;
        public float middle_reach_m;
        public float high_reach_m;
        public float maximum_body_roll_degrees;
        public int[] discrete_actions;
        public ManifestHands hands;
        public string[] goalkeeper_contact_parts;
    }

    [Serializable]
    internal sealed class ManifestHands
    {
        public float reach_start_normalized;
        public float full_extension_normalized;
        public float leading_low_lateral_reach_m;
        public float trailing_low_lateral_reach_m;
        public float leading_middle_lateral_reach_m;
        public float trailing_middle_lateral_reach_m;
        public float leading_high_lateral_reach_m;
        public float trailing_high_lateral_reach_m;
        public float leading_low_height_m;
        public float trailing_low_height_m;
        public float leading_middle_height_m;
        public float trailing_middle_height_m;
        public float leading_high_height_m;
        public float trailing_high_height_m;
        public float leading_forward_reach_m;
        public float trailing_forward_reach_m;
        public float glove_radius_m;
        public float arm_radius_m;
        public float maximum_arm_length_m;
        public float ready_glove_lateral_m;
        public float ready_glove_height_m;
        public float ready_glove_forward_m;
        public float shoulder_lateral_m;
        public float shoulder_height_m;
        public float shoulder_forward_m;
    }

    public static class KernelManifestUtility
    {
        public static string CreateJson(
            EnvironmentKernelConfig environment,
            ShotDistributionConfig shots,
            GoalkeeperMotorConfig motor,
            bool prettyPrint = true)
        {
            if (environment == null || shots == null || motor == null)
            {
                throw new ArgumentNullException("Kernel manifest configurations must not be null.");
            }

            if (!environment.Validate(out var environmentError))
            {
                throw new InvalidOperationException(environmentError);
            }

            if (!shots.Validate(out var shotError))
            {
                throw new InvalidOperationException(shotError);
            }

            if (!motor.Validate(out var motorError))
            {
                throw new InvalidOperationException(motorError);
            }

            var manifest = new KernelManifest
            {
                schema_version = KernelConstants.ManifestSchemaVersion,
                environment_id = environment.EnvironmentId,
                unity_editor = Application.unityVersion,
                ml_agents_unity = "4.0.0",
                scenario_suite_id = shots.ScenarioSuiteId,
                goalkeeper_action_spec_id = KernelConstants.ActionSpecId,
                goalkeeper_motor_profile_id = motor.MotorProfileId,
                physics = new ManifestPhysics
                {
                    fixed_timestep_s = environment.FixedTimestep,
                    decision_period_ticks = environment.DecisionPeriodTicks,
                    gravity_m_s2 = Vector(Physics.gravity),
                    curve_model = shots.CurveEnabled ? "enabled" : "disabled",
                    noise = shots.AimNoiseEnabled || shots.PowerNoiseEnabled ? "enabled" : "disabled",
                },
                goal = new ManifestGoal
                {
                    inside_width_m = KernelConstants.GoalInsideWidth,
                    crossbar_lower_edge_m = KernelConstants.CrossbarLowerEdge,
                    frame_thickness_m = KernelConstants.FrameThickness,
                    penalty_mark_distance_m = KernelConstants.PenaltyMarkDistance,
                },
                ball = new ManifestBall
                {
                    radius_m = KernelConstants.BallRadius,
                    mass_kg = KernelConstants.BallMass,
                    collision_detection = "ContinuousDynamic",
                },
                attempt = new ManifestAttempt
                {
                    reset_stabilization_ticks = environment.ResetStabilizationTicks,
                    ready_duration_s = environment.ReadyDuration,
                    timeout_s = environment.AttemptTimeout,
                    post_contact_safety_horizon_s = environment.PostContactSafetyHorizon,
                    rest_speed_threshold_m_s = environment.RestSpeedThreshold,
                    rest_dwell_s = environment.RestDwellTime,
                    danger_minimum_m = Vector(environment.DangerMinimum),
                    danger_maximum_m = Vector(environment.DangerMaximum),
                },
                shots = new ManifestShots
                {
                    target_x_min = -1f,
                    target_x_max = 1f,
                    target_y_min = 0f,
                    target_y_max = 1f,
                    flight_time_min_s = shots.MinimumFlightTime,
                    flight_time_max_s = shots.MaximumFlightTime,
                    launch_delay_min_s = shots.MinimumLaunchDelay,
                    launch_delay_max_s = shots.MaximumLaunchDelay,
                    additional_frame_clearance_m = shots.AdditionalFrameClearance,
                    spin_enabled = shots.SpinEnabled,
                    curve_enabled = shots.CurveEnabled,
                },
                motor = new ManifestMotor
                {
                    lateral_limit_m = motor.LateralLimit,
                    maximum_shuffle_speed_m_s = motor.MaximumShuffleSpeed,
                    shuffle_acceleration_m_s2 = motor.ShuffleAcceleration,
                    shuffle_deceleration_m_s2 = motor.ShuffleDeceleration,
                    dive_duration_s = motor.DiveDuration,
                    recovery_duration_s = motor.RecoveryDuration,
                    low_reach_m = motor.LowDiveReach,
                    middle_reach_m = motor.MiddleDiveReach,
                    high_reach_m = motor.HighDiveReach,
                    maximum_body_roll_degrees = motor.MaximumBodyRollDegrees,
                    discrete_actions = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
                    hands = new ManifestHands
                    {
                        reach_start_normalized = motor.ReachStartNormalized,
                        full_extension_normalized = motor.FullExtensionNormalized,
                        leading_low_lateral_reach_m =
                            motor.LeadingLowLateralReach,
                        trailing_low_lateral_reach_m =
                            motor.TrailingLowLateralReach,
                        leading_middle_lateral_reach_m =
                            motor.LeadingMiddleLateralReach,
                        trailing_middle_lateral_reach_m =
                            motor.TrailingMiddleLateralReach,
                        leading_high_lateral_reach_m =
                            motor.LeadingHighLateralReach,
                        trailing_high_lateral_reach_m =
                            motor.TrailingHighLateralReach,
                        leading_low_height_m = motor.LeadingLowHeight,
                        trailing_low_height_m = motor.TrailingLowHeight,
                        leading_middle_height_m = motor.LeadingMiddleHeight,
                        trailing_middle_height_m = motor.TrailingMiddleHeight,
                        leading_high_height_m = motor.LeadingHighHeight,
                        trailing_high_height_m = motor.TrailingHighHeight,
                        leading_forward_reach_m = motor.LeadingForwardReach,
                        trailing_forward_reach_m = motor.TrailingForwardReach,
                        glove_radius_m = motor.GloveRadius,
                        arm_radius_m = motor.ArmRadius,
                        maximum_arm_length_m = motor.MaximumArmLength,
                        ready_glove_lateral_m = motor.ReadyGloveLateral,
                        ready_glove_height_m = motor.ReadyGloveHeight,
                        ready_glove_forward_m = motor.ReadyGloveForward,
                        shoulder_lateral_m = motor.ShoulderLateral,
                        shoulder_height_m = motor.ShoulderHeight,
                        shoulder_forward_m = motor.ShoulderForward,
                    },
                    goalkeeper_contact_parts = new[]
                    {
                        GoalkeeperContactPart.LeftGlove.ToString(),
                        GoalkeeperContactPart.RightGlove.ToString(),
                        GoalkeeperContactPart.Arm.ToString(),
                        GoalkeeperContactPart.TorsoOrHead.ToString(),
                        GoalkeeperContactPart.Leg.ToString(),
                    },
                },
            };

            return JsonUtility.ToJson(manifest, prettyPrint) + "\n";
        }

        public static string CreateGoalkeeperStateJson(bool prettyPrint = true)
        {
            return CreateGoalkeeperObservationManifestJson(
                KernelConstants.GoalkeeperStateBehaviorName,
                KernelConstants.GoalkeeperStateObservationSpecId,
                false,
                prettyPrint);
        }

        public static string CreateGoalkeeperRobustJson(bool prettyPrint = true)
        {
            return CreateGoalkeeperObservationManifestJson(
                KernelConstants.GoalkeeperRobustBehaviorName,
                KernelConstants.GoalkeeperPartialObservationSpecId,
                true,
                prettyPrint);
        }

        public static string CreateGoalkeeperControlJson(
            GoalkeeperControlMotorConfig motor,
            bool prettyPrint = true)
        {
            return CreateGoalkeeperControlJson(
                motor,
                KernelConstants.GoalkeeperControlBehaviorName,
                KernelConstants.GoalkeeperControlObservationSpecId,
                KernelConstants.GoalkeeperControlObservationSize,
                false,
                prettyPrint);
        }

        public static string CreateGoalkeeperControlV2Json(
            GoalkeeperControlMotorConfig motor,
            bool prettyPrint = true)
        {
            return CreateGoalkeeperControlJson(
                motor,
                KernelConstants.GoalkeeperControlV2BehaviorName,
                KernelConstants.GoalkeeperControlV2ObservationSpecId,
                KernelConstants.GoalkeeperControlV2ObservationSize,
                true,
                prettyPrint);
        }

        private static string CreateGoalkeeperControlJson(
            GoalkeeperControlMotorConfig motor,
            string behaviorName,
            string observationSpecId,
            int observationSize,
            bool includeVisibleBallisticPrediction,
            bool prettyPrint)
        {
            if (motor == null)
            {
                throw new ArgumentNullException(nameof(motor));
            }

            if (!motor.Validate(out var error))
            {
                throw new InvalidOperationException(error);
            }

            var manifest = new GoalkeeperControlManifest
            {
                schema_version = 1,
                environment_id = KernelConstants.EnvironmentId,
                behavior_name = behaviorName,
                observation_spec_id = observationSpecId,
                reward_spec_id = KernelConstants.GoalkeeperSparseRewardSpecId,
                action_spec_id = KernelConstants.GoalkeeperControlActionSpecId,
                motor_profile_id =
                    KernelConstants.GoalkeeperControlMotorProfileId,
                scenario_suite_id = KernelConstants.ScenarioSuiteId,
                vector_observation_size = observationSize,
                continuous_actions =
                    GoalkeeperControlSpace.ContinuousActionCount,
                discrete_branches = new[]
                {
                    GoalkeeperControlSpace.CommitBranchSize,
                },
                continuous_action_order = new[]
                {
                    "move_x",
                    "aim_x",
                    "aim_y",
                    "reach",
                },
                discrete_action_order = new[]
                {
                    "no_commit",
                    "commit_save",
                },
                observation_order = ControlObservationOrder(
                    includeVisibleBallisticPrediction),
                excluded_privileged_fields = new[]
                {
                    "requested_target",
                    "future_goal_plane_intersection",
                    "launch_velocity",
                    "sampled_flight_time_parameter",
                    "terminal_outcome",
                },
                motor = new ManifestControlMotor
                {
                    states = new[]
                    {
                        GoalkeeperControlMotorState.Ready.ToString(),
                        GoalkeeperControlMotorState.Moving.ToString(),
                        GoalkeeperControlMotorState.Planting.ToString(),
                        GoalkeeperControlMotorState.Diving.ToString(),
                        GoalkeeperControlMotorState.Recovering.ToString(),
                    },
                    lateral_limit_m = motor.LateralLimit,
                    maximum_move_speed_m_s = motor.MaximumMoveSpeed,
                    move_acceleration_m_s2 = motor.MoveAcceleration,
                    move_deceleration_m_s2 = motor.MoveDeceleration,
                    plant_duration_s = motor.PlantDuration,
                    minimum_dive_duration_s = motor.MinimumDiveDuration,
                    maximum_dive_duration_s = motor.MaximumDiveDuration,
                    recovery_duration_s = motor.RecoveryDuration,
                    maximum_dive_lateral_displacement_m =
                        motor.MaximumDiveLateralDisplacement,
                    maximum_dive_root_height_m =
                        motor.MaximumDiveRootHeight,
                    maximum_body_roll_degrees =
                        motor.MaximumBodyRollDegrees,
                    maximum_midair_aim_correction_m =
                        motor.MaximumAimCorrection,
                    plant_reach_fraction =
                        motor.PlantReachFraction,
                    body = new ManifestControlBody
                    {
                        torso_center_height_m =
                            motor.TorsoCenterHeight,
                        torso_forward_m = motor.TorsoForward,
                        torso_scale = new[]
                        {
                            motor.TorsoScale.x,
                            motor.TorsoScale.y,
                            motor.TorsoScale.z,
                        },
                        torso_forward_lean_degrees =
                            motor.TorsoForwardLeanDegrees,
                        head_center_height_m =
                            motor.HeadCenterHeight,
                        head_forward_m = motor.HeadForward,
                        head_diameter_m = motor.HeadDiameter,
                        leg_lateral_m = motor.LegLateral,
                        leg_center_height_m =
                            motor.LegCenterHeight,
                        leg_forward_m = motor.LegForward,
                        leg_scale = new[]
                        {
                            motor.LegScale.x,
                            motor.LegScale.y,
                            motor.LegScale.z,
                        },
                        leg_splay_degrees = motor.LegSplayDegrees,
                        leg_forward_lean_degrees =
                            motor.LegForwardLeanDegrees,
                    },
                    arms = new ManifestControlArms
                    {
                        solver = "deterministic-two-bone-ik",
                        control =
                            "shared-policy-target-with-leading-and-trailing-hands",
                        upper_arm_length_m = motor.UpperArmLength,
                        forearm_length_m = motor.ForearmLength,
                        arm_radius_m = motor.ArmRadius,
                        glove_radius_m = motor.GloveRadius,
                        maximum_glove_target_speed_m_s =
                            motor.MaximumGloveTargetSpeed,
                        glove_separation_m = motor.GloveSeparation,
                    },
                },
            };

            return JsonUtility.ToJson(manifest, prettyPrint) +
                Environment.NewLine;
        }

        private static string[] ControlObservationOrder(
            bool includeVisibleBallisticPrediction)
        {
            var v1 = new[]
            {
                "ball_local_x",
                "ball_local_y",
                "ball_local_z",
                "ball_local_vx",
                "ball_local_vy",
                "ball_local_vz",
                "ball_angular_vx",
                "ball_angular_vy",
                "ball_angular_vz",
                "goalkeeper_root_x",
                "goalkeeper_root_y",
                "goalkeeper_root_vx",
                "goalkeeper_root_vy",
                "goalkeeper_body_roll",
                "motor_ready",
                "motor_moving",
                "motor_planting",
                "motor_diving",
                "motor_recovering",
                "motor_state_progress",
                "latched_aim_x",
                "latched_aim_y",
                "reach_aim_x",
                "reach_aim_y",
                "reach_extension",
                "left_glove_x",
                "left_glove_y",
                "right_glove_x",
                "right_glove_y",
                "can_commit",
                "attempt_time",
                "ball_flight_time",
            };

            if (!includeVisibleBallisticPrediction)
            {
                return v1;
            }

            var v2 = new string[v1.Length + 3];
            Array.Copy(v1, v2, v1.Length);
            v2[v1.Length] = "visible_time_to_goal_plane";
            v2[v1.Length + 1] = "visible_predicted_aim_x";
            v2[v1.Length + 2] = "visible_predicted_aim_y";
            return v2;
        }

        private static string CreateGoalkeeperObservationManifestJson(
            string behaviorName,
            string observationSpecId,
            bool partialObservation,
            bool prettyPrint)
        {
            var manifest = new GoalkeeperStateManifest
            {
                schema_version = 1,
                environment_id = KernelConstants.EnvironmentId,
                behavior_name = behaviorName,
                observation_spec_id = observationSpecId,
                reward_spec_id = KernelConstants.GoalkeeperSparseRewardSpecId,
                action_spec_id = KernelConstants.ActionSpecId,
                scenario_suite_id = KernelConstants.ScenarioSuiteId,
                vector_observation_size = KernelConstants.GoalkeeperStateObservationSize,
                discrete_branches = new[] { 9 },
                observation_order = new[]
                {
                    "ball_local_x",
                    "ball_local_y",
                    "ball_local_z",
                    "ball_local_vx",
                    "ball_local_vy",
                    "ball_local_vz",
                    "ball_angular_vx",
                    "ball_angular_vy",
                    "ball_angular_vz",
                    "goalkeeper_local_x",
                    "goalkeeper_lateral_velocity",
                    "motor_ready",
                    "motor_shuffling",
                    "motor_diving",
                    "motor_recovering",
                    "dive_left",
                    "dive_right",
                    "dive_low",
                    "dive_middle",
                    "dive_high",
                    "attempt_time",
                    "ball_flight_time",
                    "reserved_0",
                    "reserved_1",
                },
                excluded_privileged_fields = new[]
                {
                    "requested_target",
                    "future_goal_plane_intersection",
                    "launch_velocity",
                    "sampled_flight_time_parameter",
                    "terminal_outcome",
                },
                partial_observation = partialObservation
                    ? new ManifestPartialObservation
                    {
                        source = "visible_state_snapshot",
                        delay_application = "before_noise",
                        noise_application = "after_delay",
                        clamp_range = new[] { -1f, 1f },
                        controlled_by_environment_parameters = new[]
                        {
                            "stage4.obs_delay_steps",
                            "stage4.ball_position_noise_m",
                            "stage4.ball_velocity_noise_mps",
                            "stage4.keeper_position_noise_m",
                            "stage4.dropout_probability",
                        },
                    }
                    : null,
            };
            return JsonUtility.ToJson(manifest, prettyPrint) + Environment.NewLine;
        }

        public static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(hash.Length * 2);
                for (var index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static float[] Vector(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }
    }
}
