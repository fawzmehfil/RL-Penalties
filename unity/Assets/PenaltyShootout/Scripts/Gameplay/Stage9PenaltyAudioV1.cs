using System;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage9PenaltyAudioV1 : MonoBehaviour
    {
        private const string MasterKey = "stage9.audio.master";
        private const string EffectsKey = "stage9.audio.effects";
        private const string AmbienceKey = "stage9.audio.ambience";

        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private Stage9AudioLibraryV1 library;
        [SerializeField] private AudioSource[] worldSources;
        [SerializeField] private AudioSource uiSource;
        [SerializeField] private AudioSource ambienceSource;

        private int worldSourceIndex;
        private int eventOrdinal;
        private float masterVolume = 0.8f;
        private float effectsVolume = 0.85f;
        private float ambienceVolume = 0.12f;

        public static bool ForceMutedForAutomation { get; set; }
        public int PlayedEventCount { get; private set; }
        public float MasterVolume => masterVolume;
        public float EffectsVolume => effectsVolume;
        public float AmbienceVolume => ambienceVolume;

        public void Configure(
            PenaltyAreaController areaController,
            Stage9AudioLibraryV1 audioLibrary,
            AudioSource[] spatialSources,
            AudioSource twoDimensionalSource,
            AudioSource ambientSource)
        {
            controller = areaController;
            library = audioLibrary;
            worldSources = spatialSources;
            uiSource = twoDimensionalSource;
            ambienceSource = ambientSource;
        }

        private void Awake()
        {
            ForceMutedForAutomation |= Application.isBatchMode || Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => argument == "--stage9-muted");
            masterVolume = PlayerPrefs.GetFloat(MasterKey, 0.8f);
            effectsVolume = PlayerPrefs.GetFloat(EffectsKey, 0.85f);
            ambienceVolume = PlayerPrefs.GetFloat(AmbienceKey, 0.12f);
            ApplyVolumes();
        }

        private void OnEnable()
        {
            if (controller == null)
            {
                return;
            }
            controller.ShotLaunched += OnShotLaunched;
            controller.ContactRecorded += OnContactRecorded;
            controller.AttemptCompleted += OnAttemptCompleted;
        }

        private void Start()
        {
            if (library == null)
            {
                Debug.LogError("stage9-audio-v1 library is missing.", this);
                enabled = false;
                return;
            }
            if (!library.Validate(out var error))
            {
                Debug.LogError(error, this);
                enabled = false;
                return;
            }
            if (ambienceSource != null && library.AmbienceClip != null)
            {
                ambienceSource.clip = library.AmbienceClip;
                ambienceSource.loop = true;
                if (!ForceMutedForAutomation)
                {
                    ambienceSource.Play();
                }
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.ShotLaunched -= OnShotLaunched;
                controller.ContactRecorded -= OnContactRecorded;
                controller.AttemptCompleted -= OnAttemptCompleted;
            }
            ambienceSource?.Stop();
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterKey, masterVolume);
            ApplyVolumes();
        }

        public void SetEffectsVolume(float value)
        {
            effectsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectsKey, effectsVolume);
            ApplyVolumes();
        }

        public void SetAmbienceVolume(float value)
        {
            ambienceVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(AmbienceKey, ambienceVolume);
            ApplyVolumes();
        }

        public void PlayUiConfirm() => Play2D(Stage9AudioEventV1.UiConfirm);
        public void PlayUiBack() => Play2D(Stage9AudioEventV1.UiBack);

        private void OnShotLaunched(PlayerShotLaunchEventV1 launch)
        {
            var position = controller != null && controller.Ball != null
                ? controller.Ball.position
                : transform.position;
            Play3D(Stage9AudioEventV1.Strike, launch.Scenario.Seed, position, 1f);
        }

        private void OnContactRecorded(BallContactReplayEventV1 contact)
        {
            Stage9AudioEventV1 audioEvent;
            switch (contact.Kind)
            {
                case ContactKind.Goalkeeper:
                    audioEvent = contact.GoalkeeperPart == GoalkeeperContactPart.LeftGlove ||
                        contact.GoalkeeperPart == GoalkeeperContactPart.RightGlove
                            ? Stage9AudioEventV1.GloveContact
                            : Stage9AudioEventV1.BodyContact;
                    break;
                case ContactKind.GoalFrame:
                    audioEvent = Stage9AudioEventV1.GoalFrame;
                    break;
                case ContactKind.Ground:
                    audioEvent = Stage9AudioEventV1.GroundBounce;
                    break;
                default:
                    return;
            }
            var kinematics = contact.Kinematics;
            var position = kinematics.HasValue
                ? kinematics.PointWorld
                : controller.Ball.position;
            var speed = kinematics.HasValue
                ? kinematics.RelativeVelocityWorld.magnitude
                : 12f;
            Play3D(
                audioEvent,
                (ulong)contact.AttemptId,
                position,
                Mathf.InverseLerp(2f, 24f, speed) * 0.35f + 0.65f);
        }

        private void OnAttemptCompleted(AttemptResult result)
        {
            Stage9AudioEventV1 audioEvent;
            switch (result.Outcome)
            {
                case AttemptOutcome.Goal:
                    audioEvent = eventOrdinal % 2 == 0
                        ? Stage9AudioEventV1.GoalNet
                        : Stage9AudioEventV1.GoalReaction;
                    break;
                case AttemptOutcome.Saved:
                case AttemptOutcome.BlockedThenOut:
                    audioEvent = Stage9AudioEventV1.SaveReaction;
                    break;
                case AttemptOutcome.MissHigh:
                case AttemptOutcome.MissWide:
                case AttemptOutcome.PostOrCrossbarOut:
                    audioEvent = Stage9AudioEventV1.MissReaction;
                    break;
                default:
                    return;
            }
            Play2D(audioEvent, result.Seed);
        }

        private void Play2D(Stage9AudioEventV1 audioEvent, ulong seed = 0UL)
        {
            if (library == null || uiSource == null)
            {
                return;
            }
            var clip = library.Select(audioEvent, seed, eventOrdinal);
            if (clip == null)
            {
                return;
            }
            ConfigurePitch(uiSource, seed, eventOrdinal);
            PlayedEventCount++;
            eventOrdinal++;
            if (!ForceMutedForAutomation)
            {
                uiSource.PlayOneShot(clip, masterVolume * effectsVolume);
            }
        }

        private void Play3D(
            Stage9AudioEventV1 audioEvent,
            ulong seed,
            Vector3 position,
            float level)
        {
            if (library == null || worldSources == null || worldSources.Length == 0)
            {
                return;
            }
            var clip = library.Select(audioEvent, seed, eventOrdinal);
            if (clip == null)
            {
                return;
            }
            var source = worldSources[worldSourceIndex % worldSources.Length];
            worldSourceIndex++;
            source.transform.position = position;
            ConfigurePitch(source, seed, eventOrdinal);
            PlayedEventCount++;
            eventOrdinal++;
            if (!ForceMutedForAutomation)
            {
                source.PlayOneShot(
                    clip,
                    Mathf.Clamp01(level) * masterVolume * effectsVolume);
            }
        }

        private void ApplyVolumes()
        {
            var mute = ForceMutedForAutomation;
            if (worldSources != null)
            {
                foreach (var source in worldSources)
                {
                    if (source != null)
                    {
                        source.mute = mute;
                        source.volume = masterVolume * effectsVolume;
                    }
                }
            }
            if (uiSource != null)
            {
                uiSource.mute = mute;
                uiSource.volume = masterVolume * effectsVolume;
            }
            if (ambienceSource != null)
            {
                ambienceSource.mute = mute;
                ambienceSource.volume = masterVolume * ambienceVolume;
            }
        }

        private static void ConfigurePitch(AudioSource source, ulong seed, int ordinal)
        {
            unchecked
            {
                var mixed = seed ^ ((ulong)(ordinal + 17) * 0x9e3779b97f4a7c15UL);
                var unit = (mixed & 0xffffUL) / 65535f;
                source.pitch = Mathf.Lerp(0.97f, 1.03f, unit);
            }
        }
    }
}
