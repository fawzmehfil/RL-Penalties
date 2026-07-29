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
            currentMask = actionMask;
            var result = hasPendingCommand
                ? pendingCommand
                : GoalkeeperControlCommand.Neutral;
            if (!currentMask.CanCommit)
            {
                result.Commit = false;
            }

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
        }

        public void OnAttemptEnded(AttemptResult result)
        {
            var reward = GoalkeeperTrainingContracts.SparseReward(result.Outcome);
            SetReward(reward);
            RecordStats(result, reward);
            GoalkeeperBenchmarkTelemetry.Emit(
                result,
                reward,
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
            var shots = controller.ShotConfiguration;
            ApplyLessonDefaults(shots, stage5Lesson);
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

        private void RecordStats(AttemptResult result, float reward)
        {
            var stats = Academy.Instance.StatsRecorder;
            stats.Add(
                "Stage5/SaveRate",
                result.Outcome == AttemptOutcome.Saved ||
                result.Outcome == AttemptOutcome.BlockedThenOut
                    ? 1f
                    : 0f);
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
                "Stage5/CommitRate",
                result.HasSaveCommitment ? 1f : 0f);
            stats.Add(
                "Stage5/FirstCommitBallFlightTime",
                result.FirstCommitBallFlightTime);
            stats.Add(
                "Stage5/PeakReachExtension",
                result.GoalkeeperPeakReachExtension);
            stats.Add(
                "Stage5/RootDistance",
                result.GoalkeeperRootDistance);
            stats.Add(
                "Stage5/ActionMaskViolations",
                result.ActionMaskViolations);
            stats.Add("Stage5/SparseReward", reward);
            stats.Add("Stage5/Lesson", stage5Lesson);
        }
    }
}
