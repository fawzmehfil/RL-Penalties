using System;
using System.Collections.Generic;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    public sealed class GoalkeeperPresentationV1 : MonoBehaviour
    {
        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private Renderer[] keeperRenderers;
        [SerializeField] private Renderer leftGloveRenderer;
        [SerializeField] private Renderer rightGloveRenderer;
        [SerializeField] private ParticleSystem contactBurst;
        [SerializeField] private Color contactColor = new Color(1f, 0.82f, 0.23f, 1f);

        private Dictionary<Renderer, Color> baseColors;
        private MaterialPropertyBlock block;
        private Renderer flashingRenderer;
        private float flashUntil;
        private long lastContactAttempt = -1;
        private float lastContactTime = -1f;

        public void Configure(
            PenaltyAreaController areaController,
            Renderer[] renderers,
            Renderer leftGlove,
            Renderer rightGlove,
            ParticleSystem burst)
        {
            controller = areaController;
            keeperRenderers = renderers;
            leftGloveRenderer = leftGlove;
            rightGloveRenderer = rightGlove;
            contactBurst = burst;
        }

        private void Awake()
        {
            baseColors = new Dictionary<Renderer, Color>();
            block = new MaterialPropertyBlock();
            if (keeperRenderers == null)
            {
                return;
            }
            foreach (var renderer in keeperRenderers)
            {
                if (renderer == null || renderer.sharedMaterial == null)
                {
                    continue;
                }
                baseColors[renderer] = renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? renderer.sharedMaterial.GetColor("_BaseColor")
                    : Color.white;
            }
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.ContactRecorded += OnContactRecorded;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.ContactRecorded -= OnContactRecorded;
            }
            ClearFlash();
        }

        private void Update()
        {
            if (flashingRenderer != null && Time.unscaledTime >= flashUntil)
            {
                ClearFlash();
            }
        }

        private void OnContactRecorded(BallContactReplayEventV1 contact)
        {
            if (contact.Kind != ContactKind.Goalkeeper ||
                (lastContactAttempt == contact.AttemptId &&
                 Mathf.Abs(lastContactTime - contact.AttemptTime) < 0.0001f))
            {
                return;
            }
            lastContactAttempt = contact.AttemptId;
            lastContactTime = contact.AttemptTime;
            flashingRenderer = SelectRenderer(contact.GoalkeeperPart);
            if (flashingRenderer != null)
            {
                flashingRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", Color.Lerp(
                    baseColors.TryGetValue(flashingRenderer, out var color)
                        ? color
                        : Color.white,
                    contactColor,
                    0.55f));
                flashingRenderer.SetPropertyBlock(block);
                flashUntil = Time.unscaledTime + 0.1f;
            }
            if (contactBurst != null && contact.Kinematics.HasValue)
            {
                contactBurst.transform.position = contact.Kinematics.PointWorld;
                contactBurst.Emit(7);
            }
        }

        private Renderer SelectRenderer(GoalkeeperContactPart part)
        {
            if (part == GoalkeeperContactPart.LeftGlove)
            {
                return leftGloveRenderer;
            }
            if (part == GoalkeeperContactPart.RightGlove)
            {
                return rightGloveRenderer;
            }
            if (keeperRenderers == null || keeperRenderers.Length == 0)
            {
                return null;
            }
            var token = part == GoalkeeperContactPart.Leg
                ? "Leg"
                : part == GoalkeeperContactPart.Arm
                    ? "Arm"
                    : "Torso";
            foreach (var renderer in keeperRenderers)
            {
                if (renderer != null && renderer.name.IndexOf(
                        token,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return renderer;
                }
            }
            return keeperRenderers[0];
        }

        private void ClearFlash()
        {
            if (flashingRenderer != null)
            {
                flashingRenderer.SetPropertyBlock(null);
            }
            flashingRenderer = null;
        }
    }
}
