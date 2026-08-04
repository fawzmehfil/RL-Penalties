using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class Stage6ShotVarietyLab : MonoBehaviour
    {
        private const float CandidateBounciness = 0.35f;
        private const float CandidateFriction = 0.15f;
        private const string DefaultReplayManifest =
            "configs/audits/stage6-contact-review-replays-v1.json";

        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private HumanShotDistributionConfigV1 distribution;
        [SerializeField] private bool automaticCycling;
        [SerializeField] private bool nativeGoalkeeper = true;

        private readonly List<Stage6ReplayKeyV1> replayKeys =
            new List<Stage6ReplayKeyV1>();
        private PlayerShotStyleV1? forcedStyle;
        private HumanShotDistributionConfigV1 runtimeDistribution;
        private IGoalkeeperNativeInferenceControlV1 nativeInferenceControl;
        private AttemptResult baselineResult;
        private AttemptResult candidateResult;
        private int replayIndex;
        private bool replayMode = true;
        private bool contactCandidateEnabled;
        private string status = "Ready";

        public int ReplayCount => replayKeys.Count;
        public bool UsesNativeGoalkeeper => nativeGoalkeeper;
        public bool ContactCandidateEnabled => contactCandidateEnabled;

        private void Awake()
        {
            if (distribution != null)
            {
                runtimeDistribution = Instantiate(distribution);
                runtimeDistribution.name = $"{distribution.name} (Runtime Lab Copy)";
                distribution = runtimeDistribution;
            }

            if (controller == null)
            {
                status = "Missing PenaltyAreaController";
                return;
            }

            if (runtimeDistribution != null)
            {
                controller.HumanShotConfiguration = runtimeDistribution;
                controller.ScenarioController.HumanShotConfiguration =
                    runtimeDistribution;
            }
            controller.DebugUiIgnoresArenaId = true;
            controller.AttemptCompleted += OnAttemptCompleted;
            nativeInferenceControl =
                controller.ActionSource as IGoalkeeperNativeInferenceControlV1;
            SetNativeGoalkeeper(nativeGoalkeeper);
            LoadReplayCatalog();
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.AttemptCompleted -= OnAttemptCompleted;
                controller.ClearAuditGloveContactMaterial();
            }
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
            if (Input.GetKeyDown(KeyCode.L)) EnableReplayMode();
            if (Input.GetKeyDown(KeyCode.B)) ToggleContactCandidate();
            if (Input.GetKeyDown(KeyCode.N)) SelectReplay(1);
            if (Input.GetKeyDown(KeyCode.P)) SelectReplay(-1);
            if (Input.GetKeyDown(KeyCode.M)) SetNativeGoalkeeper(!nativeGoalkeeper);
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
            if (!RequireTerminal("Shot style can only change between attempts."))
            {
                return;
            }
            replayMode = false;
            forcedStyle = style;
            baselineResult = null;
            candidateResult = null;
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
            status = style.HasValue
                ? $"Random {style.Value} shots selected"
                : "Random gameplay mixture selected";
        }

        private void EnableReplayMode()
        {
            if (!RequireTerminal("Replay mode can only change between attempts."))
            {
                return;
            }
            replayMode = true;
            forcedStyle = null;
            status = "Failure replay mode selected";
        }

        private void LaunchIfReady()
        {
            if (controller == null || !controller.IsTerminal)
            {
                return;
            }
            if (replayMode && !ArmCurrentReplay())
            {
                return;
            }
            controller.BeginNextAttempt();
        }

        private void ToggleContactCandidate()
        {
            if (!RequireTerminal("Contact mode can only change between attempts."))
            {
                return;
            }
            contactCandidateEnabled = !contactCandidateEnabled;
            ApplyContactMode();
            status = contactCandidateEnabled
                ? "Candidate contact enabled; replaying the same shot"
                : "Baseline contact restored; replaying the same shot";
            LaunchIfReady();
        }

        private void ApplyContactMode()
        {
            if (contactCandidateEnabled)
            {
                controller.ConfigureAuditGloveContactMaterial(
                    CandidateBounciness,
                    CandidateFriction);
            }
            else
            {
                controller.ClearAuditGloveContactMaterial();
            }
        }

        private void SelectReplay(int offset)
        {
            if (!RequireTerminal("Replay selection can only change between attempts.") ||
                replayKeys.Count == 0)
            {
                return;
            }
            replayMode = true;
            replayIndex = (replayIndex + offset + replayKeys.Count) % replayKeys.Count;
            contactCandidateEnabled = false;
            ApplyContactMode();
            baselineResult = null;
            candidateResult = null;
            status = "Selected next failure; baseline contact restored";
            LaunchIfReady();
        }

        private void SetNativeGoalkeeper(bool enabled)
        {
            if (!RequireTerminal("Keeper mode can only change between attempts."))
            {
                return;
            }
            var error = string.Empty;
            if (nativeInferenceControl == null ||
                !nativeInferenceControl.TrySetNativeInference(enabled, out error))
            {
                nativeGoalkeeper = false;
                status = string.IsNullOrEmpty(error)
                    ? "Native inference control is unavailable"
                    : error;
                return;
            }

            nativeGoalkeeper = enabled;
            baselineResult = null;
            candidateResult = null;
            status = enabled
                ? "Frozen native Stage 5 goalkeeper selected"
                : "Manual goalkeeper selected";
        }

        private bool RequireTerminal(string message)
        {
            if (controller != null && controller.IsTerminal)
            {
                return true;
            }
            status = message;
            return false;
        }

        private void LoadReplayCatalog()
        {
            if (TryLoadSingleCommandLineReplay())
            {
                return;
            }

            var manifestPath = ReadArgument(
                Environment.GetCommandLineArgs(),
                "--stage6-replay-manifest=");
            if (string.IsNullOrEmpty(manifestPath))
            {
                manifestPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    DefaultReplayManifest));
            }
            var parseError = string.Empty;
            if (File.Exists(manifestPath) &&
                Stage6ContactReviewReplayCatalogV1.TryParse(
                    File.ReadAllText(manifestPath),
                    Stage6ContactReviewReplayCatalogV1.DefaultMasterSeed,
                    out var parsed,
                    out parseError))
            {
                replayKeys.AddRange(parsed);
                status = $"Loaded {parsed.Length} fixed failure replays";
                return;
            }

            replayKeys.Add(new Stage6ReplayKeyV1(
                Stage6ContactReviewReplayCatalogV1.DefaultMasterSeed,
                3,
                10,
                "Power"));
            status = File.Exists(manifestPath)
                ? $"Replay manifest rejected; using fallback: {parseError}"
                : "Replay manifest not found; using one fixed fallback replay";
        }

        private bool TryLoadSingleCommandLineReplay()
        {
            var arguments = Environment.GetCommandLineArgs();
            var seedText = ReadArgument(arguments, "--stage6-replay-master-seed=");
            var arenaText = ReadArgument(arguments, "--stage6-replay-arena-id=");
            var attemptText = ReadArgument(arguments, "--stage6-replay-attempt-id=");
            if (string.IsNullOrEmpty(seedText) ||
                string.IsNullOrEmpty(arenaText) ||
                string.IsNullOrEmpty(attemptText))
            {
                return false;
            }
            if (!ulong.TryParse(seedText, out var seed) ||
                !int.TryParse(arenaText, out var arenaId) ||
                !long.TryParse(attemptText, out var attemptId))
            {
                status = "Replay command-line arguments are not numeric";
                return false;
            }
            try
            {
                replayKeys.Add(new Stage6ReplayKeyV1(seed, arenaId, attemptId));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                status = exception.Message;
                return false;
            }
            status = "Loaded command-line replay";
            return true;
        }

        private bool ArmCurrentReplay()
        {
            if (replayKeys.Count == 0)
            {
                status = "No replay cases are available";
                return false;
            }
            var key = replayKeys[replayIndex];
            if (!controller.TryConfigureNextReplayAttempt(
                    key.MasterSeed,
                    key.ArenaId,
                    key.AttemptId,
                    out var error))
            {
                status = error;
                return false;
            }
            return true;
        }

        private void OnAttemptCompleted(AttemptResult result)
        {
            if (!replayMode)
            {
                return;
            }
            if (contactCandidateEnabled)
            {
                candidateResult = result;
            }
            else
            {
                baselineResult = result;
            }
            status = $"{(contactCandidateEnabled ? "Candidate" : "Baseline")}: " +
                $"{result.Outcome}, {result.FirstGoalkeeperContactPart}";
        }

        private static string ReadArgument(string[] arguments, string prefix)
        {
            foreach (var argument in arguments)
            {
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return argument.Substring(prefix.Length);
                }
            }
            return string.Empty;
        }

        private void OnGUI()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(12f, 390f, 610f, 330f), GUI.skin.box);
            GUILayout.Label("Stage 6 Contact Review");
            GUILayout.Label(
                $"Keeper: {(nativeGoalkeeper ? "native split seed-001" : "manual")}  " +
                $"Contact: {(contactCandidateEnabled ? "candidate 0.35 / 0.15" : "baseline")}");
            GUILayout.Label(replayMode && replayKeys.Count > 0
                ? $"Replay {replayIndex + 1}/{replayKeys.Count}: {replayKeys[replayIndex]}"
                : $"Random shot mode: {(forcedStyle.HasValue ? forcedStyle.Value.ToString() : "mixture")}");
            GUILayout.Label(status);

            var terminal = controller != null && controller.IsTerminal;
            GUI.enabled = terminal;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Run same shot")) LaunchIfReady();
            if (GUILayout.Button("Baseline / candidate")) ToggleContactCandidate();
            if (GUILayout.Button("Previous")) SelectReplay(-1);
            if (GUILayout.Button("Next")) SelectReplay(1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Native / manual")) SetNativeGoalkeeper(!nativeGoalkeeper);
            if (GUILayout.Button("Failure replays")) EnableReplayMode();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            DrawResult("Baseline", baselineResult);
            DrawResult("Candidate", candidateResult);
            GUILayout.Label(
                "Keys: Space replay, B contact A/B, N/P case, M keeper, L replay list, 1-4 random shots");
            GUILayout.EndArea();
        }

        private static void DrawResult(string label, AttemptResult result)
        {
            if (result == null)
            {
                GUILayout.Label($"{label}: not run for this replay");
                return;
            }
            var metrics = Stage6ContactReviewMetricsV1.FromResult(result);
            if (!metrics.HasContact)
            {
                GUILayout.Label($"{label}: {result.Outcome}; no goalkeeper contact");
                return;
            }
            GUILayout.Label(
                $"{label}: {result.Outcome}; {result.FirstGoalkeeperContactPart}; " +
                $"contact speed {metrics.ContactBallSpeed:F1} m/s; " +
                $"away/goalward {metrics.AwayFromGoalSpeed:F1}/{metrics.GoalwardSpeed:F1} m/s; " +
                $"vertical {metrics.VerticalSpeed:F1} m/s; impulse {metrics.ImpulseMagnitude:F1} Ns");
        }
    }
}
