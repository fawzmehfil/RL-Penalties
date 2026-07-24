using System;
using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class PenaltyAreaController : MonoBehaviour
    {
        [Header("Versioned configuration")]
        [SerializeField]
        private EnvironmentKernelConfig environmentConfiguration;

        [SerializeField]
        private ShotDistributionConfig shotConfiguration;

        [SerializeField]
        private GoalkeeperMotorConfig motorConfiguration;

        [Header("Arena references")]
        [SerializeField]
        private Transform arenaOrigin;

        [SerializeField]
        private Rigidbody ball;

        [SerializeField]
        private Collider ballCollider;

        [SerializeField]
        private BallContactSensor ballContactSensor;

        [SerializeField]
        private GoalkeeperMotor goalkeeperMotor;

        [SerializeField]
        private ScenarioController scenarioController;

        [SerializeField]
        private MonoBehaviour actionSource;

        [SerializeField]
        private Transform targetMarker;

        [SerializeField]
        private LineRenderer trajectory;

        [Header("Execution")]
        [SerializeField]
        private int arenaId;

        [SerializeField]
        private ulong masterSeed = 20260723UL;

        [SerializeField]
        private bool autoRun = true;

        [SerializeField]
        private bool manualSimulationMode;

        [SerializeField]
        private bool showDebugUi = true;

        private readonly AttemptStateMachine stateMachine = new AttemptStateMachine();
        private readonly AttemptOutcomeLatch outcomeLatch = new AttemptOutcomeLatch();
        private readonly ContactHistory contactHistory = new ContactHistory();
        private readonly List<Vector3> trajectoryPoints = new List<Vector3>(256);

        private IGoalkeeperActionSource resolvedActionSource;
        private ScenarioInstance scenario;
        private AttemptResult lastResult;
        private long attemptId;
        private float attemptTime;
        private float phaseTime;
        private float ballFlightTime;
        private float restTime;
        private float terminalTime;
        private int resetTicks;
        private int physicsTick;
        private int decisionIndex;
        private int actionMaskViolations;
        private bool launchIssued;
        private bool hasCentrePlaneIntersection;
        private Vector3 centrePlaneIntersectionLocal;
        private Vector3 previousBallLocal;
        private GoalkeeperAction lastAction;
        private GoalkeeperAction initialAction;
        private bool initialized;

        public event Action<AttemptResult> AttemptCompleted;

        public EnvironmentKernelConfig EnvironmentConfiguration
        {
            get => environmentConfiguration;
            set => environmentConfiguration = value;
        }

        public ShotDistributionConfig ShotConfiguration
        {
            get => shotConfiguration;
            set => shotConfiguration = value;
        }

        public GoalkeeperMotorConfig MotorConfiguration
        {
            get => motorConfiguration;
            set => motorConfiguration = value;
        }

        public Transform ArenaOrigin
        {
            get => arenaOrigin;
            set => arenaOrigin = value;
        }

        public Rigidbody Ball
        {
            get => ball;
            set => ball = value;
        }

        public Collider BallCollider
        {
            get => ballCollider;
            set => ballCollider = value;
        }

        public BallContactSensor BallContactSensor
        {
            get => ballContactSensor;
            set => ballContactSensor = value;
        }

        public GoalkeeperMotor GoalkeeperMotor
        {
            get => goalkeeperMotor;
            set => goalkeeperMotor = value;
        }

        public ScenarioController ScenarioController
        {
            get => scenarioController;
            set => scenarioController = value;
        }

        public MonoBehaviour ActionSource
        {
            get => actionSource;
            set
            {
                actionSource = value;
                resolvedActionSource = value as IGoalkeeperActionSource;
            }
        }

        public Transform TargetMarker
        {
            get => targetMarker;
            set => targetMarker = value;
        }

        public LineRenderer Trajectory
        {
            get => trajectory;
            set => trajectory = value;
        }

        public int ArenaId
        {
            get => arenaId;
            set
            {
                arenaId = value;
                if (scenarioController != null)
                {
                    scenarioController.ArenaId = value;
                }
            }
        }

        public ulong MasterSeed
        {
            get => masterSeed;
            set
            {
                masterSeed = value;
                if (scenarioController != null)
                {
                    scenarioController.MasterSeed = value;
                }
            }
        }

        public bool AutoRun
        {
            get => autoRun;
            set => autoRun = value;
        }

        public bool ManualSimulationMode
        {
            get => manualSimulationMode;
            set => manualSimulationMode = value;
        }

        public bool ShowDebugUi
        {
            get => showDebugUi;
            set => showDebugUi = value;
        }

        public AttemptPhase Phase => stateMachine.Phase;
        public AttemptOutcome CurrentOutcome => outcomeLatch.Outcome;
        public ScenarioInstance CurrentScenario => scenario;
        public AttemptResult LastResult => lastResult;
        public long AttemptId => attemptId;
        public float AttemptTime => attemptTime;
        public float BallFlightTime => ballFlightTime;
        public int PhysicsTick => physicsTick;
        public int DecisionIndex => decisionIndex;
        public GoalkeeperAction LastAction => lastAction;
        public bool HasCentrePlaneIntersection => hasCentrePlaneIntersection;
        public Vector3 CentrePlaneIntersectionLocal => centrePlaneIntersectionLocal;
        public bool IsTerminal => stateMachine.Phase == AttemptPhase.Terminal;
        public Vector3 BallLocalPosition => ToLocal(ball == null ? Vector3.zero : ball.position);
        public Vector3 BallLocalVelocity => ToLocalDirection(ball == null ? Vector3.zero : ball.linearVelocity);
        public Vector3 BallAngularVelocity => ball == null ? Vector3.zero : ball.angularVelocity;
        public GoalkeeperMotorState GoalkeeperMotorState =>
            goalkeeperMotor == null ? GoalkeeperMotorState.Ready : goalkeeperMotor.State;
        public GoalkeeperAction GoalkeeperDiveAction =>
            goalkeeperMotor == null ? GoalkeeperAction.Hold : goalkeeperMotor.DiveAction;
        public float GoalkeeperLocalX =>
            goalkeeperMotor == null ? 0f : goalkeeperMotor.LocalPosition.x;
        public float GoalkeeperLateralVelocity =>
            goalkeeperMotor == null ? 0f : goalkeeperMotor.LateralVelocity;

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (autoRun)
            {
                BeginNextAttempt();
            }
        }

        private void FixedUpdate()
        {
            if (!manualSimulationMode)
            {
                ManualFixedStep();
            }
        }

        public bool Initialize()
        {
            if (initialized)
            {
                return ValidateDependencies(out _);
            }

            stateMachine.InitializeTerminal();
            resolvedActionSource = actionSource as IGoalkeeperActionSource;
            initialized = ValidateDependencies(out var error);
            if (!initialized)
            {
                Debug.LogError($"PenaltyAreaController initialization failed: {error}", this);
                enabled = false;
                return false;
            }

            Time.fixedDeltaTime = environmentConfiguration.FixedTimestep;
            goalkeeperMotor.Configuration = motorConfiguration;
            goalkeeperMotor.ArenaOrigin = arenaOrigin;
            return true;
        }

        public bool ValidateDependencies(out string error)
        {
            if (environmentConfiguration == null ||
                shotConfiguration == null ||
                motorConfiguration == null)
            {
                error = "Environment, shot, and motor configuration assets are required.";
                return false;
            }

            if (!environmentConfiguration.Validate(out error) ||
                !shotConfiguration.Validate(out error) ||
                !motorConfiguration.Validate(out error))
            {
                return false;
            }

            if (arenaOrigin == null ||
                ball == null ||
                ballCollider == null ||
                ballContactSensor == null ||
                goalkeeperMotor == null ||
                scenarioController == null)
            {
                error =
                    "Arena origin, ball, scenario, contact sensor, and goalkeeper motor references are required.";
                return false;
            }

            if (arenaOrigin.lossyScale != Vector3.one ||
                Quaternion.Angle(arenaOrigin.rotation, Quaternion.identity) > 0.01f)
            {
                error = "Training arena roots must use identity rotation and unit scale.";
                return false;
            }

            if (actionSource != null && !(actionSource is IGoalkeeperActionSource))
            {
                error = "Configured action source does not implement IGoalkeeperActionSource.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void BeginNextAttempt()
        {
            if (!initialized && !Initialize())
            {
                return;
            }

            if (stateMachine.Phase != AttemptPhase.Terminal)
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            if (!stateMachine.TryTransition(AttemptPhase.Resetting))
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            attemptId++;
            attemptTime = 0f;
            phaseTime = 0f;
            ballFlightTime = 0f;
            restTime = 0f;
            terminalTime = 0f;
            resetTicks = 0;
            physicsTick = 0;
            decisionIndex = 0;
            actionMaskViolations = 0;
            launchIssued = false;
            hasCentrePlaneIntersection = false;
            centrePlaneIntersectionLocal = default;
            lastAction = GoalkeeperAction.Hold;
            initialAction = GoalkeeperAction.Hold;
            outcomeLatch.Reset();
            contactHistory.Reset();
            trajectoryPoints.Clear();
            if (trajectory != null)
            {
                trajectory.positionCount = 0;
            }

            try
            {
                scenarioController.Configuration = shotConfiguration;
                scenarioController.ArenaId = arenaId;
                scenarioController.MasterSeed = masterSeed;
                scenario = scenarioController.Sample(
                    attemptId,
                    Physics.gravity,
                    environmentConfiguration.FixedTimestep);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Complete(AttemptOutcome.Invalid);
                return;
            }

            if (!ProceduralShotGenerator.ValidateOnTarget(
                    scenario,
                    shotConfiguration,
                    out var scenarioError))
            {
                Debug.LogError(scenarioError, this);
                Complete(AttemptOutcome.Invalid);
                return;
            }

            ballCollider.enabled = false;
            ResetBall(ToWorld(KernelConstants.CanonicalLaunch));
            ballContactSensor.ResetForAttempt(attemptId, scenario.Seed);
            goalkeeperMotor.ResetForAttempt(attemptId, scenario.Seed);
            scenarioController.ResetForAttempt(attemptId, scenario.Seed);
            var sensorReset = ballContactSensor.ValidateReset(out var sensorError);
            var motorReset = goalkeeperMotor.ValidateReset(out var motorError);
            var scenarioReset = scenarioController.ValidateReset(out var scenarioResetError);
            if (!sensorReset || !motorReset || !scenarioReset)
            {
                Debug.LogError($"{sensorError} {motorError} {scenarioResetError}", this);
                Complete(AttemptOutcome.Invalid);
                return;
            }

            previousBallLocal = KernelConstants.CanonicalLaunch;
            AddTrajectoryPoint(ball.position);
            if (targetMarker != null)
            {
                targetMarker.position = ToWorld(scenario.TargetLocal);
            }

            resolvedActionSource?.OnAttemptStarted(attemptId);
            Physics.SyncTransforms();
        }

        public void ManualFixedStep()
        {
            if (!initialized)
            {
                return;
            }

            var deltaTime = environmentConfiguration.FixedTimestep;
            switch (stateMachine.Phase)
            {
                case AttemptPhase.Resetting:
                    TickResetting(deltaTime);
                    break;
                case AttemptPhase.Ready:
                    TickReady(deltaTime);
                    break;
                case AttemptPhase.RunUp:
                    TickRunUp(deltaTime);
                    break;
                case AttemptPhase.BallInFlight:
                    TickBallInFlight(deltaTime);
                    break;
                case AttemptPhase.Terminal:
                    TickTerminal(deltaTime);
                    break;
            }
        }

        private void TickResetting(float deltaTime)
        {
            attemptTime += deltaTime;
            phaseTime += deltaTime;
            resetTicks++;
            if (resetTicks < environmentConfiguration.ResetStabilizationTicks)
            {
                return;
            }

            ballCollider.enabled = true;
            Physics.SyncTransforms();
            if (!stateMachine.TryTransition(AttemptPhase.Ready))
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            phaseTime = 0f;
        }

        private void TickReady(float deltaTime)
        {
            attemptTime += deltaTime;
            phaseTime += deltaTime;
            if (phaseTime < environmentConfiguration.ReadyDuration)
            {
                return;
            }

            if (!stateMachine.TryTransition(AttemptPhase.RunUp))
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            phaseTime = 0f;
        }

        private void TickRunUp(float deltaTime)
        {
            attemptTime += deltaTime;
            phaseTime += deltaTime;
            if (phaseTime < scenario.LaunchDelay)
            {
                return;
            }

            if (launchIssued)
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            ball.isKinematic = false;
            ball.linearVelocity = ToWorldDirection(scenario.LaunchVelocityLocal);
            ball.angularVelocity = scenario.Spin;
            ball.WakeUp();
            launchIssued = true;
            previousBallLocal = ToLocal(ball.position);
            if (!stateMachine.TryTransition(AttemptPhase.BallInFlight))
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            phaseTime = 0f;
            physicsTick = 0;
            decisionIndex = 0;
            RequestAction();
        }

        private void TickBallInFlight(float deltaTime)
        {
            attemptTime += deltaTime;
            phaseTime += deltaTime;
            ballFlightTime += deltaTime;
            ballContactSensor.Drain(contactHistory, attemptTime);

            var currentLocal = ToLocal(ball.position);
            AddTrajectoryPoint(ball.position);
            if (!KernelMath.IsFinite(currentLocal) ||
                !KernelMath.IsFinite(ball.linearVelocity) ||
                !KernelMath.IsFinite(ball.angularVelocity))
            {
                Complete(AttemptOutcome.Invalid);
                return;
            }

            if (!hasCentrePlaneIntersection &&
                KernelGoalGeometry.TryIntersectPlane(
                    previousBallLocal,
                    currentLocal,
                    0f,
                    out var centreIntersection))
            {
                hasCentrePlaneIntersection = true;
                centrePlaneIntersectionLocal = centreIntersection;
            }

            if (KernelGoalGeometry.TryIntersectPlane(
                    previousBallLocal,
                    currentLocal,
                    -KernelConstants.BallRadius,
                    out var wholeBallIntersection))
            {
                Complete(OutcomeResolver.ResolveGoalPlaneCrossing(
                    wholeBallIntersection,
                    contactHistory));
                return;
            }

            if (IsOutsideDangerRegion(currentLocal))
            {
                Complete(OutcomeResolver.ResolveSafeExit(
                    currentLocal,
                    environmentConfiguration.DangerMaximum,
                    contactHistory));
                return;
            }

            if (OutcomeResolver.TryResolveSave(
                    contactHistory,
                    attemptTime,
                    ball.linearVelocity.magnitude,
                    deltaTime,
                    environmentConfiguration,
                    ref restTime))
            {
                Complete(AttemptOutcome.Saved);
                return;
            }

            if (OutcomeResolver.TryResolveFrameContact(
                    contactHistory,
                    attemptTime,
                    environmentConfiguration))
            {
                Complete(AttemptOutcome.PostOrCrossbarOut);
                return;
            }

            goalkeeperMotor.Tick(deltaTime);
            physicsTick++;
            if (physicsTick % environmentConfiguration.DecisionPeriodTicks == 0)
            {
                RequestAction();
            }

            if (attemptTime >= environmentConfiguration.AttemptTimeout)
            {
                Complete(OutcomeResolver.ResolveAttemptLimit(
                    currentLocal,
                    ToLocalDirection(ball.linearVelocity),
                    contactHistory,
                    environmentConfiguration));
                return;
            }

            previousBallLocal = currentLocal;
        }

        private void TickTerminal(float deltaTime)
        {
            if (!autoRun)
            {
                return;
            }

            terminalTime += deltaTime;
            if (terminalTime >= environmentConfiguration.TerminalHoldDuration)
            {
                BeginNextAttempt();
            }
        }

        private void RequestAction()
        {
            var context = new GoalkeeperDecisionContext(
                attemptId,
                decisionIndex,
                physicsTick,
                ballFlightTime);
            var mask = goalkeeperMotor.GetActionMask();
            var requested = resolvedActionSource == null
                ? GoalkeeperAction.Hold
                : resolvedActionSource.Decide(context, mask);
            if (!mask.IsAllowed(requested) || !goalkeeperMotor.TryApplyAction(requested))
            {
                actionMaskViolations++;
                requested = GoalkeeperAction.Hold;
                goalkeeperMotor.TryApplyAction(requested);
            }

            lastAction = requested;
            if (decisionIndex == 0)
            {
                initialAction = requested;
            }

            decisionIndex++;
        }

        private bool IsOutsideDangerRegion(Vector3 localPosition)
        {
            var minimum = environmentConfiguration.DangerMinimum;
            var maximum = environmentConfiguration.DangerMaximum;
            return localPosition.x < minimum.x ||
                localPosition.x > maximum.x ||
                localPosition.y < minimum.y ||
                localPosition.y > maximum.y ||
                localPosition.z < minimum.z ||
                localPosition.z > maximum.z;
        }

        private void Complete(AttemptOutcome outcome)
        {
            if (stateMachine.Phase == AttemptPhase.Terminal)
            {
                outcomeLatch.TrySet(outcome);
                return;
            }

            if (stateMachine.Phase != AttemptPhase.Resolving)
            {
                if (stateMachine.Phase != AttemptPhase.BallInFlight)
                {
                    outcome = AttemptOutcome.Invalid;
                    ForceResolvingFromNonFlight();
                }
                else if (!stateMachine.TryTransition(AttemptPhase.Resolving))
                {
                    outcome = AttemptOutcome.Invalid;
                }
            }

            if (!outcomeLatch.TrySet(outcome))
            {
                return;
            }

            if (ball != null)
            {
                ball.linearVelocity = Vector3.zero;
                ball.angularVelocity = Vector3.zero;
                ball.isKinematic = true;
            }

            if (stateMachine.Phase != AttemptPhase.Resolving)
            {
                ForceResolvingFromNonFlight();
            }

            if (!stateMachine.TryTransition(AttemptPhase.Terminal))
            {
                return;
            }

            terminalTime = 0f;
            var targetError = hasCentrePlaneIntersection
                ? Vector2.Distance(
                    new Vector2(centrePlaneIntersectionLocal.x, centrePlaneIntersectionLocal.y),
                    new Vector2(scenario.TargetLocal.x, scenario.TargetLocal.y))
                : float.PositiveInfinity;
            lastResult = new AttemptResult
            {
                EnvironmentId = KernelConstants.EnvironmentId,
                ScenarioSuiteId = scenario.ScenarioSuiteId,
                AttemptId = attemptId,
                ArenaId = arenaId,
                Seed = scenario.Seed,
                Outcome = outcomeLatch.Outcome,
                AttemptTime = attemptTime,
                BallFlightTime = ballFlightTime,
                GoalkeeperContact = contactHistory.GoalkeeperTouched,
                GoalFrameContact = contactHistory.GoalFrameTouched,
                GoalkeeperContactCount = contactHistory.GoalkeeperContactCount,
                GoalFrameContactCount = contactHistory.GoalFrameContactCount,
                LastGoalkeeperContactPart = contactHistory.LastGoalkeeperContactPart,
                GloveContact = contactHistory.GloveTouched,
                GloveContactCount = contactHistory.GloveContactCount,
                LeftGloveContactCount = contactHistory.LeftGloveContactCount,
                RightGloveContactCount = contactHistory.RightGloveContactCount,
                ArmContactCount = contactHistory.ArmContactCount,
                TorsoOrHeadContactCount = contactHistory.TorsoOrHeadContactCount,
                LegContactCount = contactHistory.LegContactCount,
                RequestedTargetLocal = scenario.TargetLocal,
                HasCentrePlaneIntersection = hasCentrePlaneIntersection,
                MeasuredCentrePlaneIntersectionLocal = centrePlaneIntersectionLocal,
                TargetError = targetError,
                InitialAction = initialAction,
                LastAction = lastAction,
                ActionMaskViolations = actionMaskViolations,
                DuplicateTerminalEvents = outcomeLatch.DuplicateTerminalEvents,
            };

            resolvedActionSource?.OnAttemptEnded(lastResult);
            AttemptCompleted?.Invoke(lastResult);
        }

        private void ForceResolvingFromNonFlight()
        {
            // Configuration failures can happen before BallInFlight. Walk the
            // state machine forward so terminal-state invariants remain true.
            while (stateMachine.Phase != AttemptPhase.Resolving &&
                stateMachine.Phase != AttemptPhase.Terminal)
            {
                var next = stateMachine.Phase == AttemptPhase.Resetting
                    ? AttemptPhase.Ready
                    : stateMachine.Phase == AttemptPhase.Ready
                        ? AttemptPhase.RunUp
                        : stateMachine.Phase == AttemptPhase.RunUp
                            ? AttemptPhase.BallInFlight
                            : AttemptPhase.Resolving;
                if (!stateMachine.TryTransition(next))
                {
                    break;
                }
            }
        }

        private void ResetBall(Vector3 worldPosition)
        {
            ball.isKinematic = false;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            ball.isKinematic = true;
            ball.position = worldPosition;
            ball.rotation = arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            ball.Sleep();
            ball.transform.SetPositionAndRotation(ball.position, ball.rotation);
        }

        private void AddTrajectoryPoint(Vector3 worldPoint)
        {
            trajectoryPoints.Add(worldPoint);
            if (trajectory == null)
            {
                return;
            }

            trajectory.positionCount = trajectoryPoints.Count;
            trajectory.SetPosition(trajectoryPoints.Count - 1, worldPoint);
        }

        private Vector3 ToLocal(Vector3 worldPosition)
        {
            return arenaOrigin == null
                ? worldPosition
                : arenaOrigin.InverseTransformPoint(worldPosition);
        }

        private Vector3 ToWorld(Vector3 localPosition)
        {
            return arenaOrigin == null
                ? localPosition
                : arenaOrigin.TransformPoint(localPosition);
        }

        private Vector3 ToWorldDirection(Vector3 localDirection)
        {
            return arenaOrigin == null
                ? localDirection
                : arenaOrigin.TransformDirection(localDirection);
        }

        private Vector3 ToLocalDirection(Vector3 worldDirection)
        {
            return arenaOrigin == null
                ? worldDirection
                : arenaOrigin.InverseTransformDirection(worldDirection);
        }

        private void OnGUI()
        {
            if (!showDebugUi || Application.isBatchMode || arenaId != 0)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 410f, 330f), GUI.skin.box);
            GUILayout.Label("Penalty Shootout RL — Stage 1 Kernel");
            GUILayout.Label($"Environment: {KernelConstants.EnvironmentId}");
            GUILayout.Label($"Attempt / seed: {attemptId} / {scenario.Seed}");
            GUILayout.Label($"Phase: {Phase}");
            GUILayout.Label($"Outcome: {CurrentOutcome}");
            GUILayout.Label($"Target: {scenario.TargetLocal:F3}");
            GUILayout.Label($"Flight time / delay: {scenario.FlightTime:F2}s / {scenario.LaunchDelay:F2}s");
            GUILayout.Label($"Action: {lastAction}");
            GUILayout.Label($"Motor: {goalkeeperMotor.State}");
            GUILayout.Label($"Keeper contacts: {contactHistory.GoalkeeperContactCount}");
            GUILayout.Label(
                $"Glove contacts: {contactHistory.GloveContactCount} " +
                $"(L {contactHistory.LeftGloveContactCount} / " +
                $"R {contactHistory.RightGloveContactCount})");
            GUILayout.Label(
                $"Last keeper contact: {contactHistory.LastGoalkeeperContactPart}");
            GUILayout.Label(hasCentrePlaneIntersection
                ? $"Measured crossing: {centrePlaneIntersectionLocal:F3}"
                : "Measured crossing: pending");
            GUILayout.Space(6f);
            GUILayout.Label("Controls: A/D shuffle, Q/W/E left dives, U/I/O right dives");
            if (GUILayout.Button("Start next procedural attempt"))
            {
                if (Phase == AttemptPhase.Terminal)
                {
                    BeginNextAttempt();
                }
            }

            autoRun = GUILayout.Toggle(autoRun, "Automatically repeat attempts");
            GUILayout.EndArea();
        }
    }
}
