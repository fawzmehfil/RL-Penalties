using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperControlTrainingContracts
    {
        public const string RewardSpecId =
            "goalkeeper-control-training-reach-v1";
        public const float GloveSaveReward = 1f;
        public const float OtherSaveReward = 0.8f;
        public const float GoalReward = -1f;
        public const float LessonZeroAutoCommitBallFlightTime = 0.10f;

        public static float TrainingReward(
            AttemptResult result,
            bool reachTrainingEnabled)
        {
            if (!reachTrainingEnabled)
            {
                return GoalkeeperTrainingContracts.SparseReward(result.Outcome);
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

        public static GoalkeeperControlCommand ApplyScaffold(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask,
            bool reachTrainingEnabled,
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
            if (shots == null)
            {
                return;
            }

            if (!reachTrainingEnabled)
            {
                shots.ReachFocusProbability = 0f;
                return;
            }

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
