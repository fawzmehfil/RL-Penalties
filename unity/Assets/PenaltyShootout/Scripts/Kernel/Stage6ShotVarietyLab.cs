using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class Stage6ShotVarietyLab : MonoBehaviour
    {
        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private HumanShotDistributionConfigV1 distribution;
        [SerializeField] private bool automaticCycling;
        private PlayerShotStyleV1? forcedStyle;
        private HumanShotDistributionConfigV1 runtimeDistribution;

        private void Awake()
        {
            if (distribution == null)
            {
                return;
            }

            runtimeDistribution = Instantiate(distribution);
            runtimeDistribution.name = $"{distribution.name} (Runtime Lab Copy)";
            distribution = runtimeDistribution;
            if (controller != null)
            {
                controller.HumanShotConfiguration = runtimeDistribution;
                controller.ScenarioController.HumanShotConfiguration =
                    runtimeDistribution;
            }
        }

        private void OnDestroy()
        {
            if (runtimeDistribution != null)
            {
                Destroy(runtimeDistribution);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetStyle(PlayerShotStyleV1.Placed);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetStyle(PlayerShotStyleV1.Power);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetStyle(PlayerShotStyleV1.Curled);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetStyle(null);
            if (Input.GetKeyDown(KeyCode.R)) automaticCycling = !automaticCycling;
            if (Input.GetKeyDown(KeyCode.Space)) LaunchIfReady();
            if (automaticCycling && controller != null && controller.IsTerminal)
            {
                LaunchIfReady();
            }
        }

        public void Configure(
            PenaltyAreaController areaController,
            HumanShotDistributionConfigV1 shotDistribution)
        {
            controller = areaController;
            distribution = shotDistribution;
        }

        private void SetStyle(PlayerShotStyleV1? style)
        {
            forcedStyle = style;
            if (distribution == null)
            {
                return;
            }
            distribution.PlacedWeight = !style.HasValue ? 0.45f :
                style.Value == PlayerShotStyleV1.Placed ? 1f : 0f;
            distribution.PowerWeight = !style.HasValue ? 0.35f :
                style.Value == PlayerShotStyleV1.Power ? 1f : 0f;
            distribution.CurledWeight = !style.HasValue ? 0.20f :
                style.Value == PlayerShotStyleV1.Curled ? 1f : 0f;
        }

        private void LaunchIfReady()
        {
            if (controller != null && controller.IsTerminal)
            {
                controller.BeginNextAttempt();
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 575f, 470f, 55f), GUI.skin.box);
            GUILayout.Label(
                $"Stage 6 shot mode: {(forcedStyle.HasValue ? forcedStyle.Value.ToString() : "Gameplay mixture")}; auto: {automaticCycling}");
            GUILayout.Label("1 placed, 2 power, 3 curled, 4 mixture, Space launch, R auto");
            GUILayout.EndArea();
        }
    }
}
