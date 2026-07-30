using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperControlTrainingContracts
    {
        public const string RewardSpecId =
            "goalkeeper-control-training-reach-v1";
        public const string RewardSpecV2Id =
            "goalkeeper-control-training-reach-v2";
        public const float GloveSaveReward = 1f;
        public const float OtherSaveReward = 0.8f;
        public const float GoalReward = -1f;
        public const float LessonZeroAutoCommitBallFlightTime = 0.10f;
        public const float V2OtherSaveReward = 0.25f;
        public const float V2GloveGoalReward = -0.75f;
        public const float V2MaximumGoalProximityCredit = 0.15f;
        public const float V2GoalProximityDistance = 0.75f;
        public const float V2GuidedCommitTimeToPlane = 0.62f;
        public const float V2CommitGuardTimeToPlane = 0.72f;
        public const float V2CommitWindowMinimum = 0.35f;
        public const float V2ImmediateCommitBallFlightTime = 0.06f;
        public const float MaximumVisibleTimeToGoalPlane = 1.5f;

        public static float TrainingReward(
            AttemptResult result,
            bool reachTrainingEnabled)
        {
            return TrainingReward(result, reachTrainingEnabled, 1);
        }

        public static float TrainingReward(
            AttemptResult result,
            bool reachTrainingEnabled,
            int reachTrainingVersion)
        {
            if (!reachTrainingEnabled)
            {
                return GoalkeeperTrainingContracts.SparseReward(result.Outcome);
            }

            if (reachTrainingVersion >= 2)
            {
                return TrainingRewardV2(result);
            }

            if (result.Outcome == AttemptOutcome.Goal)
            {
                return GoalReward;
            }

            if (!IsSave(result.Outcome))
            {
                return 0f;
            }

            return result.GloveContact
                ? GloveSaveReward
                : OtherSaveReward;
        }

        public static float EstimateVisibleTimeToGoalPlane(
            Vector3 ballLocalPosition,
            Vector3 ballLocalVelocity)
        {
            if (!KernelMath.IsFinite(ballLocalPosition) ||
                !KernelMath.IsFinite(ballLocalVelocity) ||
                ballLocalVelocity.z >= -0.1f)
            {
                return -1f;
            }

            var time = -ballLocalPosition.z / ballLocalVelocity.z;
            return KernelMath.IsFinite(time) && time >= 0f
                ? Mathf.Clamp(time, 0f, MaximumVisibleTimeToGoalPlane)
                : -1f;
        }

        public static GoalkeeperControlActionMask ApplyCommitGuard(
            GoalkeeperControlActionMask actionMask,
            GoalkeeperControlDecisionContext context,
            bool reachTrainingEnabled,
            int reachTrainingVersion,
            int lesson)
        {
            if (!actionMask.CanCommit ||
                !reachTrainingEnabled ||
                reachTrainingVersion < 2)
            {
                return actionMask;
            }

            var clampedLesson = Mathf.Clamp(lesson, 0, 4);
            if (clampedLesson >= 3)
            {
                return actionMask;
            }

            var maximumTimeToPlane = clampedLesson == 0
                ? V2GuidedCommitTimeToPlane
                : V2CommitGuardTimeToPlane;
            return new GoalkeeperControlActionMask(
                context.VisibleTimeToGoalPlane >= 0f &&
                context.VisibleTimeToGoalPlane <= maximumTimeToPlane);
        }

        public static GoalkeeperControlCommand ApplyScaffold(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask,
            bool reachTrainingEnabled,
            int lesson,
            out bool autoCommitApplied,
            out bool reachFloorApplied)
        {
            return ApplyScaffold(
                command,
                context,
                actionMask,
                reachTrainingEnabled,
                1,
                lesson,
                out autoCommitApplied,
                out reachFloorApplied);
        }

        public static GoalkeeperControlCommand ApplyScaffold(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask,
            bool reachTrainingEnabled,
            int reachTrainingVersion,
            int lesson,
            out bool autoCommitApplied,
            out bool reachFloorApplied)
        {
            autoCommitApplied = false;
            reachFloorApplied = false;
            if (!reachTrainingEnabled)
            {
                return command;
            }

            var clampedLesson = Mathf.Clamp(lesson, 0, 4);
            if (reachTrainingVersion >= 2)
            {
                return ApplyScaffoldV2(
                    command,
                    context,
                    actionMask,
                    clampedLesson,
                    out autoCommitApplied,
                    out reachFloorApplied);
            }

            var minimumReachAction = clampedLesson == 0
                ? 1f
                : clampedLesson == 1
                    ? 0.5f
                    : -1f;
            if (command.Reach < minimumReachAction)
            {
                command.Reach = minimumReachAction;
                reachFloorApplied = true;
            }

            if (clampedLesson == 0 &&
                actionMask.CanCommit &&
                context.BallFlightTime >= LessonZeroAutoCommitBallFlightTime &&
                !command.Commit)
            {
                command.Commit = true;
                autoCommitApplied = true;
            }

            return command;
        }

        public static void ApplyReachFocusLesson(
            ShotDistributionConfig shots,
            bool reachTrainingEnabled,
            int lesson)
        {
            ApplyReachFocusLesson(shots, reachTrainingEnabled, 1, lesson);
        }

        public static void ApplyReachFocusLesson(
            ShotDistributionConfig shots,
            bool reachTrainingEnabled,
            int reachTrainingVersion,
            int lesson)
        {
            if (shots == null)
            {
                return;
            }

            if (!reachTrainingEnabled)
            {
                shots.ReachFocusProbability = 0f;
                shots.ReachFocusBalancedHeightBands = false;
                return;
            }

            if (reachTrainingVersion >= 2)
            {
                ApplyReachFocusLessonV2(shots, lesson);
                return;
            }

            shots.ReachFocusBalancedHeightBands = false;
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    SetFocus(shots, 1f, 0.25f, 0.55f, 0.45f, 0.75f);
                    break;
                case 1:
                    SetFocus(shots, 0.85f, 0.35f, 0.75f, 0.50f, 0.90f);
                    break;
                case 2:
                    SetFocus(shots, 0.65f, 0.45f, 0.90f, 0.55f, 0.98f);
                    break;
                case 3:
                    SetFocus(shots, 0.35f, 0.45f, 0.95f, 0.55f, 0.98f);
                    break;
                default:
                    SetFocus(shots, 0.20f, 0.45f, 0.95f, 0.55f, 0.98f);
                    break;
            }
        }

        public static bool IsSave(AttemptOutcome outcome)
        {
            return outcome == AttemptOutcome.Saved ||
                   outcome == AttemptOutcome.BlockedThenOut;
        }

        private static float TrainingRewardV2(AttemptResult result)
        {
            if (result.Outcome == AttemptOutcome.Goal)
            {
                if (result.GloveContact)
                {
                    return V2GloveGoalReward;
                }

                var distance = result.MinimumGloveBallDistance;
                var proximity = distance < 0f
                    ? 0f
                    : 1f - Mathf.Clamp01(distance / V2GoalProximityDistance);
                return GoalReward +
                    V2MaximumGoalProximityCredit * proximity;
            }

            if (!IsSave(result.Outcome))
            {
                return 0f;
            }

            return result.GloveContact
                ? GloveSaveReward
                : V2OtherSaveReward;
        }

        private static GoalkeeperControlCommand ApplyScaffoldV2(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask,
            int lesson,
            out bool autoCommitApplied,
            out bool reachFloorApplied)
        {
            autoCommitApplied = false;
            reachFloorApplied = false;
            var minimumReach01 = lesson == 0
                ? 1f
                : lesson == 1
                    ? 0.75f
                    : 0f;
            var minimumReachAction = minimumReach01 * 2f - 1f;
            if (command.Reach < minimumReachAction)
            {
                command.Reach = minimumReachAction;
                reachFloorApplied = true;
            }

            if (lesson == 0 &&
                actionMask.CanCommit &&
                context.VisibleTimeToGoalPlane >= 0f &&
                context.VisibleTimeToGoalPlane <=
                    V2GuidedCommitTimeToPlane &&
                !command.Commit)
            {
                command.Commit = true;
                autoCommitApplied = true;
            }

            return command;
        }

        private static void ApplyReachFocusLessonV2(
            ShotDistributionConfig shots,
            int lesson)
        {
            shots.ReachFocusBalancedHeightBands = true;
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    SetFocus(shots, 1f, 0.25f, 0.60f, 0.08f, 0.92f);
                    break;
                case 1:
                    SetFocus(shots, 0.85f, 0.35f, 0.75f, 0.05f, 0.95f);
                    break;
                case 2:
                    SetFocus(shots, 0.65f, 0.40f, 0.90f, 0.03f, 0.97f);
                    break;
                case 3:
                    SetFocus(shots, 0.40f, 0.45f, 0.95f, 0.02f, 0.98f);
                    break;
                default:
                    SetFocus(shots, 0.20f, 0.45f, 0.95f, 0.02f, 0.98f);
                    break;
            }
        }

        private static void SetFocus(
            ShotDistributionConfig shots,
            float probability,
            float minimumAbsoluteX,
            float maximumAbsoluteX,
            float minimumY,
            float maximumY)
        {
            shots.ReachFocusProbability = probability;
            shots.ReachFocusMinimumAbsoluteXNormalized = minimumAbsoluteX;
            shots.ReachFocusMaximumAbsoluteXNormalized = maximumAbsoluteX;
            shots.ReachFocusMinimumYNormalized = minimumY;
            shots.ReachFocusMaximumYNormalized = maximumY;
        }
    }
}
