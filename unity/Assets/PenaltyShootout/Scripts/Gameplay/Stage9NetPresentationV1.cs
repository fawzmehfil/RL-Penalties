using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage9NetPresentationV1 : MonoBehaviour
    {
        [SerializeField] private PenaltyAreaController controller;

        private float rippleUntil;
        private float baselineScaleZ = 1f;

        public void Configure(PenaltyAreaController areaController) =>
            controller = areaController;

        private void Awake() => baselineScaleZ = transform.localScale.z;

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.AttemptCompleted += OnAttemptCompleted;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.AttemptCompleted -= OnAttemptCompleted;
            }
            ResetRipple();
        }

        private void Update()
        {
            if (rippleUntil <= 0f)
            {
                return;
            }
            var remaining = rippleUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                ResetRipple();
                return;
            }
            var pulse = Mathf.Sin(remaining * 40f) * remaining * 0.08f;
            var scale = transform.localScale;
            scale.z = baselineScaleZ + pulse;
            transform.localScale = scale;
        }

        private void OnAttemptCompleted(AttemptResult result)
        {
            if (result.Outcome == AttemptOutcome.Goal)
            {
                rippleUntil = Time.unscaledTime + 0.3f;
            }
            else
            {
                ResetRipple();
            }
        }

        private void ResetRipple()
        {
            rippleUntil = 0f;
            var scale = transform.localScale;
            scale.z = baselineScaleZ;
            transform.localScale = scale;
        }
    }
}
