using System;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    public enum Stage9AudioEventV1
    {
        Strike = 0,
        GloveContact = 1,
        BodyContact = 2,
        GoalFrame = 3,
        GoalNet = 4,
        GroundBounce = 5,
        UiConfirm = 6,
        UiBack = 7,
        GoalReaction = 8,
        SaveReaction = 9,
        MissReaction = 10,
    }

    [CreateAssetMenu(
        fileName = "Stage9AudioLibraryV1",
        menuName = "Penalty Shootout/Stage 9/Audio Library V1")]
    public sealed class Stage9AudioLibraryV1 : ScriptableObject
    {
        public const string ContractId = "stage9-audio-v1";

        public AudioClip[] Strike = Array.Empty<AudioClip>();
        public AudioClip[] GloveContact = Array.Empty<AudioClip>();
        public AudioClip[] BodyContact = Array.Empty<AudioClip>();
        public AudioClip[] GoalFrame = Array.Empty<AudioClip>();
        public AudioClip[] GoalNet = Array.Empty<AudioClip>();
        public AudioClip[] GroundBounce = Array.Empty<AudioClip>();
        public AudioClip[] UiConfirm = Array.Empty<AudioClip>();
        public AudioClip[] UiBack = Array.Empty<AudioClip>();
        public AudioClip[] Ambience = Array.Empty<AudioClip>();
        public AudioClip[] GoalReaction = Array.Empty<AudioClip>();
        public AudioClip[] SaveReaction = Array.Empty<AudioClip>();
        public AudioClip[] MissReaction = Array.Empty<AudioClip>();

        public bool Validate(out string error)
        {
            if (!HasClips(Strike, 3) || !HasClips(GloveContact, 3) ||
                !HasClips(BodyContact, 2) || !HasClips(GoalFrame, 2) ||
                !HasClips(GoalNet, 2) || !HasClips(GroundBounce, 2) ||
                !HasClips(UiConfirm, 2) || !HasClips(UiBack, 2) ||
                !HasClips(Ambience, 1) || !HasClips(GoalReaction, 1) ||
                !HasClips(SaveReaction, 1) || !HasClips(MissReaction, 1))
            {
                error = "stage9-audio-v1 is incomplete or contains a missing clip.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public AudioClip Select(Stage9AudioEventV1 audioEvent, ulong seed, int ordinal)
        {
            var clips = ClipsFor(audioEvent);
            return clips == null || clips.Length == 0
                ? null
                : clips[SelectIndex(clips.Length, seed, ordinal, audioEvent)];
        }

        public static int SelectIndex(
            int count,
            ulong seed,
            int ordinal,
            Stage9AudioEventV1 audioEvent)
        {
            if (count <= 0)
            {
                return -1;
            }
            unchecked
            {
                var value = seed ^ 0x9e3779b97f4a7c15UL;
                value ^= (ulong)(ordinal + 1) * 0xbf58476d1ce4e5b9UL;
                value ^= (ulong)((int)audioEvent + 1) * 0x94d049bb133111ebUL;
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                value ^= value >> 31;
                return (int)(value % (ulong)count);
            }
        }

        public AudioClip AmbienceClip =>
            Ambience == null || Ambience.Length == 0 ? null : Ambience[0];

        private AudioClip[] ClipsFor(Stage9AudioEventV1 audioEvent)
        {
            switch (audioEvent)
            {
                case Stage9AudioEventV1.Strike:
                    return Strike;
                case Stage9AudioEventV1.GloveContact:
                    return GloveContact;
                case Stage9AudioEventV1.BodyContact:
                    return BodyContact;
                case Stage9AudioEventV1.GoalFrame:
                    return GoalFrame;
                case Stage9AudioEventV1.GoalNet:
                    return GoalNet;
                case Stage9AudioEventV1.GroundBounce:
                    return GroundBounce;
                case Stage9AudioEventV1.UiConfirm:
                    return UiConfirm;
                case Stage9AudioEventV1.UiBack:
                    return UiBack;
                case Stage9AudioEventV1.GoalReaction:
                    return GoalReaction;
                case Stage9AudioEventV1.SaveReaction:
                    return SaveReaction;
                case Stage9AudioEventV1.MissReaction:
                    return MissReaction;
                default:
                    return Array.Empty<AudioClip>();
            }
        }

        private static bool HasClips(AudioClip[] clips, int minimum)
        {
            if (clips == null || clips.Length < minimum)
            {
                return false;
            }
            for (var index = 0; index < clips.Length; index++)
            {
                if (clips[index] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
