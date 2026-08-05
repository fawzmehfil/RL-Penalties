using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GloveContactSurfaceV1 : MonoBehaviour
    {
        [SerializeField] private GoalkeeperContactPart goalkeeperPart;
        [SerializeField] private GloveContactRegionV1 region;
        [SerializeField] private Vector3 localPalmNormal = Vector3.forward;

        public GoalkeeperContactPart GoalkeeperPart => goalkeeperPart;
        public GloveContactRegionV1 Region => region;
        public Vector3 PalmNormalWorld =>
            transform.TransformDirection(localPalmNormal).normalized;

        public void Configure(
            GoalkeeperContactPart part,
            GloveContactRegionV1 contactRegion)
        {
            goalkeeperPart = part;
            region = contactRegion;
            localPalmNormal = Vector3.forward;
        }

        public float NormalizedContactExtent(Vector3 contactPointWorld)
        {
            var local = transform.InverseTransformPoint(contactPointWorld);
            return Mathf.Clamp01(Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y)) * 2f);
        }
    }
}
