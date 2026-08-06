using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class Stage7PenaltyAudioV1 : MonoBehaviour
    {
        private AudioSource source;
        private AudioClip strike;
        private AudioClip glove;
        private AudioClip frame;
        private AudioClip goal;
        private AudioClip ui;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            strike = Tone("Strike", 115f, 0.09f, 0.45f, true);
            glove = Tone("Glove", 82f, 0.12f, 0.35f, true);
            frame = Tone("Frame", 930f, 0.18f, 0.25f, false);
            goal = Tone("Goal", 330f, 0.28f, 0.22f, false);
            ui = Tone("UI", 520f, 0.06f, 0.15f, false);
        }

        public void PlayStrike() => Play(strike);
        public void PlayUi() => Play(ui);

        public void PlayContact(BallContactReplayEventV1 contact)
        {
            if (contact.Kind == ContactKind.Goalkeeper)
            {
                Play(glove);
            }
            else if (contact.Kind == ContactKind.GoalFrame)
            {
                Play(frame);
            }
        }

        public void PlayOutcome(AttemptOutcome outcome)
        {
            if (outcome == AttemptOutcome.Goal)
            {
                Play(goal);
            }
        }

        private void Play(AudioClip clip)
        {
            if (source != null && clip != null)
            {
                source.PlayOneShot(clip);
            }
        }

        private static AudioClip Tone(
            string name,
            float frequency,
            float seconds,
            float volume,
            bool noise)
        {
            const int sampleRate = 44100;
            var count = Mathf.CeilToInt(seconds * sampleRate);
            var samples = new float[count];
            var random = new System.Random(name.GetHashCode());
            for (var index = 0; index < count; index++)
            {
                var t = index / (float)sampleRate;
                var envelope = Mathf.Pow(1f - index / (float)count, 2f);
                var wave = Mathf.Sin(2f * Mathf.PI * frequency * t);
                if (noise)
                {
                    wave = wave * 0.45f +
                        ((float)random.NextDouble() * 2f - 1f) * 0.55f;
                }
                samples[index] = wave * envelope * volume;
            }
            var clip = AudioClip.Create(name, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
