using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GoalkeeperObservationProfile
    {
        TransportProbe = 0,
        StateV0 = 1,
        StatePartialV0 = 2,
    }

    [Serializable]
    public struct GoalkeeperVisibleStateSnapshot
    {
        public Vector3 BallLocalPosition;
        public Vector3 BallLocalVelocity;
        public Vector3 BallAngularVelocity;
        public float GoalkeeperLocalX;
        public float GoalkeeperLateralVelocity;
        public GoalkeeperMotorState MotorState;
        public GoalkeeperAction DiveAction;
        public float AttemptTime;
        public float BallFlightTime;
    }

    [Serializable]
    public struct GoalkeeperPartialObservationSettings
    {
        public int DelaySteps;
        public float BallPositionNoiseMeters;
        public float BallVelocityNoiseMetersPerSecond;
        public float GoalkeeperPositionNoiseMeters;
        public float DropoutProbability;
        public ulong Seed;
        public int ObservationIndex;

        public static GoalkeeperPartialObservationSettings None =>
            new GoalkeeperPartialObservationSettings();
    }

    public sealed class GoalkeeperObservationDelayBuffer
    {
        private readonly GoalkeeperVisibleStateSnapshot[] snapshots;
        private int nextWriteIndex;
        private int count;

        public GoalkeeperObservationDelayBuffer(int capacity = 128)
        {
            snapshots = new GoalkeeperVisibleStateSnapshot[Mathf.Max(1, capacity)];
        }

        public int Count => count;
        public int Capacity => snapshots.Length;

        public void Reset()
        {
            nextWriteIndex = 0;
            count = 0;
            Array.Clear(snapshots, 0, snapshots.Length);
        }

        public GoalkeeperVisibleStateSnapshot PushAndRead(
            GoalkeeperVisibleStateSnapshot snapshot,
            int delaySteps)
        {
            snapshots[nextWriteIndex] = snapshot;
            nextWriteIndex = (nextWriteIndex + 1) % snapshots.Length;
            count = Mathf.Min(count + 1, snapshots.Length);

            var clampedDelay = Mathf.Clamp(delaySteps, 0, count - 1);
            var readIndex = nextWriteIndex - 1 - clampedDelay;
            while (readIndex < 0)
            {
                readIndex += snapshots.Length;
            }

            return snapshots[readIndex];
        }
    }

    public static class GoalkeeperTrainingContracts
    {
        public static float SparseReward(AttemptOutcome outcome)
        {
            switch (outcome)
            {
                case AttemptOutcome.Saved:
                case AttemptOutcome.BlockedThenOut:
                    return 1f;
                case AttemptOutcome.Goal:
                    return -1f;
                default:
                    return 0f;
            }
        }

        public static void WriteStateV0(
            PenaltyAreaController controller,
            Action<float> add)
        {
            WriteVisibleState(CaptureVisibleState(controller), add);
        }

        public static void WriteStatePartialV0(
            GoalkeeperVisibleStateSnapshot snapshot,
            GoalkeeperPartialObservationSettings settings,
            Action<float> add)
        {
            WriteVisibleState(PerturbVisibleState(snapshot, settings), add);
        }

        public static GoalkeeperVisibleStateSnapshot CaptureVisibleState(
            PenaltyAreaController controller)
        {
            if (controller == null)
            {
                return new GoalkeeperVisibleStateSnapshot
                {
                    MotorState = GoalkeeperMotorState.Ready,
                    DiveAction = GoalkeeperAction.Hold,
                };
            }

            return new GoalkeeperVisibleStateSnapshot
            {
                BallLocalPosition = controller.BallLocalPosition,
                BallLocalVelocity = controller.BallLocalVelocity,
                BallAngularVelocity = controller.BallAngularVelocity,
                GoalkeeperLocalX = controller.GoalkeeperLocalX,
                GoalkeeperLateralVelocity = controller.GoalkeeperLateralVelocity,
                MotorState = controller.GoalkeeperMotorState,
                DiveAction = controller.GoalkeeperDiveAction,
                AttemptTime = controller.AttemptTime,
                BallFlightTime = controller.BallFlightTime,
            };
        }

        public static GoalkeeperVisibleStateSnapshot PerturbVisibleState(
            GoalkeeperVisibleStateSnapshot snapshot,
            GoalkeeperPartialObservationSettings settings)
        {
            var output = snapshot;
            var dropout = Mathf.Clamp01(settings.DropoutProbability);
            if (settings.BallPositionNoiseMeters <= 0f &&
                settings.BallVelocityNoiseMetersPerSecond <= 0f &&
                settings.GoalkeeperPositionNoiseMeters <= 0f &&
                dropout <= 0f)
            {
                return output;
            }

            var random = new Pcg32(
                Pcg32.DeriveSeed(
                    settings.Seed,
                    Mathf.Max(0, settings.DelaySteps),
                    settings.ObservationIndex));
            AddNoise(
                ref output.BallLocalPosition,
                Mathf.Max(0f, settings.BallPositionNoiseMeters),
                ref random);
            AddNoise(
                ref output.BallLocalVelocity,
                Mathf.Max(0f, settings.BallVelocityNoiseMetersPerSecond),
                ref random);
            output.GoalkeeperLocalX += random.Range(
                -Mathf.Max(0f, settings.GoalkeeperPositionNoiseMeters),
                Mathf.Max(0f, settings.GoalkeeperPositionNoiseMeters));

            if (dropout > 0f && random.NextFloat() < dropout)
            {
                output.BallLocalPosition = Vector3.zero;
                output.BallLocalVelocity = Vector3.zero;
                output.BallAngularVelocity = Vector3.zero;
            }

            return output;
        }

        public static string ObservationSpecIdForProfile(GoalkeeperObservationProfile profile)
        {
            return profile == GoalkeeperObservationProfile.StatePartialV0
                ? KernelConstants.GoalkeeperPartialObservationSpecId
                : profile == GoalkeeperObservationProfile.StateV0
                    ? KernelConstants.GoalkeeperStateObservationSpecId
                    : "transport-probe-v0";
        }

        private static void WriteVisibleState(
            GoalkeeperVisibleStateSnapshot snapshot,
            Action<float> add)
        {
            AddVector(
                add,
                snapshot.BallLocalPosition,
                5f,
                4f,
                KernelConstants.PenaltyMarkDistance);
            AddVector(add, snapshot.BallLocalVelocity, 25f, 25f, 25f);
            AddVector(add, snapshot.BallAngularVelocity, 50f, 50f, 50f);
            add(Clamp(snapshot.GoalkeeperLocalX / 3.1f));
            add(Clamp(snapshot.GoalkeeperLateralVelocity / 3.5f));
            AddOneHot(add, (int)snapshot.MotorState, 4);
            AddDiveSide(add, snapshot.DiveAction);
            AddDiveHeight(add, snapshot.DiveAction);
            add(Mathf.Clamp01(snapshot.AttemptTime / 4f));
            add(Mathf.Clamp01(snapshot.BallFlightTime / 1f));
            add(0f);
            add(0f);
        }

        private static void AddNoise(
            ref Vector3 vector,
            float amplitude,
            ref Pcg32 random)
        {
            if (amplitude <= 0f)
            {
                return;
            }

            vector.x += random.Range(-amplitude, amplitude);
            vector.y += random.Range(-amplitude, amplitude);
            vector.z += random.Range(-amplitude, amplitude);
        }

        private static void AddVector(
            Action<float> add,
            Vector3 vector,
            float xScale,
            float yScale,
            float zScale)
        {
            add(Clamp(vector.x / xScale));
            add(Clamp(vector.y / yScale));
            add(Clamp(vector.z / zScale));
        }

        private static void AddOneHot(Action<float> add, int index, int count)
        {
            for (var item = 0; item < count; item++)
            {
                add(item == index ? 1f : 0f);
            }
        }

        private static void AddDiveSide(Action<float> add, GoalkeeperAction action)
        {
            add(action >= GoalkeeperAction.DiveLeftLow &&
                action <= GoalkeeperAction.DiveLeftHigh ? 1f : 0f);
            add(action >= GoalkeeperAction.DiveRightLow &&
                action <= GoalkeeperAction.DiveRightHigh ? 1f : 0f);
        }

        private static void AddDiveHeight(Action<float> add, GoalkeeperAction action)
        {
            add(action == GoalkeeperAction.DiveLeftLow ||
                action == GoalkeeperAction.DiveRightLow ? 1f : 0f);
            add(action == GoalkeeperAction.DiveLeftMiddle ||
                action == GoalkeeperAction.DiveRightMiddle ? 1f : 0f);
            add(action == GoalkeeperAction.DiveLeftHigh ||
                action == GoalkeeperAction.DiveRightHigh ? 1f : 0f);
        }

        private static float Clamp(float value)
        {
            return Mathf.Clamp(value, -1f, 1f);
        }
    }
}
