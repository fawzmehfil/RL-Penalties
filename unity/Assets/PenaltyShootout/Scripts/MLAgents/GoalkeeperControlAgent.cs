using PenaltyShootout.Kernel;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    public sealed class GoalkeeperControlAgent :
        Agent,
        IGoalkeeperControlSourceV1
    {
        [SerializeField]
        private PenaltyAreaController controller;

        private GoalkeeperControlCommand pendingCommand =
            GoalkeeperControlCommand.Neutral;
        private GoalkeeperControlActionMask currentMask =
            new GoalkeeperControlActionMask(false);
        private bool hasPendingCommand;
        private bool bufferedCommit;
        private float heuristicAimX;
        private float heuristicAimY;
        private int stage5Lesson;
        private bool reachTrainingEnabled;
        private int reachTrainingVersion;
        private bool attemptAutoCommitApplied;
        private bool attemptReachFloorApplied;

        public PenaltyAreaController Controller
        {
            get => controller;
            set => controller = value;
        }

        private new void Awake()
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
            var aimSpeed = 0.9f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                heuristicAimX -= aimSpeed;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                heuristicAimX += aimSpeed;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                heuristicAimY -= aimSpeed;
            }

            if (Input.GetKey(KeyCode.UpArrow))
            {
                heuristicAimY += aimSpeed;
            }

            heuristicAimX = Mathf.Clamp(heuristicAimX, -1f, 1f);
            heuristicAimY = Mathf.Clamp(heuristicAimY, -1f, 1f);
            if (Input.GetKeyDown(KeyCode.Space))
            {
                bufferedCommit = true;
            }
#endif
        }

        public override void OnEpisodeBegin()
        {
            ApplyCurriculumParameters();
            pendingCommand = GoalkeeperControlCommand.Neutral;
            currentMask = new GoalkeeperControlActionMask(false);
            hasPendingCommand = false;
            bufferedCommit = false;
            heuristicAimX = 0f;
            heuristicAimY = 0f;
            attemptAutoCommitApplied = false;
            attemptReachFloorApplied = false;
            RequestDecision();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            GoalkeeperTrainingContracts.WriteControlStateV1(
                controller,
                sensor.AddObservation);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var command = GoalkeeperControlCommand.Neutral;
            var continuous = actions.ContinuousActions;
            if (continuous.Length >= GoalkeeperControlSpace.ContinuousActionCount)
            {
                command.MoveX = continuous[0];
                command.AimX = continuous[1];
                command.AimY = continuous[2];
                command.Reach = continuous[3];
            }

            var discrete = actions.DiscreteActions;
            command.Commit =
                discrete.Length > 0 &&
                discrete[0] == 1 &&
                currentMask.CanCommit;
            pendingCommand = command.Sanitized(out _);
            hasPendingCommand = true;
        }

        public override void WriteDiscreteActionMask(
            IDiscreteActionMask actionMask)
        {
            if (!currentMask.CanCommit)
            {
                actionMask.SetActionEnabled(0, 1, false);
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuous = actionsOut.ContinuousActions;
            var move = 0f;
            var reach = -1f;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.A))
            {
                move = -1f;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                move = 1f;
            }

            if (Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift) ||
                Input.GetMouseButton(0))
            {
                reach = 1f;
            }
#endif
            continuous[0] = move;
            continuous[1] = heuristicAimX;
            continuous[2] = heuristicAimY;
            continuous[3] = reach;
            var discrete = actionsOut.DiscreteActions;
            discrete[0] = bufferedCommit && currentMask.CanCommit ? 1 : 0;
            if (discrete[0] == 1)
            {
                bufferedCommit = false;
            }
        }

        public GoalkeeperControlCommand DecideControl(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask)
        {
            currentMask =
                GoalkeeperControlTrainingContracts.ApplyCommitGuard(
                    actionMask,
                    context,
                    reachTrainingEnabled,
                    reachTrainingVersion,
                    stage5Lesson);
            var result = hasPendingCommand
                ? pendingCommand
                : GoalkeeperControlCommand.Neutral;
            if (!currentMask.CanCommit)
            {
                result.Commit = false;
            }

            result = GoalkeeperControlTrainingContracts.ApplyScaffold(
                result,
                context,
                currentMask,
                reachTrainingEnabled,
                reachTrainingVersion,
                stage5Lesson,
                out var autoCommitApplied,
                out var reachFloorApplied);
            attemptAutoCommitApplied |= autoCommitApplied;
            attemptReachFloorApplied |= reachFloorApplied;
            hasPendingCommand = false;
            RequestDecision();
            return result;
        }

        public void OnAttemptStarted(long attemptId)
        {
            pendingCommand = GoalkeeperControlCommand.Neutral;
            currentMask = new GoalkeeperControlActionMask(false);
            hasPendingCommand = false;
            bufferedCommit = false;
            attemptAutoCommitApplied = false;
            attemptReachFloorApplied = false;
        }

        public void OnAttemptEnded(AttemptResult result)
        {
            var sparseReward =
                GoalkeeperTrainingContracts.SparseReward(result.Outcome);
            var trainingReward =
                GoalkeeperControlTrainingContracts.TrainingReward(
                    result,
                    reachTrainingEnabled,
                    reachTrainingVersion);
            SetReward(trainingReward);
            RecordStats(result, sparseReward, trainingReward);
            GoalkeeperBenchmarkTelemetry.Emit(
                result,
                sparseReward,
                KernelConstants.GoalkeeperControlObservationSpecId);
            EndEpisode();
        }

        private void ApplyCurriculumParameters()
        {
            if (controller == null || controller.ShotConfiguration == null)
            {
                return;
            }

            var parameters = Academy.Instance.EnvironmentParameters;
            stage5Lesson = Mathf.Clamp(
                Mathf.RoundToInt(
                    parameters.GetWithDefault("stage5.lesson", 4f)),
                0,
                4);
            reachTrainingEnabled =
                parameters.GetWithDefault(
                    "stage5.reach_training_enabled",
                    0f) >= 0.5f;
            reachTrainingVersion = reachTrainingEnabled
                ? Mathf.Clamp(
                    Mathf.RoundToInt(
                        parameters.GetWithDefault(
                            "stage5.reach_training_version",
                            1f)),
                    1,
                    2)
                : 0;
            var shots = controller.ShotConfiguration;
            ApplyLessonDefaults(shots, stage5Lesson);
            GoalkeeperControlTrainingContracts.ApplyReachFocusLesson(
                shots,
                reachTrainingEnabled,
                reachTrainingVersion,
                stage5Lesson);
            shots.MinimumTargetXNormalized =
                parameters.GetWithDefault(
                    "stage5.target_x_min",
                    shots.MinimumTargetXNormalized);
            shots.MaximumTargetXNormalized =
                parameters.GetWithDefault(
                    "stage5.target_x_max",
                    shots.MaximumTargetXNormalized);
            shots.MinimumTargetYNormalized =
                parameters.GetWithDefault(
                    "stage5.target_y_min",
                    shots.MinimumTargetYNormalized);
            shots.MaximumTargetYNormalized =
                parameters.GetWithDefault(
                    "stage5.target_y_max",
                    shots.MaximumTargetYNormalized);
            shots.MinimumFlightTime =
                parameters.GetWithDefault(
                    "stage5.flight_time_min",
                    shots.MinimumFlightTime);
            shots.MaximumFlightTime =
                parameters.GetWithDefault(
                    "stage5.flight_time_max",
                    shots.MaximumFlightTime);
            shots.MinimumLaunchDelay =
                parameters.GetWithDefault(
                    "stage5.launch_delay_min",
                    shots.MinimumLaunchDelay);
            shots.MaximumLaunchDelay =
                parameters.GetWithDefault(
                    "stage5.launch_delay_max",
                    shots.MaximumLaunchDelay);
        }

        private static void ApplyLessonDefaults(
            ShotDistributionConfig shots,
            int lesson)
        {
            switch (lesson)
            {
                case 0:
                    shots.MinimumTargetXNormalized = -0.20f;
                    shots.MaximumTargetXNormalized = 0.20f;
                    shots.MinimumTargetYNormalized = 0.30f;
                    shots.MaximumTargetYNormalized = 0.65f;
                    shots.MinimumFlightTime = 0.78f;
                    shots.MaximumFlightTime = 0.88f;
                    shots.MinimumLaunchDelay = 0.35f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
                case 1:
                    shots.MinimumTargetXNormalized = -0.70f;
                    shots.MaximumTargetXNormalized = 0.70f;
                    shots.MinimumTargetYNormalized = 0.30f;
                    shots.MaximumTargetYNormalized = 0.65f;
                    shots.MinimumFlightTime = 0.72f;
                    shots.MaximumFlightTime = 0.88f;
                    shots.MinimumLaunchDelay = 0.28f;
                    shots.MaximumLaunchDelay = 0.45f;
                    break;
                case 2:
                    shots.MinimumTargetXNormalized = -0.85f;
                    shots.MaximumTargetXNormalized = 0.85f;
                    shots.MinimumTargetYNormalized = 0.05f;
                    shots.MaximumTargetYNormalized = 0.95f;
                    shots.MinimumFlightTime = 0.62f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.22f;
                    shots.MaximumLaunchDelay = 0.42f;
                    break;
                case 3:
                    shots.MinimumTargetXNormalized = -1f;
                    shots.MaximumTargetXNormalized = 1f;
                    shots.MinimumTargetYNormalized = 0f;
                    shots.MaximumTargetYNormalized = 1f;
                    shots.MinimumFlightTime = 0.48f;
                    shots.MaximumFlightTime = 0.85f;
                    shots.MinimumLaunchDelay = 0.18f;
                    shots.MaximumLaunchDelay = 0.42f;
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

        private void RecordStats(
            AttemptResult result,
            float sparseReward,
            float trainingReward)
        {
            var stats = Academy.Instance.StatsRecorder;
            var isSave =
                GoalkeeperControlTrainingContracts.IsSave(result.Outcome);
            stats.Add(
                "Stage5/SaveRate",
                isSave ? 1f : 0f);
            stats.Add(
                "Stage5/GoalRate",
                result.Outcome == AttemptOutcome.Goal ? 1f : 0f);
            stats.Add(
                "Stage5/InvalidRate",
                result.Outcome == AttemptOutcome.Invalid ? 1f : 0f);
            stats.Add(
                "Stage5/GloveContactRate",
                result.GloveContact ? 1f : 0f);
            stats.Add(
                "Stage5/GloveSaveRate",
                isSave && result.GloveContact ? 1f : 0f);
            stats.Add(
                "Stage5/BodySaveRate",
                isSave && !result.GloveContact ? 1f : 0f);
            stats.Add(
                "Stage5/ContactThenGoalRate",
                result.GoalkeeperContact &&
                result.Outcome == AttemptOutcome.Goal
                    ? 1f
                    : 0f);
            stats.Add(
                "Stage5/CommitRate",
                result.HasSaveCommitment ? 1f : 0f);
            if (result.HasSaveCommitment)
            {
                stats.Add(
                    "Stage5/FirstCommitBallFlightTime",
                    result.FirstCommitBallFlightTime);
                var committedTarget =
                    GoalkeeperControlSpace.AimToLocal(
                        result.FirstCommitAim.x,
                        result.FirstCommitAim.y);
                stats.Add(
                    "Stage5/FirstCommitAimError",
                    Vector2.Distance(
                        committedTarget,
                        new Vector2(
                            result.RequestedTargetLocal.x,
                            result.RequestedTargetLocal.y)));
                stats.Add(
                    "Stage5/CommitWithoutContactRate",
                    result.GoalkeeperContact ? 0f : 1f);
            }

            stats.Add(
                "Stage5/PeakReachExtension",
                result.GoalkeeperPeakReachExtension);
            stats.Add(
                "Stage5/RootDistance",
                result.GoalkeeperRootDistance);
            stats.Add(
                "Stage5/ActionMaskViolations",
                result.ActionMaskViolations);
            var targetAim = GoalkeeperControlSpace.LocalToAim(
                new Vector2(
                    result.RequestedTargetLocal.x,
                    result.RequestedTargetLocal.y));
            var targetY01 = Mathf.Clamp01((targetAim.y + 1f) * 0.5f);
            if (targetY01 >= 0.66f)
            {
                stats.Add("Stage5/HighShotSaveRate", isSave ? 1f : 0f);
            }

            if (Mathf.Abs(targetAim.x) >= 0.60f)
            {
                stats.Add("Stage5/EdgeShotSaveRate", isSave ? 1f : 0f);
            }

            stats.Add(
                "Stage5/ReachFocusRate",
                result.ReachFocusSample ? 1f : 0f);
            stats.Add(
                "Stage5/ReachScaffoldRate",
                attemptReachFloorApplied ? 1f : 0f);
            stats.Add(
                "Stage5/AutoCommitRate",
                attemptAutoCommitApplied ? 1f : 0f);
            stats.Add(
                "Stage5/ReachTrainingEnabled",
                reachTrainingEnabled ? 1f : 0f);
            stats.Add(
                "Stage5/ReachTrainingVersion",
                reachTrainingVersion);
            stats.Add(
                "Stage5/FirstContactGloveRate",
                result.FirstGoalkeeperContactPart ==
                    GoalkeeperContactPart.LeftGlove ||
                result.FirstGoalkeeperContactPart ==
                    GoalkeeperContactPart.RightGlove
                    ? 1f
                    : 0f);
            stats.Add(
                "Stage5/TargetClampAttemptRate",
                result.ControlTargetClampCount > 0 ? 1f : 0f);
            if (result.HasSaveCommitment)
            {
                stats.Add(
                    "Stage5/FirstCommitVisibleTimeToGoalPlane",
                    result.FirstCommitVisibleTimeToGoalPlane);
                stats.Add(
                    "Stage5/FirstCommitReachDemand",
                    result.FirstCommitReachDemand);
                stats.Add(
                    "Stage5/FirstCommitReachExtension",
                    result.FirstCommitReachExtension);
                stats.Add(
                    "Stage5/ImmediateCommitRate",
                    result.FirstCommitWasImmediate ? 1f : 0f);
            }
            stats.Add("Stage5/SparseReward", sparseReward);
            stats.Add("Stage5/TrainingReward", trainingReward);
            stats.Add("Stage5/Lesson", stage5Lesson);
        }
    }
}
