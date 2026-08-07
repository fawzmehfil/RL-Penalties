using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage9ShooterPresentationV1 : MonoBehaviour
    {
        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;

        private Quaternion leftLegRest;
        private Quaternion rightLegRest;
        private Quaternion leftArmRest;
        private Quaternion rightArmRest;
        private float poseStart = -1f;

        public void Configure(
            PenaltyAreaController areaController,
            Transform leftLegTransform,
            Transform rightLegTransform,
            Transform leftArmTransform,
            Transform rightArmTransform)
        {
            controller = areaController;
            leftLeg = leftLegTransform;
            rightLeg = rightLegTransform;
            leftArm = leftArmTransform;
            rightArm = rightArmTransform;
        }

        private void Awake()
        {
            leftLegRest = RotationOf(leftLeg);
            rightLegRest = RotationOf(rightLeg);
            leftArmRest = RotationOf(leftArm);
            rightArmRest = RotationOf(rightArm);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.ShotLaunched += OnShotLaunched;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.ShotLaunched -= OnShotLaunched;
            }
            ApplyPose(0f);
        }

        private void Update()
        {
            if (poseStart < 0f)
            {
                return;
            }
            var elapsed = Time.unscaledTime - poseStart;
            var strike = Mathf.Sin(Mathf.Clamp01(elapsed / 0.26f) * Mathf.PI);
            ApplyPose(strike);
            if (elapsed >= 0.38f)
            {
                poseStart = -1f;
                ApplyPose(0f);
            }
        }

        private void OnShotLaunched(PlayerShotLaunchEventV1 _) =>
            poseStart = Time.unscaledTime;

        private void ApplyPose(float amount)
        {
            SetRotation(leftLeg, leftLegRest, new Vector3(-38f, 0f, 0f), amount);
            SetRotation(rightLeg, rightLegRest, new Vector3(48f, 0f, 0f), amount);
            SetRotation(leftArm, leftArmRest, new Vector3(30f, 0f, 0f), amount);
            SetRotation(rightArm, rightArmRest, new Vector3(-26f, 0f, 0f), amount);
        }

        private static Quaternion RotationOf(Transform target) =>
            target == null ? Quaternion.identity : target.localRotation;

        private static void SetRotation(
            Transform target,
            Quaternion rest,
            Vector3 euler,
            float amount)
        {
            if (target != null)
            {
                target.localRotation = rest * Quaternion.Euler(euler * amount);
            }
        }
    }
}
