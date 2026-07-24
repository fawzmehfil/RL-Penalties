using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GoalkeeperObservationProfile
    {
        TransportProbe = 0,
        StateV0 = 1,
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
            if (controller == null)
            {
                for (var index = 0; index < KernelConstants.GoalkeeperStateObservationSize; index++)
                {
                    add(0f);
                }

                return;
            }

            var ballPosition = controller.BallLocalPosition;
            var ballVelocity = controller.BallLocalVelocity;
            var angularVelocity = controller.BallAngularVelocity;

            AddVector(add, ballPosition, 5f, 4f, KernelConstants.PenaltyMarkDistance);
            AddVector(add, ballVelocity, 25f, 25f, 25f);
            AddVector(add, angularVelocity, 50f, 50f, 50f);
            add(Clamp(controller.GoalkeeperLocalX / 3.1f));
            add(Clamp(controller.GoalkeeperLateralVelocity / 3.5f));
            AddOneHot(add, (int)controller.GoalkeeperMotorState, 4);
            AddDiveSide(add, controller.GoalkeeperDiveAction);
            AddDiveHeight(add, controller.GoalkeeperDiveAction);
            add(Mathf.Clamp01(controller.AttemptTime / 4f));
            add(Mathf.Clamp01(controller.BallFlightTime / 1f));
            add(0f);
            add(0f);
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
