using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum PlayerShotInputDeviceV1
    {
        Pointer = 0,
        Keyboard = 1,
        AutomatedTest = 2,
    }

    [Serializable]
    public struct PlayerPenaltyShotRequestV1
    {
        public PlayerShotCommandV1 Command;
        public PlayerShotStyleV1 Style;
        public ulong InputSeed;
        public float TimingQuality;
        public float ChargeDuration;
        public PlayerShotInputDeviceV1 InputDevice;

        public bool Validate(out string error)
        {
            if (!Command.Validate(out error))
            {
                return false;
            }

            if (!KernelMath.IsFinite(TimingQuality) ||
                TimingQuality < 0f || TimingQuality > 1f ||
                !KernelMath.IsFinite(ChargeDuration) || ChargeDuration < 0f ||
                InputSeed == 0UL ||
                Style < PlayerShotStyleV1.Placed ||
                Style > PlayerShotStyleV1.Curled ||
                InputDevice < PlayerShotInputDeviceV1.Pointer ||
                InputDevice > PlayerShotInputDeviceV1.AutomatedTest)
            {
                error = "Player penalty request violates player-penalty-input-v1.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }

    public static class PlayerShotScenarioFactoryV1
    {
        public const float PresentationRunUpSeconds = 0.28f;

        public static ScenarioInstance Resolve(
            PlayerPenaltyShotRequestV1 request,
            ulong scenarioSeed,
            Vector3 gravity,
            float fixedTimestep,
            PlayerShotPhysicsConfigV1 physics)
        {
            if (!request.Validate(out var requestError))
            {
                throw new ArgumentException(requestError, nameof(request));
            }
            if (scenarioSeed == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(scenarioSeed));
            }

            var resolved = PlayerShotResolverV1.Resolve(
                request.Command,
                request.Style,
                "player-interactive",
                request.Command.Power >= 0.95f &&
                Mathf.Abs(request.Command.AimX) >= 0.90f,
                gravity,
                fixedTimestep,
                physics);
            return new ScenarioInstance
            {
                ScenarioSuiteId = KernelConstants.PlayerInteractiveScenarioSuiteId,
                Seed = scenarioSeed,
                TargetXNormalized = resolved.Command.AimX,
                TargetYNormalized = (resolved.Command.AimY + 1f) * 0.5f,
                ReachFocusSample = false,
                TargetLocal = resolved.ContactAdjustedTargetLocal,
                FlightTime = resolved.NominalFlightTime,
                LaunchDelay = PresentationRunUpSeconds,
                Spin = resolved.AngularVelocityLocal,
                LaunchVelocityLocal = resolved.LaunchVelocityLocal,
                PlayerShot = resolved,
            };
        }

        public static bool Validate(
            ScenarioInstance scenario,
            PlayerShotPhysicsConfigV1 physics,
            out string error)
        {
            error = string.Empty;
            if (physics == null || !physics.Validate(out error))
            {
                return false;
            }

            if (scenario.ScenarioSuiteId !=
                    KernelConstants.PlayerInteractiveScenarioSuiteId ||
                scenario.Seed == 0UL ||
                scenario.PlayerShot.ShotContractId !=
                    KernelConstants.PlayerShotContractId ||
                scenario.PlayerShot.ShotPhysicsId !=
                    KernelConstants.PlayerShotPhysicsId ||
                !scenario.PlayerShot.Command.Validate(out error) ||
                !KernelMath.IsFinite(scenario.LaunchVelocityLocal) ||
                !KernelMath.IsFinite(scenario.Spin) ||
                scenario.LaunchDelay != PresentationRunUpSeconds ||
                scenario.PlayerShot.SolverCrossingError >
                    physics.MaximumAcceptedSolverError + 1e-5f ||
                scenario.PlayerShot.PredictedCurveDisplacement.magnitude >
                    physics.MaximumCurveDisplacement + 1e-5f)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "Interactive player scenario violates its contract.";
                }
                return false;
            }

            return true;
        }
    }

    public readonly struct PlayerShotLaunchEventV1
    {
        public readonly long AttemptId;
        public readonly float AttemptTime;
        public readonly ScenarioInstance Scenario;

        public PlayerShotLaunchEventV1(
            long attemptId,
            float attemptTime,
            ScenarioInstance scenario)
        {
            AttemptId = attemptId;
            AttemptTime = attemptTime;
            Scenario = scenario;
        }
    }

    public readonly struct GoalkeeperControlCommandEventV1
    {
        public readonly long AttemptId;
        public readonly int DecisionIndex;
        public readonly int PhysicsTick;
        public readonly float BallFlightTime;
        public readonly GoalkeeperControlCommand Command;

        public GoalkeeperControlCommandEventV1(
            long attemptId,
            int decisionIndex,
            int physicsTick,
            float ballFlightTime,
            GoalkeeperControlCommand command)
        {
            AttemptId = attemptId;
            DecisionIndex = decisionIndex;
            PhysicsTick = physicsTick;
            BallFlightTime = ballFlightTime;
            Command = command;
        }
    }

    public readonly struct BallContactReplayEventV1
    {
        public readonly long AttemptId;
        public readonly float AttemptTime;
        public readonly ContactKind Kind;
        public readonly GoalkeeperContactPart GoalkeeperPart;
        public readonly ContactKinematics Kinematics;

        public BallContactReplayEventV1(
            long attemptId,
            float attemptTime,
            BallContactEventV1 contact)
        {
            AttemptId = attemptId;
            AttemptTime = attemptTime;
            Kind = contact.Kind;
            GoalkeeperPart = contact.GoalkeeperPart;
            Kinematics = contact.Kinematics;
        }
    }
}
