using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperControlTrainingContracts
    {
        public const string RewardSpecId =
            "goalkeeper-control-training-reach-v1";
        public const string RewardSpecV2Id =
            "goalkeeper-control-training-reach-v2";
        public const string RewardSpecV3Id =
            "goalkeeper-control-training-reach-v3";
        public const string RewardSpecV4Id =
            "goalkeeper-control-training-reach-v4";
        public const string RewardSpecV5Id =
            "goalkeeper-control-result-v2";
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
        public const float V3MinimumObservedBallFlightTime = 0.08f;
        public const float V3PreferredMaximumTimeToPlane = 0.68f;
        public const float V3GloveFirstSaveReward = 1f;
        public const float V3GloveSaveReward = 0.85f;
        public const float V3ArmSaveReward = 0.55f;
        public const float V3BodySaveReward = 0.25f;
        public const float V3MaximumGoalProximityCredit = 0.05f;
        public const float V3ImmediateCommitPenalty = 0.15f;
        public const float V3EarlyCommitPenalty = 0.10f;
        public const float V3MaximumAimErrorPenalty = 0.15f;
        public const float V3AimErrorPenaltyDistance = 2f;
        public const float V4MinimumTimeToPlane = 0.35f;
        public const float V4MaximumTimeToPlane = 0.68f;
        public const float V4ImmediateCommitPenalty = 0.30f;
        public const float V4PrematureCommitPenalty = 0.22f;
        public const float V4LateCommitPenalty = 0.15f;
        public const float V4TimelyCommitBonus = 0.08f;
        public const float V4MaximumAimErrorPenalty = 0.25f;
        public const float V4MaximumReachShortfallPenalty = 0.25f;
        public const float V4GloveFirstSaveReward = 1f;
        public const float V4GloveSaveReward = 0.85f;
        public const float V4ArmSaveReward = 0.60f;
        public const float V4BodySaveReward = 0.35f;
        public const float V4PrematureSaveRewardCeiling = 0f;
        public const float V4LateSaveRewardCeiling = 0.15f;
        public const float V5GloveFirstSaveReward = 1f;
        public const float V5GloveSaveReward = 0.9f;
        public const float V5ArmSaveReward = 0.75f;
        public const float V5BodySaveReward = 0.5f;
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

            if (reachTrainingVersion >= 5)
            {
                return TrainingRewardV5(result);
            }

            if (reachTrainingVersion == 4)
            {
                return TrainingRewardV4(result);
            }

            if (reachTrainingVersion == 3)
            {
                return TrainingRewardV3(result);
            }

            if (reachTrainingVersion == 2)
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

        public static bool TryEstimateVisibleGoalPlaneAim(
            Vector3 ballLocalPosition,
            Vector3 ballLocalVelocity,
            Vector3 gravity,
            out float timeToPlane,
            out Vector2 aim)
        {
            timeToPlane = EstimateVisibleTimeToGoalPlane(
                ballLocalPosition,
                ballLocalVelocity);
            if (timeToPlane < 0f || !KernelMath.IsFinite(gravity))
            {
                aim = Vector2.zero;
                return false;
            }

            var predicted = new Vector2(
                ballLocalPosition.x +
                    ballLocalVelocity.x * timeToPlane +
                    0.5f * gravity.x * timeToPlane * timeToPlane,
                ballLocalPosition.y +
                    ballLocalVelocity.y * timeToPlane +
                    0.5f * gravity.y * timeToPlane * timeToPlane);
            if (!KernelMath.IsFinite(predicted.x) ||
                !KernelMath.IsFinite(predicted.y))
            {
                aim = Vector2.zero;
                return false;
            }

            aim = GoalkeeperControlSpace.LocalToAim(predicted);
            return true;
        }

        public static float VisibleAimErrorMeters(
            Vector2 policyAim,
            Vector2 visiblePredictedAim)
        {
            return Vector2.Distance(
                GoalkeeperControlSpace.AimToLocal(policyAim.x, policyAim.y),
                GoalkeeperControlSpace.AimToLocal(
                    visiblePredictedAim.x,
                    visiblePredictedAim.y));
        }

        public static bool IsV3PreferredCommitOpportunity(
            GoalkeeperControlDecisionContext context)
        {
            return context.BallFlightTime >=
                    V3MinimumObservedBallFlightTime &&
                context.VisibleTimeToGoalPlane >= 0f &&
                context.VisibleTimeToGoalPlane <=
                    V3PreferredMaximumTimeToPlane;
        }

        public static bool IsV3PrematureCommit(
            GoalkeeperControlDecisionContext context)
        {
            return context.BallFlightTime <
                    V3MinimumObservedBallFlightTime ||
                context.VisibleTimeToGoalPlane < 0f ||
                context.VisibleTimeToGoalPlane >
                    V3PreferredMaximumTimeToPlane;
        }

        public static bool IsV4TimelyCommitOpportunity(
            GoalkeeperControlDecisionContext context)
        {
            return context.BallFlightTime >=
                    V3MinimumObservedBallFlightTime &&
                context.HasVisibleGoalPlanePrediction &&
                context.VisibleTimeToGoalPlane >=
                    V4MinimumTimeToPlane &&
                context.VisibleTimeToGoalPlane <=
                    V4MaximumTimeToPlane;
        }

        public static GoalkeeperControlDecisionCredit EvaluateDecisionCreditV4(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            float goalkeeperRootLocalX)
        {
            if (!command.Commit)
            {
                return new GoalkeeperControlDecisionCredit(
                    0f,
                    false,
                    false,
                    false,
                    false,
                    -1f,
                    0f,
                    0f);
            }

            var immediate =
                context.BallFlightTime < V3MinimumObservedBallFlightTime;
            var predictionAvailable =
                context.HasVisibleGoalPlanePrediction &&
                context.VisibleTimeToGoalPlane >= 0f;
            var premature =
                immediate ||
                !predictionAvailable ||
                context.VisibleTimeToGoalPlane > V4MaximumTimeToPlane;
            var late =
                predictionAvailable &&
                !premature &&
                context.VisibleTimeToGoalPlane < V4MinimumTimeToPlane;
            var timely = predictionAvailable && !premature && !late;

            var reward = immediate
                ? -V4ImmediateCommitPenalty
                : premature
                    ? -V4PrematureCommitPenalty
                    : late
                        ? -V4LateCommitPenalty
                        : V4TimelyCommitBonus;
            var visibleAimError = predictionAvailable
                ? VisibleAimErrorMeters(
                    new Vector2(command.AimX, command.AimY),
                    context.VisiblePredictedAim)
                : -1f;
            if (visibleAimError >= 0f)
            {
                reward -=
                    V4MaximumAimErrorPenalty *
                    Mathf.Clamp01(
                        visibleAimError / V3AimErrorPenaltyDistance);
            }

            var desiredReach01 = predictionAvailable
                ? DesiredReachV4(
                    context.VisiblePredictedAim,
                    goalkeeperRootLocalX)
                : 0f;
            var reachShortfall = Mathf.Max(
                0f,
                desiredReach01 - command.Reach01);
            reward -=
                V4MaximumReachShortfallPenalty *
                reachShortfall;
            return new GoalkeeperControlDecisionCredit(
                reward,
                immediate,
                premature,
                late,
                timely,
                visibleAimError,
                desiredReach01,
                reachShortfall);
        }

        public static float DesiredReachV4(
            Vector2 visiblePredictedAim,
            float goalkeeperRootLocalX)
        {
            var target = GoalkeeperControlSpace.AimToLocal(
                visiblePredictedAim.x,
                visiblePredictedAim.y);
            var horizontalDemand = Mathf.InverseLerp(
                0.35f,
                1.35f,
                Mathf.Abs(target.x - goalkeeperRootLocalX));
            var verticalDemand = Mathf.InverseLerp(
                1.15f,
                2.05f,
                target.y);
            return Mathf.Clamp01(Mathf.Max(
                horizontalDemand,
                verticalDemand));
        }

        public static GoalkeeperControlActionMask ApplyCommitGuard(
            GoalkeeperControlActionMask actionMask,
            GoalkeeperControlDecisionContext context,
            bool reachTrainingEnabled,
            int reachTrainingVersion,
            int lesson)
        {
            if (!actionMask.CanCommit || !reachTrainingEnabled)
            {
                return actionMask;
            }

            var clampedLesson = Mathf.Clamp(lesson, 0, 4);
            if (reachTrainingVersion >= 4)
            {
                return actionMask;
            }

            if (reachTrainingVersion == 3)
            {
                if (clampedLesson >= 4)
                {
                    return actionMask;
                }

                var v3MaximumTimeToPlane =
                    V3MaximumTimeToPlaneForLesson(clampedLesson);
                var minimumBallFlightTime =
                    clampedLesson <= 2
                        ? V3MinimumObservedBallFlightTime
                        : V2ImmediateCommitBallFlightTime;
                return new GoalkeeperControlActionMask(
                    context.BallFlightTime >= minimumBallFlightTime &&
                    context.VisibleTimeToGoalPlane >= 0f &&
                    context.VisibleTimeToGoalPlane <= v3MaximumTimeToPlane);
            }

            if (reachTrainingVersion < 2)
            {
                return actionMask;
            }

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
            if (reachTrainingVersion >= 4)
            {
                return command;
            }

            if (reachTrainingVersion == 3)
            {
                return ApplyScaffoldV3(
                    command,
                    context,
                    actionMask,
                    clampedLesson,
                    out autoCommitApplied,
                    out reachFloorApplied);
            }

            if (reachTrainingVersion == 2)
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

            if (reachTrainingVersion >= 5)
            {
                ApplyReachFocusLessonV5(shots, lesson);
                return;
            }

            if (reachTrainingVersion == 4)
            {
                ApplyReachFocusLessonV4(shots, lesson);
                return;
            }

            if (reachTrainingVersion == 3)
            {
                ApplyReachFocusLessonV3(shots, lesson);
                return;
            }

            if (reachTrainingVersion == 2)
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

        private static float TrainingRewardV3(AttemptResult result)
        {
            if (!IsSave(result.Outcome) &&
                result.Outcome != AttemptOutcome.Goal)
            {
                return 0f;
            }

            float reward;
            if (result.Outcome == AttemptOutcome.Goal)
            {
                var distance = result.MinimumGloveBallDistance;
                var proximity = distance < 0f
                    ? 0f
                    : 1f - Mathf.Clamp01(
                        distance / V2GoalProximityDistance);
                reward =
                    GoalReward +
                    V3MaximumGoalProximityCredit * proximity;
            }
            else
            {
                switch (result.FirstGoalkeeperContactPart)
                {
                    case GoalkeeperContactPart.LeftGlove:
                    case GoalkeeperContactPart.RightGlove:
                        reward = V3GloveFirstSaveReward;
                        break;
                    case GoalkeeperContactPart.Arm:
                        reward = V3ArmSaveReward;
                        break;
                    default:
                        reward = result.GloveContact
                            ? V3GloveSaveReward
                            : V3BodySaveReward;
                        break;
                }
            }

            if (result.HasSaveCommitment)
            {
                if (result.FirstCommitWasImmediate)
                {
                    reward -= V3ImmediateCommitPenalty;
                }

                if (result.FirstCommitVisibleTimeToGoalPlane >
                    V3PreferredMaximumTimeToPlane)
                {
                    reward -= V3EarlyCommitPenalty;
                }

                if (result.FirstCommitVisibleAimError >= 0f)
                {
                    reward -=
                        V3MaximumAimErrorPenalty *
                        Mathf.Clamp01(
                            result.FirstCommitVisibleAimError /
                            V3AimErrorPenaltyDistance);
                }
            }

            return reward;
        }

        private static float TrainingRewardV4(AttemptResult result)
        {
            if (!IsSave(result.Outcome) &&
                result.Outcome != AttemptOutcome.Goal)
            {
                return 0f;
            }

            if (result.Outcome == AttemptOutcome.Goal)
            {
                var distance = result.MinimumGloveBallDistance;
                var proximity = distance < 0f
                    ? 0f
                    : 1f - Mathf.Clamp01(
                        distance / V2GoalProximityDistance);
                return GoalReward +
                    V3MaximumGoalProximityCredit * proximity;
            }

            float reward;
            switch (result.FirstGoalkeeperContactPart)
            {
                case GoalkeeperContactPart.LeftGlove:
                case GoalkeeperContactPart.RightGlove:
                    reward = V4GloveFirstSaveReward;
                    break;
                case GoalkeeperContactPart.Arm:
                    reward = V4ArmSaveReward;
                    break;
                default:
                    reward = result.GloveContact
                        ? V4GloveSaveReward
                        : V4BodySaveReward;
                    break;
            }

            if (result.HasSaveCommitment &&
                result.FirstCommitWasPremature)
            {
                return Mathf.Min(
                    reward,
                    V4PrematureSaveRewardCeiling);
            }

            if (result.HasSaveCommitment &&
                result.FirstCommitWasLate)
            {
                return Mathf.Min(
                    reward,
                    V4LateSaveRewardCeiling);
            }

            return reward;
        }

        private static float TrainingRewardV5(AttemptResult result)
        {
            if (result.Outcome == AttemptOutcome.Goal)
            {
                return GoalReward;
            }

            if (!IsSave(result.Outcome))
            {
                return 0f;
            }

            switch (result.FirstGoalkeeperContactPart)
            {
                case GoalkeeperContactPart.LeftGlove:
                case GoalkeeperContactPart.RightGlove:
                    return V5GloveFirstSaveReward;
                case GoalkeeperContactPart.Arm:
                    return V5ArmSaveReward;
                default:
                    return result.GloveContact
                        ? V5GloveSaveReward
                        : V5BodySaveReward;
            }
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

        private static GoalkeeperControlCommand ApplyScaffoldV3(
            GoalkeeperControlCommand command,
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask,
            int lesson,
            out bool autoCommitApplied,
            out bool reachFloorApplied)
        {
            autoCommitApplied = false;
            reachFloorApplied = false;
            var minimumReach01 =
                lesson == 0
                    ? 1f
                    : lesson == 1
                        ? 0.75f
                        : lesson == 2
                            ? 0.35f
                            : 0f;
            var minimumReachAction = minimumReach01 * 2f - 1f;
            if (command.Reach < minimumReachAction)
            {
                command.Reach = minimumReachAction;
                reachFloorApplied = true;
            }

            var guidanceWeight = V3AimGuidanceWeight(lesson);
            if (guidanceWeight > 0f &&
                context.HasVisibleGoalPlanePrediction)
            {
                command.AimX = Mathf.Lerp(
                    command.AimX,
                    context.VisiblePredictedAim.x,
                    guidanceWeight);
                command.AimY = Mathf.Lerp(
                    command.AimY,
                    context.VisiblePredictedAim.y,
                    guidanceWeight);
            }

            if (lesson == 0 &&
                actionMask.CanCommit &&
                IsV3PreferredCommitOpportunity(context) &&
                !command.Commit)
            {
                command.Commit = true;
                autoCommitApplied = true;
            }

            return command;
        }

        public static float V3AimGuidanceWeight(int lesson)
        {
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    return 1f;
                case 1:
                    return 0.65f;
                case 2:
                    return 0.25f;
                default:
                    return 0f;
            }
        }

        public static float V3MaximumTimeToPlaneForLesson(int lesson)
        {
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    return 0.56f;
                case 1:
                    return 0.60f;
                case 2:
                    return 0.64f;
                default:
                    return V3PreferredMaximumTimeToPlane;
            }
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

        private static void ApplyReachFocusLessonV3(
            ShotDistributionConfig shots,
            int lesson)
        {
            shots.ReachFocusBalancedHeightBands = true;
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    SetFocus(shots, 1f, 0.25f, 0.60f, 0.02f, 0.98f);
                    break;
                case 1:
                    SetFocus(shots, 0.90f, 0.30f, 0.75f, 0.02f, 0.98f);
                    break;
                case 2:
                    SetFocus(shots, 0.75f, 0.35f, 0.90f, 0.02f, 0.98f);
                    break;
                case 3:
                    SetFocus(shots, 0.60f, 0.40f, 0.95f, 0.02f, 0.98f);
                    break;
                default:
                    SetFocus(shots, 0.45f, 0.45f, 0.95f, 0.02f, 0.98f);
                    break;
            }
        }

        private static void ApplyReachFocusLessonV4(
            ShotDistributionConfig shots,
            int lesson)
        {
            shots.ReachFocusBalancedHeightBands = true;
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    SetFocus(shots, 0.35f, 0.25f, 0.55f, 0.12f, 0.88f);
                    break;
                case 1:
                    SetFocus(shots, 0.45f, 0.30f, 0.70f, 0.08f, 0.92f);
                    break;
                case 2:
                    SetFocus(shots, 0.50f, 0.35f, 0.85f, 0.04f, 0.96f);
                    break;
                case 3:
                    SetFocus(shots, 0.45f, 0.40f, 0.95f, 0.02f, 0.98f);
                    break;
                default:
                    SetFocus(shots, 0.35f, 0.45f, 0.95f, 0.02f, 0.98f);
                    break;
            }
        }

        private static void ApplyReachFocusLessonV5(
            ShotDistributionConfig shots,
            int lesson)
        {
            shots.ReachFocusBalancedHeightBands = true;
            switch (Mathf.Clamp(lesson, 0, 4))
            {
                case 0:
                    SetFocus(shots, 0f, 0.25f, 0.55f, 0.12f, 0.88f);
                    break;
                case 1:
                    SetFocus(shots, 0.20f, 0.30f, 0.70f, 0.08f, 0.92f);
                    break;
                case 2:
                    SetFocus(shots, 0.30f, 0.35f, 0.85f, 0.04f, 0.96f);
                    break;
                case 3:
                    SetFocus(shots, 0.15f, 0.40f, 0.95f, 0.02f, 0.98f);
                    break;
                default:
                    shots.ReachFocusBalancedHeightBands = false;
                    SetFocus(shots, 0f, 0.45f, 0.95f, 0.02f, 0.98f);
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
