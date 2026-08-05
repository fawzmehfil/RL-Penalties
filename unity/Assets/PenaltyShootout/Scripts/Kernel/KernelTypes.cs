using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum AttemptPhase
    {
        Resetting = 0,
        Ready = 1,
        RunUp = 2,
        BallInFlight = 3,
        Resolving = 4,
        Terminal = 5,
    }

    public enum AttemptOutcome
    {
        None = 0,
        Goal = 1,
        Saved = 2,
        MissWide = 3,
        MissHigh = 4,
        PostOrCrossbarOut = 5,
        BlockedThenOut = 6,
        Timeout = 7,
        Invalid = 8,
    }

    public enum GoalkeeperAction
    {
        Hold = 0,
        ShuffleLeft = 1,
        ShuffleRight = 2,
        DiveLeftLow = 3,
        DiveLeftMiddle = 4,
        DiveLeftHigh = 5,
        DiveRightLow = 6,
        DiveRightMiddle = 7,
        DiveRightHigh = 8,
    }

    public enum GoalkeeperMotorState
    {
        Ready = 0,
        Shuffling = 1,
        Diving = 2,
        Recovering = 3,
    }

    public enum ContactKind
    {
        None = 0,
        Goalkeeper = 1,
        GoalFrame = 2,
        Ground = 3,
    }

    public enum GoalkeeperContactPart
    {
        None = 0,
        LeftGlove = 1,
        RightGlove = 2,
        Arm = 3,
        TorsoOrHead = 4,
        Leg = 5,
    }

    public readonly struct GoalkeeperReachTargets
    {
        public readonly Vector3 LeftGlove;
        public readonly Vector3 RightGlove;

        public GoalkeeperReachTargets(Vector3 leftGlove, Vector3 rightGlove)
        {
            LeftGlove = leftGlove;
            RightGlove = rightGlove;
        }
    }

    [Serializable]
    public struct ScenarioInstance
    {
        public string ScenarioSuiteId;
        public ulong Seed;
        public float TargetXNormalized;
        public float TargetYNormalized;
        public bool ReachFocusSample;
        public Vector3 TargetLocal;
        public float FlightTime;
        public float LaunchDelay;
        public Vector3 Spin;
        public Vector3 LaunchVelocityLocal;
        public ResolvedPlayerShotV1 PlayerShot;
    }

    [Serializable]
    public struct ContactKinematics
    {
        public bool HasValue;
        public Vector3 PointWorld;
        public Vector3 NormalWorld;
        public Vector3 ImpulseWorld;
        public Vector3 RelativeVelocityWorld;
        public Vector3 BallVelocityWorld;
    }

    [Serializable]
    public sealed class AttemptResult
    {
        public string EnvironmentId;
        public string ScenarioSuiteId;
        public long AttemptId;
        public int ArenaId;
        public ulong Seed;
        public AttemptOutcome Outcome;
        public float AttemptTime;
        public float BallFlightTime;
        public float SampledShotFlightTime;
        public float SampledLaunchDelay;
        public bool ReachFocusSample;
        public bool GoalkeeperContact;
        public bool GoalFrameContact;
        public int GoalkeeperContactCount;
        public int GoalFrameContactCount;
        public GoalkeeperContactPart FirstGoalkeeperContactPart;
        public float FirstGoalkeeperContactTime;
        public bool HasFirstGoalkeeperContactKinematics;
        public Vector3 FirstGoalkeeperContactPointLocal;
        public Vector3 FirstGoalkeeperContactNormalLocal;
        public Vector3 FirstGoalkeeperContactImpulseLocal;
        public Vector3 FirstGoalkeeperContactRelativeVelocityLocal;
        public Vector3 FirstGoalkeeperContactBallVelocityLocal;
        public Vector3 FirstGoalkeeperContactRootVelocityLocal;
        public Vector3 FirstGoalkeeperContactLeftGloveVelocityLocal;
        public Vector3 FirstGoalkeeperContactRightGloveVelocityLocal;
        public GoalkeeperContactPart LastGoalkeeperContactPart;
        public bool GloveContact;
        public int GloveContactCount;
        public int LeftGloveContactCount;
        public int RightGloveContactCount;
        public int ArmContactCount;
        public int TorsoOrHeadContactCount;
        public int LegContactCount;
        public Vector3 RequestedTargetLocal;
        public bool HasCentrePlaneIntersection;
        public Vector3 MeasuredCentrePlaneIntersectionLocal;
        public float TargetError;
        public GoalkeeperAction InitialAction;
        public GoalkeeperAction LastAction;
        public GoalkeeperAction FirstAcceptedDiveAction;
        public int FirstDiveDecisionIndex;
        public float FirstDiveAttemptTime;
        public float FirstDiveBallFlightTime;
        public int[] AcceptedActionCounts;
        public int ActionMaskViolations;
        public int DuplicateTerminalEvents;
        public GoalkeeperControlMode ControlMode;
        public GoalkeeperControlCommand InitialControlCommand;
        public GoalkeeperControlCommand LastControlCommand;
        public bool HasSaveCommitment;
        public int FirstCommitDecisionIndex;
        public float FirstCommitAttemptTime;
        public float FirstCommitBallFlightTime;
        public float FirstCommitVisibleTimeToGoalPlane;
        public float FirstCommitReachDemand;
        public float FirstCommitReachExtension;
        public bool FirstCommitWasImmediate;
        public bool FirstCommitWasPremature;
        public bool FirstCommitWasLate;
        public bool FirstCommitWasTimely;
        public Vector2 FirstCommitAim;
        public Vector2 FirstCommitRawPolicyAim;
        public bool HasFirstCommitVisiblePrediction;
        public Vector2 FirstCommitVisiblePredictedAim;
        public float FirstCommitVisibleAimError;
        public float FirstCommitDesiredReach;
        public float FirstCommitReachShortfall;
        public int FirstEligibleCommitDecisionIndex;
        public float FirstEligibleCommitBallFlightTime;
        public float FirstEligibleCommitVisibleTimeToGoalPlane;
        public int EligibleCommitDecisionsBeforeCommit;
        public float GoalkeeperRootDistance;
        public float GoalkeeperPeakRootSpeed;
        public float GoalkeeperPeakReachExtension;
        public int ControlCommandClampCount;
        public int ControlTargetClampCount;
        public float RootTargetSaturationDistance;
        public float TrainingDecisionShapingReward;
        public int PolicyActionOverrideCount;
        public int AcceptedControlDecisionCount;
        public int PolicyDecisionRequestCount;
        public int PolicyDecisionConsumedCount;
        public int PolicyDecisionDiscardedCount;
        public int PolicyDecisionDuplicateRequestCount;
        public int PolicyDecisionMissingActionCount;
        public int NativeInferenceEvaluationCount;
        public float NativeInferenceMaximumActionError;
        public int NativeInferenceCommitMismatchCount;
        public int NativeInferenceInvalidOutputCount;
        public int ControlMoveCommandCount;
        public int ControlReachCommandCount;
        public float[] ControlAbsoluteActionSums;
        public int[] ControlSaturationCounts;
        public float MinimumGloveBallDistance;
        public float CommittedGloveForward;
        public string GloveHandlingId;
        public string GloveGeometryId;
        public bool GloveHandlingEnabled;
        public int GloveHandlingVersion;
        public string GloveHandlingProfileId;
        public GloveHandlingOutcomeV1 GloveHandlingOutcome;
        public GloveContactRegionV1 GloveInitialContactRegion;
        public GloveContactRegionV1 GloveContactRegion;
        public int GloveCandidateContactCount;
        public Vector3 GloveRelativeImpactVelocityLocal;
        public Vector3 GloveReconstructedIncomingVelocityLocal;
        public float GlovePalmAlignment;
        public float GloveForwardSpeed;
        public float GloveIncomingSpeed;
        public float GloveOutgoingSpeed;
        public float GloveOutgoingEnergyRatio;
        public bool GloveCatchEligible;
        public bool GlovePunchEligible;
        public GloveHandlingRejectionReasonV2 GloveHandlingRejectionReason;
        public float GloveCaptureDistance;
        public bool GloveTwoHandCandidate;
        public float GloveTwoHandSeparation;
        public float GloveNormalizedContactExtent;
        public Vector3 GloveAppliedImpulseLocal;
        public int GlovePossessionHandCount;
        public int GloveControlledResponseCount;
        public float GlovePossessionDuration;
        public ResolvedPlayerShotV1 PlayerShot;
        public int ObservationDelayTicks;
    }

    public readonly struct GoalkeeperDecisionContext
    {
        public readonly long AttemptId;
        public readonly int DecisionIndex;
        public readonly int PhysicsTick;
        public readonly float BallFlightTime;

        public GoalkeeperDecisionContext(
            long attemptId,
            int decisionIndex,
            int physicsTick,
            float ballFlightTime)
        {
            AttemptId = attemptId;
            DecisionIndex = decisionIndex;
            PhysicsTick = physicsTick;
            BallFlightTime = ballFlightTime;
        }
    }

    public struct GoalkeeperActionMask
    {
        private ushort allowedBits;

        public static GoalkeeperActionMask HoldOnly
        {
            get
            {
                var mask = new GoalkeeperActionMask();
                mask.Allow(GoalkeeperAction.Hold);
                return mask;
            }
        }

        public static GoalkeeperActionMask All
        {
            get
            {
                var mask = new GoalkeeperActionMask();
                for (var index = 0; index <= (int)GoalkeeperAction.DiveRightHigh; index++)
                {
                    mask.Allow((GoalkeeperAction)index);
                }

                return mask;
            }
        }

        public void Allow(GoalkeeperAction action)
        {
            allowedBits = (ushort)(allowedBits | (1 << (int)action));
        }

        public void Disallow(GoalkeeperAction action)
        {
            allowedBits = (ushort)(allowedBits & ~(1 << (int)action));
        }

        public bool IsAllowed(GoalkeeperAction action)
        {
            var index = (int)action;
            return index >= 0 &&
                index <= (int)GoalkeeperAction.DiveRightHigh &&
                (allowedBits & (1 << index)) != 0;
        }
    }

    public interface IGoalkeeperActionSource
    {
        GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask);

        void OnAttemptStarted(long attemptId);
        void OnAttemptEnded(AttemptResult result);
    }

    public interface IAttemptResettable
    {
        void ResetForAttempt(long attemptId, ulong seed);
        bool ValidateReset(out string error);
    }

    [Serializable]
    public sealed class AttemptStateMachine
    {
        [SerializeField]
        private AttemptPhase phase = AttemptPhase.Terminal;

        [SerializeField]
        private int invalidTransitionCount;

        public AttemptPhase Phase => phase;
        public int InvalidTransitionCount => invalidTransitionCount;

        public void InitializeTerminal()
        {
            phase = AttemptPhase.Terminal;
            invalidTransitionCount = 0;
        }

        public bool TryTransition(AttemptPhase next)
        {
            if (!IsValidTransition(phase, next))
            {
                invalidTransitionCount++;
                return false;
            }

            phase = next;
            return true;
        }

        public static bool IsValidTransition(AttemptPhase from, AttemptPhase to)
        {
            switch (from)
            {
                case AttemptPhase.Resetting:
                    return to == AttemptPhase.Ready;
                case AttemptPhase.Ready:
                    return to == AttemptPhase.RunUp;
                case AttemptPhase.RunUp:
                    return to == AttemptPhase.BallInFlight;
                case AttemptPhase.BallInFlight:
                    return to == AttemptPhase.Resolving;
                case AttemptPhase.Resolving:
                    return to == AttemptPhase.Terminal;
                case AttemptPhase.Terminal:
                    return to == AttemptPhase.Resetting;
                default:
                    return false;
            }
        }
    }

    [Serializable]
    public sealed class AttemptOutcomeLatch
    {
        [SerializeField]
        private AttemptOutcome outcome;

        [SerializeField]
        private int duplicateTerminalEvents;

        public AttemptOutcome Outcome => outcome;
        public int DuplicateTerminalEvents => duplicateTerminalEvents;
        public bool IsTerminal => outcome != AttemptOutcome.None;

        public void Reset()
        {
            outcome = AttemptOutcome.None;
            duplicateTerminalEvents = 0;
        }

        public bool TrySet(AttemptOutcome terminalOutcome)
        {
            if (terminalOutcome == AttemptOutcome.None)
            {
                return false;
            }

            if (IsTerminal)
            {
                duplicateTerminalEvents++;
                return false;
            }

            outcome = terminalOutcome;
            return true;
        }
    }

    public sealed class ContactHistory
    {
        public bool GoalkeeperTouched { get; private set; }
        public bool GoalFrameTouched { get; private set; }
        public int GoalkeeperContactCount { get; private set; }
        public int GoalFrameContactCount { get; private set; }
        public float LastGoalkeeperContactTime { get; private set; }
        public float LastGoalFrameContactTime { get; private set; }
        public float FirstGoalkeeperContactTime { get; private set; }
        public GoalkeeperContactPart FirstGoalkeeperContactPart { get; private set; }
        public ContactKinematics FirstGoalkeeperContactKinematics { get; private set; }
        public GoalkeeperContactPart LastGoalkeeperContactPart { get; private set; }
        public bool GloveTouched { get; private set; }
        public int GloveContactCount { get; private set; }
        public int LeftGloveContactCount { get; private set; }
        public int RightGloveContactCount { get; private set; }
        public int ArmContactCount { get; private set; }
        public int TorsoOrHeadContactCount { get; private set; }
        public int LegContactCount { get; private set; }

        public void Reset()
        {
            GoalkeeperTouched = false;
            GoalFrameTouched = false;
            GoalkeeperContactCount = 0;
            GoalFrameContactCount = 0;
            LastGoalkeeperContactTime = float.NegativeInfinity;
            LastGoalFrameContactTime = float.NegativeInfinity;
            FirstGoalkeeperContactTime = float.NegativeInfinity;
            FirstGoalkeeperContactPart = GoalkeeperContactPart.None;
            FirstGoalkeeperContactKinematics = default;
            LastGoalkeeperContactPart = GoalkeeperContactPart.None;
            GloveTouched = false;
            GloveContactCount = 0;
            LeftGloveContactCount = 0;
            RightGloveContactCount = 0;
            ArmContactCount = 0;
            TorsoOrHeadContactCount = 0;
            LegContactCount = 0;
        }

        public void Record(
            ContactKind kind,
            float attemptTime,
            GoalkeeperContactPart goalkeeperPart = GoalkeeperContactPart.None,
            ContactKinematics kinematics = default)
        {
            switch (kind)
            {
                case ContactKind.Goalkeeper:
                    if (!GoalkeeperTouched)
                    {
                        FirstGoalkeeperContactTime = attemptTime;
                        FirstGoalkeeperContactPart = goalkeeperPart;
                        FirstGoalkeeperContactKinematics = kinematics;
                    }

                    GoalkeeperTouched = true;
                    GoalkeeperContactCount++;
                    LastGoalkeeperContactTime = attemptTime;
                    LastGoalkeeperContactPart = goalkeeperPart;
                    RecordGoalkeeperPart(goalkeeperPart);
                    break;
                case ContactKind.GoalFrame:
                    GoalFrameTouched = true;
                    GoalFrameContactCount++;
                    LastGoalFrameContactTime = attemptTime;
                    break;
            }
        }

        private void RecordGoalkeeperPart(GoalkeeperContactPart part)
        {
            switch (part)
            {
                case GoalkeeperContactPart.LeftGlove:
                    GloveTouched = true;
                    GloveContactCount++;
                    LeftGloveContactCount++;
                    break;
                case GoalkeeperContactPart.RightGlove:
                    GloveTouched = true;
                    GloveContactCount++;
                    RightGloveContactCount++;
                    break;
                case GoalkeeperContactPart.Arm:
                    ArmContactCount++;
                    break;
                case GoalkeeperContactPart.TorsoOrHead:
                    TorsoOrHeadContactCount++;
                    break;
                case GoalkeeperContactPart.Leg:
                    LegContactCount++;
                    break;
            }
        }
    }

    public struct Pcg32
    {
        private ulong state;
        private ulong increment;

        public Pcg32(ulong seed, ulong sequence = 1442695040888963407UL)
        {
            state = 0UL;
            increment = (sequence << 1) | 1UL;
            NextUInt();
            state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            var oldState = state;
            state = oldState * 6364136223846793005UL + increment;
            var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            var rotation = (int)(oldState >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        public float Range(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, NextFloat());
        }

        public static ulong DeriveSeed(ulong masterSeed, int arenaId, long attemptId)
        {
            var value = masterSeed;
            value ^= (ulong)(uint)arenaId + 0x9e3779b97f4a7c15UL + (value << 6) + (value >> 2);
            value ^= (ulong)attemptId + 0x9e3779b97f4a7c15UL + (value << 6) + (value >> 2);
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebUL;
            value ^= value >> 31;
            return value;
        }
    }

    public static class KernelMath
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
