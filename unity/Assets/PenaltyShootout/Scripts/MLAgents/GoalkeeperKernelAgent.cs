using PenaltyShootout.Kernel;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    /// <summary>
    /// Stage 1 exposes the stable nine-action transport plus one constant
    /// transport-health value. Semantic observations and rewards are
    /// deliberately introduced as a versioned Stage 2 contract.
    /// </summary>
    public sealed class GoalkeeperKernelAgent : Agent, IGoalkeeperActionSource
    {
        [SerializeField]
        private PenaltyAreaController controller;

        [SerializeField]
        private GoalkeeperObservationProfile observationProfile =
            GoalkeeperObservationProfile.TransportProbe;

        private GoalkeeperAction pendingAction = GoalkeeperAction.Hold;
        private GoalkeeperActionMask currentMask = GoalkeeperActionMask.HoldOnly;
        private GoalkeeperAction? bufferedDiveAction;
        private bool hasPendingAction;
        private int stage2Lesson;
        private GoalkeeperPartialObservationSettings stage4ObservationSettings =
            GoalkeeperPartialObservationSettings.None;
        private readonly GoalkeeperObservationDelayBuffer partialObservationBuffer =
            new GoalkeeperObservationDelayBuffer();
        private int partialObservationIndex;

        public PenaltyAreaController Controller
        {
            get => controller;
            set => controller = value;
        }

        public GoalkeeperObservationProfile ObservationProfile
        {
            get => observationProfile;
            set => observationProfile = value;
        }

        private void Awake()
        {
            GoalkeeperBenchmarkTelemetry.InitializeIfEnabled();
            if (controller == null)
            {
                controller = GetComponentInParent<PenaltyAreaController>();
            }
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Q))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftLow;
            }
            else if (Input.GetKeyDown(KeyCode.W))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftHigh;
            }
            else if (Input.GetKeyDown(KeyCode.U))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightLow;
            }
            else if (Input.GetKeyDown(KeyCode.I))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.O))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightHigh;
            }
#endif
        }

        public override void OnEpisodeBegin()
        {
            ApplyCurriculumParameters();
            pendingAction = GoalkeeperAction.Hold;
            currentMask = GoalkeeperActionMask.HoldOnly;
            bufferedDiveAction = null;
            hasPendingAction = false;
            partialObservationBuffer.Reset();
            partialObservationIndex = 0;
            // An initial decision is required so Python reset() receives the
            // behavior specification before the physical shot begins.
            RequestDecision();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (observationProfile == GoalkeeperObservationProfile.StateV0)
            {
                GoalkeeperTrainingContracts.WriteStateV0(
                    controller,
                    sensor.AddObservation);
                return;
            }

            if (observationProfile == GoalkeeperObservationProfile.StatePartialV0)
            {
                var snapshot = GoalkeeperTrainingContracts.CaptureVisibleState(controller);
                var delayed = partialObservationBuffer.PushAndRead(
                    snapshot,
                    stage4ObservationSettings.DelaySteps);
                var settings = stage4ObservationSettings;
                settings.Seed = controller == null
                    ? 0UL
                    : Pcg32.DeriveSeed(
                        controller.MasterSeed,
                        controller.ArenaId,
                        controller.AttemptId);
                settings.ObservationIndex = partialObservationIndex;
                partialObservationIndex++;
                GoalkeeperTrainingContracts.WriteStatePartialV0(
                    delayed,
                    settings,
                    sensor.AddObservation);
                return;
            }

            // ML-Agents requires a sensor value to emit a decision step. This
            // constant carries no environment state and preserves the Stage 1
            // transport probe contract.
            sensor.AddObservation(0.0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var discrete = actions.DiscreteActions;
            if (discrete.Length == 0)
            {
                pendingAction = GoalkeeperAction.Hold;
                hasPendingAction = true;
                return;
            }

            var requested = (GoalkeeperAction)discrete[0];
            pendingAction = currentMask.IsAllowed(requested)
                ? requested
                : GoalkeeperAction.Hold;
            hasPendingAction = true;
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            for (var action = 0; action <= (int)GoalkeeperAction.DiveRightHigh; action++)
            {
                if (!currentMask.IsAllowed((GoalkeeperAction)action))
                {
                    actionMask.SetActionEnabled(0, action, false);
                }
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var action = GoalkeeperAction.Hold;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (bufferedDiveAction.HasValue &&
                currentMask.IsAllowed(bufferedDiveAction.Value))
            {
                action = bufferedDiveAction.Value;
                bufferedDiveAction = null;
            }
            else if (Input.GetKey(KeyCode.A))
            {
                action = GoalkeeperAction.ShuffleLeft;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                action = GoalkeeperAction.ShuffleRight;
            }
#endif
            var discreteActions = actionsOut.DiscreteActions;
            discreteActions[0] = (int)action;
        }

        public GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            currentMask = actionMask;
            var result = hasPendingAction && currentMask.IsAllowed(pendingAction)
                ? pendingAction
                : GoalkeeperAction.Hold;
            hasPendingAction = false;
            RequestDecision();
            return result;
        }

        public void OnAttemptStarted(long attemptId)
        {
            pendingAction = GoalkeeperAction.Hold;
            currentMask = GoalkeeperActionMask.HoldOnly;
            bufferedDiveAction = null;
            hasPendingAction = false;
            partialObservationBuffer.Reset();
            partialObservationIndex = 0;
        }

        public void OnAttemptEnded(AttemptResult result)
        {
            var reward = GoalkeeperTrainingContracts.SparseReward(result.Outcome);
            if (IsTrainableObservationProfile())
            {
                SetReward(reward);
                RecordStage2Stats(result, reward);
                if (observationProfile == GoalkeeperObservationProfile.StatePartialV0)
                {
                    RecordStage4Stats(result, reward);
                }
            }

            GoalkeeperBenchmarkTelemetry.Emit(
                result,
                reward,
                GoalkeeperTrainingContracts.ObservationSpecIdForProfile(observationProfile),
                stage4ObservationSettings);
            EndEpisode();
        }

        private void ApplyCurriculumParameters()
        {
            if (!IsTrainableObservationProfile() ||
                controller == null ||
                controller.ShotConfiguration == null)
            {
                return;
            }

            var parameters = Academy.Instance.EnvironmentParameters;
            controller.GoalkeeperGloveHandling?.SetHandlingEnabled(
                parameters.GetWithDefault(
                    "stage6.glove_handling_v1",
                    controller.GoalkeeperGloveHandling.HandlingEnabled ? 1f : 0f) >=
                0.5f);
            stage2Lesson = Mathf.RoundToInt(parameters.GetWithDefault("stage2.lesson", 3f));
            var shots = controller.ShotConfiguration;
            ApplyLessonDefaults(shots, stage2Lesson);
            shots.MinimumTargetXNormalized =
                parameters.GetWithDefault("stage2.target_x_min", shots.MinimumTargetXNormalized);
            shots.MaximumTargetXNormalized =
                parameters.GetWithDefault("stage2.target_x_max", shots.MaximumTargetXNormalized);
            shots.MinimumTargetYNormalized =
                parameters.GetWithDefault("stage2.target_y_min", shots.MinimumTargetYNormalized);
            shots.MaximumTargetYNormalized =
                parameters.GetWithDefault("stage2.target_y_max", shots.MaximumTargetYNormalized);
            shots.MinimumFlightTime =
                parameters.GetWithDefault("stage2.flight_time_min", shots.MinimumFlightTime);
            shots.MaximumFlightTime =
                parameters.GetWithDefault("stage2.flight_time_max", shots.MaximumFlightTime);
            shots.MinimumLaunchDelay =
                parameters.GetWithDefault("stage2.launch_delay_min", shots.MinimumLaunchDelay);
            shots.MaximumLaunchDelay =
                parameters.GetWithDefault("stage2.launch_delay_max", shots.MaximumLaunchDelay);

            if (observationProfile == GoalkeeperObservationProfile.StatePartialV0)
            {
                stage4ObservationSettings = new GoalkeeperPartialObservationSettings
                {
                    DelaySteps = Mathf.Clamp(
                        Mathf.RoundToInt(parameters.GetWithDefault("stage4.obs_delay_steps", 0f)),
                        0,
                        64),
                    BallPositionNoiseMeters = Mathf.Max(
                        0f,
                        parameters.GetWithDefault("stage4.ball_position_noise_m", 0f)),
                    BallVelocityNoiseMetersPerSecond = Mathf.Max(
                        0f,
                        parameters.GetWithDefault("stage4.ball_velocity_noise_mps", 0f)),
                    GoalkeeperPositionNoiseMeters = Mathf.Max(
                        0f,
                        parameters.GetWithDefault("stage4.keeper_position_noise_m", 0f)),
                    DropoutProbability = Mathf.Clamp01(
                        parameters.GetWithDefault("stage4.dropout_probability", 0f)),
                };
            }
            else
            {
                stage4ObservationSettings = GoalkeeperPartialObservationSettings.None;
            }
        }

        private static void ApplyLessonDefaults(
            ShotDistributionConfig shots,
            int lesson)
        {
            switch (Mathf.Clamp(lesson, 0, 3))
            {
                case 0:
                    shots.MinimumTargetXNormalized = -0.35f;
                    shots.MaximumTargetXNormalized = 0.35f;
                    shots.MinimumTargetYNormalized = 0.35f;
                    shots.MaximumTargetYNormalized = 0.55f;
                    shots.MinimumFlightTime = 0.75f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.35f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
                case 1:
                    shots.MinimumTargetXNormalized = -1f;
                    shots.MaximumTargetXNormalized = 1f;
                    shots.MinimumTargetYNormalized = 0.10f;
                    shots.MaximumTargetYNormalized = 0.65f;
                    shots.MinimumFlightTime = 0.68f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.25f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
                case 2:
                    shots.MinimumTargetXNormalized = -1f;
                    shots.MaximumTargetXNormalized = 1f;
                    shots.MinimumTargetYNormalized = 0f;
                    shots.MaximumTargetYNormalized = 1f;
                    shots.MinimumFlightTime = 0.58f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.20f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
                default:
                    shots.MinimumTargetXNormalized = -1f;
                    shots.MaximumTargetXNormalized = 1f;
                    shots.MinimumTargetYNormalized = 0f;
                    shots.MaximumTargetYNormalized = 1f;
                    shots.MinimumFlightTime = 0.38f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.15f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
            }
        }

        private void RecordStage2Stats(AttemptResult result, float reward)
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Stage2/SaveRate",
                result.Outcome == AttemptOutcome.Saved ||
                result.Outcome == AttemptOutcome.BlockedThenOut ? 1f : 0f);
            stats.Add("Stage2/GoalRate", result.Outcome == AttemptOutcome.Goal ? 1f : 0f);
            stats.Add("Stage2/InvalidRate", result.Outcome == AttemptOutcome.Invalid ? 1f : 0f);
            stats.Add("Stage2/GloveContactRate", result.GloveContact ? 1f : 0f);
            stats.Add("Stage2/KeeperContactRate", result.GoalkeeperContact ? 1f : 0f);
            stats.Add("Stage2/ActionMaskViolations", result.ActionMaskViolations);
            stats.Add("Stage2/EpisodeLengthSeconds", result.AttemptTime);
            stats.Add("Stage2/SparseReward", reward);
            stats.Add("Stage2/Lesson", stage2Lesson);
        }

        private void RecordStage4Stats(AttemptResult result, float reward)
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Stage4/SaveRate",
                result.Outcome == AttemptOutcome.Saved ||
                result.Outcome == AttemptOutcome.BlockedThenOut ? 1f : 0f);
            stats.Add("Stage4/GoalRate", result.Outcome == AttemptOutcome.Goal ? 1f : 0f);
            stats.Add("Stage4/InvalidRate", result.Outcome == AttemptOutcome.Invalid ? 1f : 0f);
            stats.Add("Stage4/ActionMaskViolations", result.ActionMaskViolations);
            stats.Add("Stage4/SparseReward", reward);
            stats.Add("Stage4/ObservationDelaySteps", stage4ObservationSettings.DelaySteps);
            stats.Add(
                "Stage4/BallPositionNoiseMeters",
                stage4ObservationSettings.BallPositionNoiseMeters);
            stats.Add(
                "Stage4/BallVelocityNoiseMetersPerSecond",
                stage4ObservationSettings.BallVelocityNoiseMetersPerSecond);
            stats.Add(
                "Stage4/GoalkeeperPositionNoiseMeters",
                stage4ObservationSettings.GoalkeeperPositionNoiseMeters);
            stats.Add(
                "Stage4/DropoutProbability",
                stage4ObservationSettings.DropoutProbability);
        }

        private bool IsTrainableObservationProfile()
        {
            return observationProfile == GoalkeeperObservationProfile.StateV0 ||
                observationProfile == GoalkeeperObservationProfile.StatePartialV0;
        }
    }
}
