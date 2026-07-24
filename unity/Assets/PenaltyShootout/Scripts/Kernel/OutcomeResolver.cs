using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class OutcomeResolver
    {
        public static AttemptOutcome ResolveGoalPlaneCrossing(
            Vector3 wholeBallIntersection,
            ContactHistory contacts)
        {
            var crossing = KernelGoalGeometry.ClassifyOutsideCrossing(wholeBallIntersection);
            if (crossing == AttemptOutcome.Goal || crossing == AttemptOutcome.Invalid)
            {
                return crossing;
            }

            if (contacts != null && contacts.GoalkeeperTouched)
            {
                return AttemptOutcome.BlockedThenOut;
            }

            if (contacts != null && contacts.GoalFrameTouched)
            {
                return AttemptOutcome.PostOrCrossbarOut;
            }

            return crossing;
        }

        public static AttemptOutcome ResolveSafeExit(
            Vector3 localPosition,
            Vector3 dangerMaximum,
            ContactHistory contacts)
        {
            if (contacts != null && contacts.GoalkeeperTouched)
            {
                return AttemptOutcome.BlockedThenOut;
            }

            if (contacts != null && contacts.GoalFrameTouched)
            {
                return AttemptOutcome.PostOrCrossbarOut;
            }

            return localPosition.y > dangerMaximum.y
                ? AttemptOutcome.MissHigh
                : AttemptOutcome.MissWide;
        }

        public static bool TryResolveSave(
            ContactHistory contacts,
            float attemptTime,
            float speed,
            float deltaTime,
            EnvironmentKernelConfig configuration,
            ref float restTime)
        {
            if (contacts == null || !contacts.GoalkeeperTouched)
            {
                restTime = 0f;
                return false;
            }

            if (speed <= configuration.RestSpeedThreshold)
            {
                restTime += deltaTime;
                if (restTime >= configuration.RestDwellTime)
                {
                    return true;
                }
            }
            else
            {
                restTime = 0f;
            }

            return attemptTime - contacts.LastGoalkeeperContactTime >=
                configuration.PostContactSafetyHorizon;
        }

        public static bool TryResolveFrameContact(
            ContactHistory contacts,
            float attemptTime,
            EnvironmentKernelConfig configuration)
        {
            return contacts != null &&
                contacts.GoalFrameTouched &&
                !contacts.GoalkeeperTouched &&
                attemptTime - contacts.LastGoalFrameContactTime >=
                    configuration.PostContactSafetyHorizon;
        }

        public static AttemptOutcome ResolveAttemptLimit(
            Vector3 ballLocalPosition,
            Vector3 ballLocalVelocity,
            ContactHistory contacts,
            EnvironmentKernelConfig configuration)
        {
            if (contacts != null && contacts.GoalkeeperTouched)
            {
                return AttemptOutcome.Saved;
            }

            if (contacts != null && contacts.GoalFrameTouched)
            {
                return AttemptOutcome.PostOrCrossbarOut;
            }

            var isStillThreatening =
                ballLocalPosition.z > 0f &&
                ballLocalVelocity.z < -configuration.RestSpeedThreshold;
            if (isStillThreatening)
            {
                return AttemptOutcome.Timeout;
            }

            return ballLocalPosition.y + KernelConstants.BallRadius >
                KernelConstants.CrossbarLowerEdge
                ? AttemptOutcome.MissHigh
                : AttemptOutcome.MissWide;
        }
    }
}
