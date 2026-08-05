using System;
using PenaltyShootout.Kernel;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    public sealed class GoalkeeperControlAgent :
        Agent,
        IGoalkeeperControlSourceV1,
        IGoalkeeperDeferredControlSourceV2,
        IGoalkeeperNativeInferenceControlV1
    {
        [SerializeField]
        private PenaltyAreaController controller;

        [SerializeField]
        private GoalkeeperControlHeuristicMode heuristicMode;

        [SerializeField]
        [Range(0.1f, 1f)]
        private float reactiveTeacherCommitHorizon =
            GoalkeeperReactiveControlPolicyV1.DefaultCommitHorizon;

        [SerializeField]
        private GoalkeeperSplitInferencePolicyV1 nativeSplitPolicy;

        [SerializeField]
        private bool nativeSplitInferenceByDefault;

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
        private int controlMetricsVersion = 3;
        private bool attemptAutoCommitApplied;
        private bool attemptReachFloorApplied;
        private bool attemptAimGuidanceApplied;
        private int firstEligibleCommitDecisionIndex = -1;
        private float firstEligibleCommitBallFlightTime = -1f;
        private float firstEligibleCommitVisibleTimeToGoalPlane = -1f;
        private int eligibleCommitDecisionsBeforeCommit;
        private bool hasRecordedCommitMetadata;
        private bool firstCommitWasPremature;
        private bool firstCommitWasLate;
        private bool firstCommitWasTimely;
        private Vector2 firstCommitRawPolicyAim;
        private bool hasFirstCommitVisiblePrediction;
        private Vector2 firstCommitVisiblePredictedAim;
        private float firstCommitVisibleAimError = -1f;
        private float firstCommitDesiredReach;
        private float firstCommitReachShortfall;
        private float attemptDecisionShapingReward;
        private int attemptPolicyActionOverrideCount;
        private bool policyDecisionOutstanding;
        private GoalkeeperControlDecisionContext requestedDecisionContext;
        private int policyDecisionRequestCount;
        private int policyDecisionConsumedCount;
        private int policyDecisionDiscardedCount;
        private int policyDecisionDuplicateRequestCount;
        private int policyDecisionMissingActionCount;
        private bool nativeSplitInferenceEnabled;
        private int nativeInferenceEvaluationCount;
        private float nativeInferenceMaximumActionError;
        private int nativeInferenceCommitMismatchCount;
        private readonly float[] nativeObservations =
            new float[KernelConstants.GoalkeeperControlV2ObservationSize];
        private bool hasCachedDecisionObservations;

        public PenaltyAreaController Controller
        {
            get => controller;
            set => controller = value;
        }

        public GoalkeeperControlHeuristicMode HeuristicMode
        {
            get => heuristicMode;
            set => heuristicMode = value;
        }

        public GoalkeeperSplitInferencePolicyV1 NativeSplitPolicy
        {
            get => nativeSplitPolicy;
            set => nativeSplitPolicy = value;
        }

        public bool NativeSplitInferenceEnabled =>
            nativeSplitInferenceEnabled;

        public bool NativeInferenceEnabled => nativeSplitInferenceEnabled;

        public bool NativeSplitInferenceByDefault
        {
            get => nativeSplitInferenceByDefault;
            set => nativeSplitInferenceByDefault = value;
        }

        public bool TrySetNativeInference(bool enabled, out string error)
        {
            if (enabled && nativeSplitPolicy == null)
            {
                error = "Native split inference requires a packaged policy.";
                return false;
            }

            nativeSplitInferenceByDefault = enabled;
            nativeSplitInferenceEnabled = enabled;
            nativeSplitPolicy?.ResetAttempt();
            error = string.Empty;
            return true;
        }

        public bool UsesDeferredDecisionScheduling
        {
            get
            {
                var behavior = GetComponent<BehaviorParameters>();
                return behavior != null &&
                    behavior.BehaviorName ==
                    KernelConstants.GoalkeeperControlV2BehaviorName;
            }
        }

        private new void Awake()
        {
            GoalkeeperBenchmarkTelemetry.InitializeIfEnabled();
            if (controller == null)
            {
                controller = GetComponentInParent<PenaltyAreaController>();
            }

            if (nativeSplitPolicy == null)
            {
                nativeSplitPolicy =
                    GetComponent<GoalkeeperSplitInferencePolicyV1>();
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
            nativeSplitPolicy?.ResetAttempt();
            hasCachedDecisionObservations = false;
            ResetAttemptTrainingTelemetry();
            if (!UsesDeferredDecisionScheduling)
            {
                RequestDecision();
            }
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (UsesDeferredDecisionScheduling)
            {
                var index = 0;
                WriteConfiguredControlObservations(
                    value =>
                    {
                        sensor.AddObservation(value);
                        if (index < nativeObservations.Length)
                        {
                            nativeObservations[index] = value;
                        }
                        index++;
                    });
                hasCachedDecisionObservations =
                    index == nativeObservations.Length;
            }
            else
            {
                GoalkeeperTrainingContracts.WriteControlStateV1(
                    controller,
                    sensor.AddObservation);
            }
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var transportCommand = ReadTransportCommand(actions);
            var command = transportCommand;
            if (nativeSplitInferenceEnabled)
            {
                if (nativeSplitPolicy == null ||
                    !TryCollectNativeObservations() ||
                    !nativeSplitPolicy.TryEvaluate(
                        nativeObservations,
                        currentMask,
                        out command,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Stage 5.6B native split inference failed closed.");
                }

                nativeInferenceEvaluationCount++;
                nativeInferenceMaximumActionError = Mathf.Max(
                    nativeInferenceMaximumActionError,
                    MaximumContinuousError(command, transportCommand));
                if (command.Commit != transportCommand.Commit)
                {
                    nativeInferenceCommitMismatchCount++;
                }
            }
            pendingCommand = command.Sanitized(out _);
            hasPendingCommand = true;
        }

        public void RequestControlDecision(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask)
        {
            if (!UsesDeferredDecisionScheduling)
            {
                return;
            }

            if (policyDecisionOutstanding)
            {
                policyDecisionDuplicateRequestCount++;
                return;
            }

            currentMask =
                GoalkeeperControlTrainingContracts.ApplyCommitGuard(
                    actionMask,
                    context,
                    reachTrainingEnabled,
                    reachTrainingVersion,
                    stage5Lesson);
            requestedDecisionContext = context;
            pendingCommand = GoalkeeperControlCommand.Neutral;
            hasPendingCommand = false;
            hasCachedDecisionObservations = false;
            policyDecisionOutstanding = true;
            policyDecisionRequestCount++;
            RequestDecision();
        }

        public GoalkeeperControlCommand ConsumeControlDecision(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask)
        {
            if (!UsesDeferredDecisionScheduling)
            {
                return DecideControl(context, actionMask);
            }

            policyDecisionConsumedCount++;
            if (!policyDecisionOutstanding || !hasPendingCommand)
            {
                policyDecisionMissingActionCount++;
            }

            policyDecisionOutstanding = false;
            var decisionContext = requestedDecisionContext;
            return DecideControl(decisionContext, actionMask);
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
            if (heuristicMode ==
                GoalkeeperControlHeuristicMode.ReactiveTeacher)
            {
                var gravity =
                    controller == null || controller.ArenaOrigin == null
                        ? Physics.gravity
                        : controller.ArenaOrigin.InverseTransformDirection(
                            Physics.gravity);
                var command = controller == null
                    ? GoalkeeperControlCommand.Neutral
                    : GoalkeeperReactiveControlPolicyV1.Decide(
                        controller.BallLocalPosition,
                        controller.BallLocalVelocity,
                        gravity,
                        controller.GoalkeeperControlLocalPosition.x,
                        currentMask,
                        reactiveTeacherCommitHorizon);
                continuous[0] = command.MoveX;
                continuous[1] = command.AimX;
                continuous[2] = command.AimY;
                continuous[3] = command.Reach;
                var teacherDiscrete = actionsOut.DiscreteActions;
                teacherDiscrete[0] = command.Commit ? 1 : 0;
                return;
            }

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
            var rawPolicyAim = new Vector2(result.AimX, result.AimY);
            if (!currentMask.CanCommit)
            {
                result.Commit = false;
            }

            var usesPolicyFaithfulMetrics =
                reachTrainingVersion >= 4 ||
                controlMetricsVersion >= 4;
            var preferredOpportunity =
                currentMask.CanCommit &&
                (usesPolicyFaithfulMetrics
                    ? GoalkeeperControlTrainingContracts
                        .IsV4TimelyCommitOpportunity(context)
                    : GoalkeeperControlTrainingContracts
                        .IsV3PreferredCommitOpportunity(context));
            if (preferredOpportunity &&
                firstEligibleCommitDecisionIndex < 0)
            {
                firstEligibleCommitDecisionIndex = context.DecisionIndex;
                firstEligibleCommitBallFlightTime = context.BallFlightTime;
                firstEligibleCommitVisibleTimeToGoalPlane =
                    context.VisibleTimeToGoalPlane;
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
            var aimGuidanceApplied =
                !Mathf.Approximately(result.AimX, rawPolicyAim.x) ||
                !Mathf.Approximately(result.AimY, rawPolicyAim.y);
            attemptAimGuidanceApplied |= aimGuidanceApplied;
            attemptPolicyActionOverrideCount +=
                (autoCommitApplied ? 1 : 0) +
                (reachFloorApplied ? 1 : 0) +
                (aimGuidanceApplied ? 1 : 0);
            var acceptedCommit = result.Commit && currentMask.CanCommit;
            var usesPolicyFaithfulTraining =
                reachTrainingEnabled &&
                reachTrainingVersion == 4;
            var decisionCredit = usesPolicyFaithfulMetrics
                ? GoalkeeperControlTrainingContracts
                    .EvaluateDecisionCreditV4(
                        result,
                        context,
                        controller == null
                            ? 0f
                            : controller
                                .GoalkeeperControlLocalPosition.x)
                : new GoalkeeperControlDecisionCredit(
                    0f,
                    false,
                    GoalkeeperControlTrainingContracts
                        .IsV3PrematureCommit(context),
                    false,
                    false,
                    context.HasVisibleGoalPlanePrediction
                        ? GoalkeeperControlTrainingContracts
                            .VisibleAimErrorMeters(
                                rawPolicyAim,
                                context.VisiblePredictedAim)
                        : -1f,
                    0f,
                    0f);
            if (acceptedCommit &&
                usesPolicyFaithfulTraining)
            {
                AddReward(decisionCredit.Reward);
                attemptDecisionShapingReward +=
                    decisionCredit.Reward;
            }

            if (!hasRecordedCommitMetadata && acceptedCommit)
            {
                hasRecordedCommitMetadata = true;
                firstCommitRawPolicyAim = rawPolicyAim;
                firstCommitWasPremature =
                    decisionCredit.IsPremature;
                firstCommitWasLate = decisionCredit.IsLate;
                firstCommitWasTimely = decisionCredit.IsTimely;
                hasFirstCommitVisiblePrediction =
                    context.HasVisibleGoalPlanePrediction;
                firstCommitVisiblePredictedAim =
                    context.VisiblePredictedAim;
                firstCommitVisibleAimError =
                    decisionCredit.VisibleAimError;
                firstCommitDesiredReach =
                    decisionCredit.DesiredReach01;
                firstCommitReachShortfall =
                    decisionCredit.ReachShortfall;
            }
            else if (!hasRecordedCommitMetadata && preferredOpportunity)
            {
                eligibleCommitDecisionsBeforeCommit++;
            }

            hasPendingCommand = false;
            if (!UsesDeferredDecisionScheduling)
            {
                RequestDecision();
            }
            return result;
        }

        public void OnAttemptStarted(long attemptId)
        {
            pendingCommand = GoalkeeperControlCommand.Neutral;
            currentMask = new GoalkeeperControlActionMask(false);
            hasPendingCommand = false;
            bufferedCommit = false;
            nativeSplitPolicy?.ResetAttempt();
            ResetAttemptTrainingTelemetry();
        }

        public void OnAttemptEnded(AttemptResult result)
        {
            result.FirstCommitWasPremature =
                hasRecordedCommitMetadata &&
                firstCommitWasPremature;
            result.FirstCommitWasLate =
                hasRecordedCommitMetadata &&
                firstCommitWasLate;
            result.FirstCommitWasTimely =
                hasRecordedCommitMetadata &&
                firstCommitWasTimely;
            result.FirstCommitRawPolicyAim =
                firstCommitRawPolicyAim;
            result.HasFirstCommitVisiblePrediction =
                hasRecordedCommitMetadata &&
                hasFirstCommitVisiblePrediction;
            result.FirstCommitVisiblePredictedAim =
                firstCommitVisiblePredictedAim;
            result.FirstCommitVisibleAimError =
                hasRecordedCommitMetadata
                    ? firstCommitVisibleAimError
                    : -1f;
            result.FirstCommitDesiredReach =
                hasRecordedCommitMetadata
                    ? firstCommitDesiredReach
                    : 0f;
            result.FirstCommitReachShortfall =
                hasRecordedCommitMetadata
                    ? firstCommitReachShortfall
                    : 0f;
            result.FirstEligibleCommitDecisionIndex =
                firstEligibleCommitDecisionIndex;
            result.FirstEligibleCommitBallFlightTime =
                firstEligibleCommitBallFlightTime;
            result.FirstEligibleCommitVisibleTimeToGoalPlane =
                firstEligibleCommitVisibleTimeToGoalPlane;
            result.EligibleCommitDecisionsBeforeCommit =
                eligibleCommitDecisionsBeforeCommit;
            result.TrainingDecisionShapingReward =
                attemptDecisionShapingReward;
            result.PolicyActionOverrideCount =
                attemptPolicyActionOverrideCount;
            if (policyDecisionOutstanding)
            {
                policyDecisionDiscardedCount++;
                policyDecisionOutstanding = false;
                hasPendingCommand = false;
            }

            result.PolicyDecisionRequestCount =
                policyDecisionRequestCount;
            result.PolicyDecisionConsumedCount =
                policyDecisionConsumedCount;
            result.PolicyDecisionDiscardedCount =
                policyDecisionDiscardedCount;
            result.PolicyDecisionDuplicateRequestCount =
                policyDecisionDuplicateRequestCount;
            result.PolicyDecisionMissingActionCount =
                policyDecisionMissingActionCount;
            result.NativeInferenceEvaluationCount =
                nativeInferenceEvaluationCount;
            result.NativeInferenceMaximumActionError =
                nativeInferenceMaximumActionError;
            result.NativeInferenceCommitMismatchCount =
                nativeInferenceCommitMismatchCount;
            result.NativeInferenceInvalidOutputCount =
                nativeSplitPolicy == null
                    ? 0
                    : nativeSplitPolicy.InvalidOutputCount;
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
                UsesGameplayObservations
                    ? KernelConstants.GoalkeeperGameplayObservationSpecId
                    : UsesDeferredDecisionScheduling
                        ? KernelConstants.GoalkeeperControlV2ObservationSpecId
                        : KernelConstants.GoalkeeperControlObservationSpecId);
            EndEpisode();
        }

        private void ApplyCurriculumParameters()
        {
            if (controller == null)
            {
                return;
            }

            var parameters = Academy.Instance.EnvironmentParameters;
            ApplyGloveHandlingParameters(parameters);
            controller.GameplayObservationDelayTicks = Mathf.Clamp(
                Mathf.RoundToInt(
                    parameters.GetWithDefault(
                        "stage6.observation_delay_ticks",
                        controller.UsesHumanShots ? 2f : 0f)),
                0,
                127);
            controller.GoalkeeperControlMotor?.SetCommittedGloveForward(
                parameters.GetWithDefault(
                    "stage6.committed_glove_forward_m",
                    controller.UsesHumanShots ? 0.28f : 0f));
            controller.ConfigureAuditGloveContactMaterial(
                parameters.GetWithDefault(
                    "stage6.audit.glove_bounciness",
                    -1f),
                parameters.GetWithDefault(
                    "stage6.audit.glove_friction",
                    -1f));
            nativeSplitInferenceEnabled =
                nativeSplitInferenceByDefault ||
                CommandLineEnablesNativeInference() ||
                parameters.GetWithDefault(
                    "stage5.native_split_inference",
                    0f) >= 0.5f;
            if (nativeSplitInferenceEnabled && nativeSplitPolicy == null)
            {
                throw new InvalidOperationException(
                    "Native split inference is enabled without a packaged policy.");
            }
            stage5Lesson = Mathf.Clamp(
                Mathf.RoundToInt(
                    parameters.GetWithDefault("stage5.lesson", 4f)),
                0,
                4);
            reachTrainingEnabled =
                parameters.GetWithDefault(
                    "stage5.reach_training_enabled",
                    0f) >= 0.5f;
            controlMetricsVersion = Mathf.Clamp(
                Mathf.RoundToInt(
                    parameters.GetWithDefault(
                        "stage5.metrics_version",
                        3f)),
                3,
                5);
            reachTrainingVersion = reachTrainingEnabled
                ? Mathf.Clamp(
                    Mathf.RoundToInt(
                        parameters.GetWithDefault(
                            "stage5.reach_training_version",
                            1f)),
                    1,
                    5)
                : 0;
            var shots = controller.ShotConfiguration;
            if (shots == null)
            {
                return;
            }
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

        private void ApplyGloveHandlingParameters(
            EnvironmentParameters parameters)
        {
            var handler = controller.GoalkeeperGloveHandling;
            if (handler == null)
            {
                return;
            }
            var explicitVersion = parameters.GetWithDefault(
                "stage6.glove_handling_version",
                -1f);
            if (explicitVersion >= 0f)
            {
                handler.SetHandlingVersion(Mathf.RoundToInt(explicitVersion));
            }
            else
            {
                handler.SetHandlingEnabled(
                    parameters.GetWithDefault(
                        "stage6.glove_handling_v1",
                        handler.HandlingEnabled ? 1f : 0f) >= 0.5f);
            }
            handler.SetV2Profile(Mathf.RoundToInt(parameters.GetWithDefault(
                "stage6.glove_handling_profile",
                (float)handler.ProfileV2)));
        }

        private bool TryCollectNativeObservations()
        {
            return hasCachedDecisionObservations;
        }

        private bool UsesGameplayObservations =>
            controller != null && controller.UsesHumanShots;

        private void WriteConfiguredControlObservations(Action<float> add)
        {
            if (UsesGameplayObservations)
            {
                GoalkeeperTrainingContracts.WriteControlStateGameplayV1(
                    controller,
                    controller.GameplayObservationDelayTicks,
                    add);
            }
            else
            {
                GoalkeeperTrainingContracts.WriteControlStateV2(controller, add);
            }
        }

        private static bool CommandLineEnablesNativeInference()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument ==
                    GoalkeeperSplitInferencePolicyV1.EnableArgument)
                {
                    return true;
                }
            }

            return false;
        }

        private GoalkeeperControlCommand ReadTransportCommand(
            ActionBuffers actions)
        {
            var command = GoalkeeperControlCommand.Neutral;
            var continuous = actions.ContinuousActions;
            if (continuous.Length >=
                GoalkeeperControlSpace.ContinuousActionCount)
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
            return command.Sanitized(out _);
        }

        private static float MaximumContinuousError(
            GoalkeeperControlCommand left,
            GoalkeeperControlCommand right)
        {
            return Mathf.Max(
                Mathf.Abs(left.MoveX - right.MoveX),
                Mathf.Abs(left.AimX - right.AimX),
                Mathf.Abs(left.AimY - right.AimY),
                Mathf.Abs(left.Reach - right.Reach));
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
                "Stage5/GloveFirstSaveRate",
                isSave &&
                (result.FirstGoalkeeperContactPart ==
                    GoalkeeperContactPart.LeftGlove ||
                 result.FirstGoalkeeperContactPart ==
                    GoalkeeperContactPart.RightGlove)
                    ? 1f
                    : 0f);
            stats.Add(
                "Stage5/ArmSaveRate",
                isSave &&
                result.FirstGoalkeeperContactPart ==
                    GoalkeeperContactPart.Arm
                    ? 1f
                    : 0f);
            stats.Add(
                "Stage5/TargetClampAttemptRate",
                result.ControlTargetClampCount > 0 ? 1f : 0f);
            stats.Add(
                "Stage5/RootTargetSaturationRate",
                result.ControlTargetClampCount > 0 ? 1f : 0f);
            stats.Add(
                "Stage5/RootTargetSaturationDistance",
                result.RootTargetSaturationDistance);
            stats.Add(
                "Stage5/CommandClampAttemptRate",
                result.ControlCommandClampCount > 0 ? 1f : 0f);
            stats.Add(
                "Stage5/AimGuidanceRate",
                attemptAimGuidanceApplied ? 1f : 0f);
            stats.Add(
                "Stage5/PolicyActionOverrideCount",
                result.PolicyActionOverrideCount);
            stats.Add(
                "Stage5/DecisionShapingReward",
                result.TrainingDecisionShapingReward);
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
                stats.Add(
                    "Stage5/PrematureCommitRate",
                    result.FirstCommitWasPremature ? 1f : 0f);
                stats.Add(
                    "Stage5/LateCommitRate",
                    result.FirstCommitWasLate ? 1f : 0f);
                stats.Add(
                    "Stage5/TimelyCommitRate",
                    result.FirstCommitWasTimely ? 1f : 0f);
                stats.Add(
                    "Stage5/FirstCommitDesiredReach",
                    result.FirstCommitDesiredReach);
                stats.Add(
                    "Stage5/FirstCommitReachShortfall",
                    result.FirstCommitReachShortfall);
                if (result.FirstCommitVisibleAimError >= 0f)
                {
                    stats.Add(
                        "Stage5/FirstCommitVisibleAimError",
                        result.FirstCommitVisibleAimError);
                }
            }
            stats.Add("Stage5/SparseReward", sparseReward);
            stats.Add("Stage5/TrainingReward", trainingReward);
            stats.Add("Stage5/Lesson", stage5Lesson);
            stats.Add(
                "Stage5/PolicyDecisionRequests",
                result.PolicyDecisionRequestCount);
            stats.Add(
                "Stage5/PolicyDecisionConsumed",
                result.PolicyDecisionConsumedCount);
            stats.Add(
                "Stage5/PolicyDecisionDiscarded",
                result.PolicyDecisionDiscardedCount);
            stats.Add(
                "Stage5/PolicyDecisionDuplicateRequests",
                result.PolicyDecisionDuplicateRequestCount);
            stats.Add(
                "Stage5/PolicyDecisionMissingActions",
                result.PolicyDecisionMissingActionCount);
        }

        private void ResetAttemptTrainingTelemetry()
        {
            attemptAutoCommitApplied = false;
            attemptReachFloorApplied = false;
            attemptAimGuidanceApplied = false;
            firstEligibleCommitDecisionIndex = -1;
            firstEligibleCommitBallFlightTime = -1f;
            firstEligibleCommitVisibleTimeToGoalPlane = -1f;
            eligibleCommitDecisionsBeforeCommit = 0;
            hasRecordedCommitMetadata = false;
            firstCommitWasPremature = false;
            firstCommitWasLate = false;
            firstCommitWasTimely = false;
            firstCommitRawPolicyAim = Vector2.zero;
            hasFirstCommitVisiblePrediction = false;
            firstCommitVisiblePredictedAim = Vector2.zero;
            firstCommitVisibleAimError = -1f;
            firstCommitDesiredReach = 0f;
            firstCommitReachShortfall = 0f;
            attemptDecisionShapingReward = 0f;
            attemptPolicyActionOverrideCount = 0;
            policyDecisionOutstanding = false;
            requestedDecisionContext = default;
            policyDecisionRequestCount = 0;
            policyDecisionConsumedCount = 0;
            policyDecisionDiscardedCount = 0;
            policyDecisionDuplicateRequestCount = 0;
            policyDecisionMissingActionCount = 0;
            nativeInferenceEvaluationCount = 0;
            nativeInferenceMaximumActionError = 0f;
            nativeInferenceCommitMismatchCount = 0;
        }
    }
}
