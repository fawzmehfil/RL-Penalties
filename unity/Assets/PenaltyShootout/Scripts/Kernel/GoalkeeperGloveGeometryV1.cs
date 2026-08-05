using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GoalkeeperGloveGeometryV1 : MonoBehaviour
    {
        private const string PalmName = "GloveHandlingV1_Palm";
        private const string FingersName = "GloveHandlingV1_Fingers";

        [SerializeField] private GoalkeeperGloveHandlingConfigV1 configuration;
        [SerializeField] private GoalkeeperArmRigV1 armRig;
        [SerializeField] private bool handlingEnabled;

        public bool HandlingEnabled => handlingEnabled;

        public void Configure(
            GoalkeeperGloveHandlingConfigV1 handlingConfiguration,
            GoalkeeperArmRigV1 rig)
        {
            configuration = handlingConfiguration;
            armRig = rig;
            EnsureGeometry();
            SetHandlingEnabled(handlingEnabled);
        }

        private void Awake()
        {
            if (armRig == null)
            {
                armRig = GetComponent<GoalkeeperArmRigV1>();
            }
            EnsureGeometry();
            SetHandlingEnabled(handlingEnabled);
        }

        public void SetHandlingEnabled(bool enabled)
        {
            handlingEnabled = enabled;
            SetGloveMode(armRig == null ? null : armRig.LeftGlove, enabled);
            SetGloveMode(armRig == null ? null : armRig.RightGlove, enabled);
        }

        private void EnsureGeometry()
        {
            if (configuration == null || armRig == null)
            {
                return;
            }
            CreateCompound(armRig.LeftGlove, GoalkeeperContactPart.LeftGlove);
            CreateCompound(armRig.RightGlove, GoalkeeperContactPart.RightGlove);
        }

        private void CreateCompound(Transform glove, GoalkeeperContactPart part)
        {
            if (glove == null)
            {
                return;
            }
            var sourceMesh = glove.GetComponent<MeshFilter>();
            var sourceRenderer = glove.GetComponent<MeshRenderer>();
            CreateSurface(
                glove,
                PalmName,
                Vector3.zero,
                configuration.PalmSize,
                part,
                GloveContactRegionV1.Palm,
                sourceMesh,
                sourceRenderer);
            CreateSurface(
                glove,
                FingersName,
                new Vector3(0f, configuration.FingerOffsetY, 0f),
                configuration.FingerSize,
                part,
                GloveContactRegionV1.Fingers,
                sourceMesh,
                sourceRenderer);
        }

        private static void CreateSurface(
            Transform glove,
            string childName,
            Vector3 worldOffset,
            Vector3 worldSize,
            GoalkeeperContactPart part,
            GloveContactRegionV1 region,
            MeshFilter sourceMesh,
            MeshRenderer sourceRenderer)
        {
            var child = glove.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(glove, false);
            }
            var parentScale = Mathf.Max(1e-5f, glove.localScale.x);
            child.localPosition = worldOffset / parentScale;
            child.localRotation = Quaternion.identity;
            child.localScale = worldSize / parentScale;

            var meshFilter = child.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = child.gameObject.AddComponent<MeshFilter>();
            }
            meshFilter.sharedMesh = sourceMesh == null ? null : sourceMesh.sharedMesh;
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = child.gameObject.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterials = sourceRenderer == null
                ? new Material[0]
                : sourceRenderer.sharedMaterials;
            var collider = child.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = child.gameObject.AddComponent<BoxCollider>();
            }
            collider.size = Vector3.one;
            var marker = child.GetComponent<ContactMarker>();
            if (marker == null)
            {
                marker = child.gameObject.AddComponent<ContactMarker>();
            }
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = part;
            var surface = child.GetComponent<GloveContactSurfaceV1>();
            if (surface == null)
            {
                surface = child.gameObject.AddComponent<GloveContactSurfaceV1>();
            }
            surface.Configure(part, region);
        }

        private static void SetGloveMode(Transform glove, bool compoundEnabled)
        {
            if (glove == null)
            {
                return;
            }
            var legacyCollider = glove.GetComponent<SphereCollider>();
            var legacyRenderer = glove.GetComponent<MeshRenderer>();
            if (legacyCollider != null) legacyCollider.enabled = !compoundEnabled;
            if (legacyRenderer != null) legacyRenderer.enabled = !compoundEnabled;
            SetChildActive(glove, PalmName, compoundEnabled);
            SetChildActive(glove, FingersName, compoundEnabled);
        }

        private static void SetChildActive(
            Transform glove,
            string name,
            bool active)
        {
            var child = glove.Find(name);
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }
    }
}
