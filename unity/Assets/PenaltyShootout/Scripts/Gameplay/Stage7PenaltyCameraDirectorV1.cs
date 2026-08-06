using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [DefaultExecutionOrder(100)]
    public sealed class Stage7PenaltyCameraDirectorV1 : MonoBehaviour
    {
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private Transform arenaOrigin;
        [SerializeField] private float positionSharpness = 6f;
        [SerializeField] private float rotationSharpness = 8f;

        private Stage7GameplayStateV1 state;

        public void Configure(
            Camera camera,
            PenaltyAreaController areaController,
            Transform origin)
        {
            gameplayCamera = camera;
            controller = areaController;
            arenaOrigin = origin;
        }

        public void SetState(Stage7GameplayStateV1 next) => state = next;

        private void LateUpdate()
        {
            if (gameplayCamera == null || controller == null || arenaOrigin == null)
            {
                return;
            }

            var targetPosition = arenaOrigin.TransformPoint(
                state == Stage7GameplayStateV1.RunUp
                    ? new Vector3(0f, 1.45f, 14.1f)
                    : state == Stage7GameplayStateV1.BallInFlight ||
                      state == Stage7GameplayStateV1.Result
                        ? new Vector3(0f, 1.8f, 13.6f)
                        : new Vector3(0f, 1.35f, 14.6f));
            var lookLocal = new Vector3(0f, 1.25f, 0f);
            if (state == Stage7GameplayStateV1.BallInFlight ||
                state == Stage7GameplayStateV1.Result)
            {
                var ball = controller.BallLocalPosition;
                lookLocal = new Vector3(
                    ball.x * 0.18f,
                    Mathf.Clamp(ball.y, 0.8f, 1.75f),
                    Mathf.Clamp(ball.z * 0.18f, 0f, 2.2f));
            }

            var delta = Time.unscaledDeltaTime;
            var positionT = 1f - Mathf.Exp(-positionSharpness * delta);
            var rotationT = 1f - Mathf.Exp(-rotationSharpness * delta);
            gameplayCamera.transform.position = Vector3.Lerp(
                gameplayCamera.transform.position,
                targetPosition,
                positionT);
            var desired = Quaternion.LookRotation(
                arenaOrigin.TransformPoint(lookLocal) -
                gameplayCamera.transform.position,
                Vector3.up);
            gameplayCamera.transform.rotation = Quaternion.Slerp(
                gameplayCamera.transform.rotation,
                desired,
                rotationT);
            gameplayCamera.fieldOfView = Mathf.Lerp(
                gameplayCamera.fieldOfView,
                state == Stage7GameplayStateV1.BallInFlight ? 45f : 48f,
                positionT);
        }
    }
}
