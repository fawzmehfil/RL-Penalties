using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class ManualGoalkeeperControlSourceV1 :
        GoalkeeperControlSourceBehaviourV1
    {
        [SerializeField]
        private PenaltyAreaController controller;

        [SerializeField]
        private float aimSpeed = 0.9f;

        private float aimX;
        private float aimY;
        private bool bufferedCommit;

        public PenaltyAreaController Controller
        {
            get => controller;
            set => controller = value;
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<PenaltyAreaController>();
            }
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                aimX -= aimSpeed * Time.unscaledDeltaTime;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                aimX += aimSpeed * Time.unscaledDeltaTime;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                aimY -= aimSpeed * Time.unscaledDeltaTime;
            }

            if (Input.GetKey(KeyCode.UpArrow))
            {
                aimY += aimSpeed * Time.unscaledDeltaTime;
            }

            aimX = Mathf.Clamp(aimX, -1f, 1f);
            aimY = Mathf.Clamp(aimY, -1f, 1f);
            if (Input.GetKeyDown(KeyCode.Space))
            {
                bufferedCommit = true;
            }
#endif
        }

        public override GoalkeeperControlCommand DecideControl(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask)
        {
            var command = GoalkeeperControlCommand.Neutral;
            command.AimX = aimX;
            command.AimY = aimY;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.A))
            {
                command.MoveX = -1f;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                command.MoveX = 1f;
            }

            command.Reach =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift) ||
                Input.GetMouseButton(0)
                    ? 1f
                    : -1f;
#endif
            command.Commit = bufferedCommit && actionMask.CanCommit;
            if (command.Commit)
            {
                bufferedCommit = false;
            }

            return command;
        }

        public override void OnAttemptStarted(long attemptId)
        {
            aimX = 0f;
            aimY = 0f;
            bufferedCommit = false;
        }
    }
}
