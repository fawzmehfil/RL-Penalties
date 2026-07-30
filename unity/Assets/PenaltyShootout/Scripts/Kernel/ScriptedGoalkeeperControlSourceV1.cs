using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum ScriptedGoalkeeperControlPolicyV1
    {
        StandCenter = 0,
        RandomLegal = 1,
        ReactiveIntercept = 2,
        OracleReachBound = 3,
    }

    public sealed class ScriptedGoalkeeperControlSourceV1 :
        GoalkeeperControlSourceBehaviourV1
    {
        [SerializeField]
        private PenaltyAreaController controller;

        [SerializeField]
        private ScriptedGoalkeeperControlPolicyV1 policy =
            ScriptedGoalkeeperControlPolicyV1.ReactiveIntercept;

        [SerializeField]
        [Range(0.1f, 1f)]
        private float reactiveCommitHorizon = 0.62f;

        private Pcg32 random;

        public PenaltyAreaController Controller
        {
            get => controller;
            set => controller = value;
        }

        public ScriptedGoalkeeperControlPolicyV1 Policy
        {
            get => policy;
            set => policy = value;
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<PenaltyAreaController>();
            }
        }

        public override void OnAttemptStarted(long attemptId)
        {
            var arenaId = controller == null ? 0 : controller.ArenaId;
            random = new Pcg32(
                Pcg32.DeriveSeed(20260725UL, arenaId, attemptId));
        }

        public override GoalkeeperControlCommand DecideControl(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask)
        {
            switch (policy)
            {
                case ScriptedGoalkeeperControlPolicyV1.RandomLegal:
                    return RandomCommand(actionMask);
                case ScriptedGoalkeeperControlPolicyV1.ReactiveIntercept:
                    return InterceptCommand(actionMask, false);
                case ScriptedGoalkeeperControlPolicyV1.OracleReachBound:
                    return InterceptCommand(actionMask, true);
                default:
                    return GoalkeeperControlCommand.Neutral;
            }
        }

        private GoalkeeperControlCommand RandomCommand(
            GoalkeeperControlActionMask actionMask)
        {
            return new GoalkeeperControlCommand
            {
                MoveX = random.Range(-1f, 1f),
                AimX = random.Range(-1f, 1f),
                AimY = random.Range(-1f, 1f),
                Reach = random.Range(-1f, 1f),
                Commit = actionMask.CanCommit && random.NextFloat() < 0.12f,
            };
        }

        private GoalkeeperControlCommand InterceptCommand(
            GoalkeeperControlActionMask actionMask,
            bool usePrivilegedTarget)
        {
            if (controller == null)
            {
                return GoalkeeperControlCommand.Neutral;
            }

            if (!usePrivilegedTarget)
            {
                var gravity = controller.ArenaOrigin == null
                    ? Physics.gravity
                    : controller.ArenaOrigin.InverseTransformDirection(
                        Physics.gravity);
                return GoalkeeperReactiveControlPolicyV1.Decide(
                    controller.BallLocalPosition,
                    controller.BallLocalVelocity,
                    gravity,
                    controller.GoalkeeperControlLocalPosition.x,
                    actionMask,
                    reactiveCommitHorizon);
            }

            var target = new Vector2(
                controller.CurrentScenario.TargetLocal.x,
                controller.CurrentScenario.TargetLocal.y);
            var aim = GoalkeeperControlSpace.LocalToAim(target);
            var deltaX = target.x - controller.GoalkeeperControlLocalPosition.x;
            var command = new GoalkeeperControlCommand
            {
                MoveX = Mathf.Clamp(deltaX / 1.25f, -1f, 1f),
                AimX = aim.x,
                AimY = aim.y,
                Reach = 1f,
                Commit = false,
            };
            command.Commit = actionMask.CanCommit;
            return command;
        }

    }
}
