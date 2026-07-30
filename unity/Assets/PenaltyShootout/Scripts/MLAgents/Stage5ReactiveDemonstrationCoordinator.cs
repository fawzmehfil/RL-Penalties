using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PenaltyShootout.Kernel;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    public sealed class Stage5ReactiveDemonstrationCoordinator :
        MonoBehaviour
    {
        public const string ContractId =
            "goalkeeper-control-v2-reactive-demo-v1";
        public const string OutputArgument = "--stage5-demo-output";
        public const string AttemptsArgument =
            "--stage5-demo-attempts-per-arena";
        public const string SeedArgument = "--stage5-demo-master-seed";

        [SerializeField]
        private int attemptsPerArena = 1250;

        [SerializeField]
        private ulong masterSeed = 20260723UL;

        [SerializeField]
        private string demonstrationDirectory = string.Empty;

        [SerializeField]
        private bool quitWhenComplete = true;

        private readonly List<ArenaRecordingState> arenas = new();
        private bool recordingStarted;
        private bool completed;
        private int closedArenaCount;

        public int AttemptsPerArena
        {
            get => attemptsPerArena;
            set => attemptsPerArena = value;
        }

        public ulong MasterSeed
        {
            get => masterSeed;
            set => masterSeed = value;
        }

        public string DemonstrationDirectory
        {
            get => demonstrationDirectory;
            set => demonstrationDirectory = value;
        }

        public bool QuitWhenComplete
        {
            get => quitWhenComplete;
            set => quitWhenComplete = value;
        }

        public bool Completed => completed;
        public int ClosedArenaCount => closedArenaCount;

        private void Start()
        {
            ApplyCommandLineOverrides();
            if (string.IsNullOrWhiteSpace(demonstrationDirectory))
            {
                return;
            }

            BeginRecording();
        }

        public void BeginRecording()
        {
            if (recordingStarted)
            {
                throw new InvalidOperationException(
                    "Stage 5 demonstration recording already started.");
            }

            recordingStarted = true;
            StartCoroutine(RunRecording());
        }

        private IEnumerator RunRecording()
        {
            try
            {
                ConfigureRecorders();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (quitWhenComplete)
                {
                    Application.Quit(2);
                    yield break;
                }

                throw;
            }

            // DemonstrationRecorder initializes its writer during Update.
            yield return null;
            foreach (var arena in arenas)
            {
                arena.Controller.BeginNextAttempt();
            }
        }

        private void ConfigureRecorders()
        {
            if (attemptsPerArena <= 0)
            {
                throw new InvalidOperationException(
                    "Stage 5 demonstration attempts per arena must be positive.");
            }

            if (string.IsNullOrWhiteSpace(demonstrationDirectory))
            {
                throw new InvalidOperationException(
                    $"{OutputArgument}=<absolute-directory> is required.");
            }

            demonstrationDirectory =
                Path.GetFullPath(demonstrationDirectory);
            if (Directory.Exists(demonstrationDirectory) &&
                Directory.EnumerateFileSystemEntries(
                    demonstrationDirectory).Any())
            {
                throw new InvalidOperationException(
                    "Stage 5 demonstration output must be absent or empty: " +
                    demonstrationDirectory);
            }

            Directory.CreateDirectory(demonstrationDirectory);
            var controllers =
                FindObjectsByType<PenaltyAreaController>(
                    FindObjectsSortMode.None)
                    .OrderBy(item => item.ArenaId)
                    .ToArray();
            if (controllers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Stage 5 demonstration scene contains no arenas.");
            }

            foreach (var controller in controllers)
            {
                var agent =
                    controller.GetComponentInChildren<
                        GoalkeeperControlAgent>(true);
                var behavior =
                    agent == null
                        ? null
                        : agent.GetComponent<BehaviorParameters>();
                var recorder =
                    agent == null
                        ? null
                        : agent.GetComponent<DemonstrationRecorder>();
                if (agent == null || behavior == null || recorder == null)
                {
                    throw new InvalidOperationException(
                        $"Arena {controller.ArenaId} is missing its " +
                        "Stage 5 demonstration components.");
                }

                if (behavior.BehaviorName !=
                    KernelConstants.GoalkeeperControlV2BehaviorName ||
                    behavior.BrainParameters.VectorObservationSize !=
                    KernelConstants.GoalkeeperControlV2ObservationSize ||
                    behavior.BrainParameters.ActionSpec
                        .NumContinuousActions !=
                    GoalkeeperControlSpace.ContinuousActionCount ||
                    !behavior.BrainParameters.ActionSpec.BranchSizes
                        .SequenceEqual(
                            new[]
                            {
                                GoalkeeperControlSpace.CommitBranchSize,
                            }) ||
                    behavior.BehaviorType != BehaviorType.HeuristicOnly)
                {
                    throw new InvalidOperationException(
                        $"Arena {controller.ArenaId} does not expose the " +
                        "GoalkeeperControl-v2 demonstration behavior.");
                }

                controller.AutoRun = false;
                controller.MasterSeed = masterSeed;
                agent.HeuristicMode =
                    GoalkeeperControlHeuristicMode.ReactiveTeacher;
                recorder.DemonstrationName =
                    $"GKCtrlV2A{controller.ArenaId:000}";
                recorder.DemonstrationDirectory =
                    demonstrationDirectory;
                recorder.NumStepsToRecord = 0;
                recorder.Record = true;
                var state = new ArenaRecordingState(
                    controller,
                    recorder);
                arenas.Add(state);
                controller.AttemptCompleted +=
                    result => OnAttemptCompleted(state, result);
            }
        }

        private void OnAttemptCompleted(
            ArenaRecordingState arena,
            AttemptResult result)
        {
            if (arena.Closed)
            {
                return;
            }

            arena.Record(result);
            if (arena.CompletedAttempts < attemptsPerArena)
            {
                arena.Controller.BeginNextAttempt();
                return;
            }

            arena.Controller.AutoRun = false;
            arena.Recorder.Record = false;
            arena.Recorder.Close();
            arena.Closed = true;
            closedArenaCount++;
            if (closedArenaCount == arenas.Count)
            {
                FinishRecording();
            }
        }

        private void FinishRecording()
        {
            WriteTeacherReport();
            completed = true;
            if (quitWhenComplete)
            {
                StartCoroutine(QuitAfterRecordersClose());
            }
        }

        private IEnumerator QuitAfterRecordersClose()
        {
            yield return null;
            Application.Quit(0);
        }

        private void WriteTeacherReport()
        {
            var report = new TeacherReport
            {
                schema_version = 1,
                demonstration_contract_id = ContractId,
                behavior_name =
                    KernelConstants.GoalkeeperControlV2BehaviorName,
                observation_spec_id =
                    KernelConstants.GoalkeeperControlV2ObservationSpecId,
                action_spec_id =
                    KernelConstants.GoalkeeperControlActionSpecId,
                scenario_suite_id = KernelConstants.ScenarioSuiteId,
                master_seed = masterSeed.ToString(),
                arena_count = arenas.Count,
                attempts_per_arena = attemptsPerArena,
                total_attempts =
                    arenas.Sum(item => item.CompletedAttempts),
                saves = arenas.Sum(item => item.Saves),
                goals = arenas.Sum(item => item.Goals),
                invalids = arenas.Sum(item => item.Invalids),
                timeouts = arenas.Sum(item => item.Timeouts),
                off_target = arenas.Sum(item => item.OffTarget),
                goalkeeper_contacts =
                    arenas.Sum(item => item.GoalkeeperContacts),
                glove_contacts =
                    arenas.Sum(item => item.GloveContacts),
                high_attempts =
                    arenas.Sum(item => item.HighAttempts),
                high_saves = arenas.Sum(item => item.HighSaves),
                action_mask_violations =
                    arenas.Sum(item => item.ActionMaskViolations),
                control_command_clamps =
                    arenas.Sum(item => item.ControlCommandClamps),
                policy_decision_duplicate_requests =
                    arenas.Sum(item => item.DuplicateRequests),
                policy_decision_missing_actions =
                    arenas.Sum(item => item.MissingActions),
                arenas = arenas.Select(item => item.ToReport()).ToArray(),
            };
            report.save_rate = Rate(report.saves, report.total_attempts);
            report.glove_contact_rate =
                Rate(report.glove_contacts, report.total_attempts);
            report.high_shot_save_rate =
                Rate(report.high_saves, report.high_attempts);
            var output = Path.Combine(
                demonstrationDirectory,
                "teacher-report.json");
            File.WriteAllText(
                output,
                JsonUtility.ToJson(report, true) + Environment.NewLine);
        }

        private void ApplyCommandLineOverrides()
        {
            var args = Environment.GetCommandLineArgs();
            if (TryReadArgument(args, OutputArgument, out var output))
            {
                demonstrationDirectory = output;
            }

            if (TryReadArgument(args, AttemptsArgument, out var attempts) &&
                int.TryParse(attempts, out var parsedAttempts))
            {
                attemptsPerArena = parsedAttempts;
            }

            if (TryReadArgument(args, SeedArgument, out var seed) &&
                ulong.TryParse(seed, out var parsedSeed))
            {
                masterSeed = parsedSeed;
            }
        }

        private static bool TryReadArgument(
            string[] args,
            string key,
            out string value)
        {
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index].StartsWith(
                        key + "=",
                        StringComparison.Ordinal))
                {
                    value = args[index].Substring(key.Length + 1);
                    return true;
                }

                if (args[index] == key && index + 1 < args.Length)
                {
                    value = args[index + 1];
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static float Rate(int numerator, int denominator)
        {
            return denominator > 0
                ? (float)numerator / denominator
                : 0f;
        }

        private sealed class ArenaRecordingState
        {
            public ArenaRecordingState(
                PenaltyAreaController controller,
                DemonstrationRecorder recorder)
            {
                Controller = controller;
                Recorder = recorder;
            }

            public PenaltyAreaController Controller { get; }
            public DemonstrationRecorder Recorder { get; }
            public int CompletedAttempts { get; private set; }
            public int Saves { get; private set; }
            public int Goals { get; private set; }
            public int Invalids { get; private set; }
            public int Timeouts { get; private set; }
            public int OffTarget { get; private set; }
            public int GoalkeeperContacts { get; private set; }
            public int GloveContacts { get; private set; }
            public int HighAttempts { get; private set; }
            public int HighSaves { get; private set; }
            public int ActionMaskViolations { get; private set; }
            public int ControlCommandClamps { get; private set; }
            public int DuplicateRequests { get; private set; }
            public int MissingActions { get; private set; }
            public bool Closed { get; set; }

            public void Record(AttemptResult result)
            {
                CompletedAttempts++;
                var saved =
                    GoalkeeperControlTrainingContracts.IsSave(
                        result.Outcome);
                Saves += saved ? 1 : 0;
                Goals += result.Outcome == AttemptOutcome.Goal ? 1 : 0;
                Invalids +=
                    result.Outcome == AttemptOutcome.Invalid ? 1 : 0;
                Timeouts +=
                    result.Outcome == AttemptOutcome.Timeout ? 1 : 0;
                OffTarget +=
                    (result.Outcome == AttemptOutcome.MissWide ||
                     result.Outcome == AttemptOutcome.MissHigh ||
                     result.Outcome == AttemptOutcome.PostOrCrossbarOut)
                        ? 1
                        : 0;
                GoalkeeperContacts += result.GoalkeeperContact ? 1 : 0;
                GloveContacts += result.GloveContact ? 1 : 0;
                var targetAim = GoalkeeperControlSpace.LocalToAim(
                    new Vector2(
                        result.RequestedTargetLocal.x,
                        result.RequestedTargetLocal.y));
                var high =
                    Mathf.Clamp01((targetAim.y + 1f) * 0.5f) >= 0.66f;
                HighAttempts += high ? 1 : 0;
                HighSaves += high && saved ? 1 : 0;
                ActionMaskViolations += result.ActionMaskViolations;
                ControlCommandClamps += result.ControlCommandClampCount;
                DuplicateRequests +=
                    result.PolicyDecisionDuplicateRequestCount;
                MissingActions += result.PolicyDecisionMissingActionCount;
            }

            public ArenaReport ToReport()
            {
                return new ArenaReport
                {
                    arena_id = Controller.ArenaId,
                    attempts = CompletedAttempts,
                    saves = Saves,
                    goals = Goals,
                    invalids = Invalids,
                    timeouts = Timeouts,
                    glove_contacts = GloveContacts,
                    high_attempts = HighAttempts,
                    high_saves = HighSaves,
                    action_mask_violations = ActionMaskViolations,
                    control_command_clamps = ControlCommandClamps,
                };
            }
        }

        [Serializable]
        private sealed class TeacherReport
        {
            public int schema_version;
            public string demonstration_contract_id;
            public string behavior_name;
            public string observation_spec_id;
            public string action_spec_id;
            public string scenario_suite_id;
            public string master_seed;
            public int arena_count;
            public int attempts_per_arena;
            public int total_attempts;
            public int saves;
            public int goals;
            public int invalids;
            public int timeouts;
            public int off_target;
            public int goalkeeper_contacts;
            public int glove_contacts;
            public int high_attempts;
            public int high_saves;
            public int action_mask_violations;
            public int control_command_clamps;
            public int policy_decision_duplicate_requests;
            public int policy_decision_missing_actions;
            public float save_rate;
            public float glove_contact_rate;
            public float high_shot_save_rate;
            public ArenaReport[] arenas;
        }

        [Serializable]
        private sealed class ArenaReport
        {
            public int arena_id;
            public int attempts;
            public int saves;
            public int goals;
            public int invalids;
            public int timeouts;
            public int glove_contacts;
            public int high_attempts;
            public int high_saves;
            public int action_mask_violations;
            public int control_command_clamps;
        }
    }
}
