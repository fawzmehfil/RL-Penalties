using System;
using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public readonly struct Stage6ReplayKeyV1
    {
        public readonly ulong MasterSeed;
        public readonly int ArenaId;
        public readonly long AttemptId;
        public readonly string ShotStyle;

        public Stage6ReplayKeyV1(
            ulong masterSeed,
            int arenaId,
            long attemptId,
            string shotStyle = "")
        {
            if (masterSeed == 0UL || arenaId < 0 || attemptId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(masterSeed),
                    "Replay keys require a positive seed and attempt ID and a non-negative arena ID.");
            }
            MasterSeed = masterSeed;
            ArenaId = arenaId;
            AttemptId = attemptId;
            ShotStyle = shotStyle ?? string.Empty;
        }

        public override string ToString()
        {
            var style = string.IsNullOrEmpty(ShotStyle)
                ? string.Empty
                : $" {ShotStyle}";
            return $"arena {ArenaId}, attempt {AttemptId}{style}";
        }
    }

    public readonly struct Stage6ContactReviewMetricsV1
    {
        public readonly bool HasContact;
        public readonly float ContactBallSpeed;
        public readonly float AwayFromGoalSpeed;
        public readonly float GoalwardSpeed;
        public readonly float VerticalSpeed;
        public readonly float ImpulseMagnitude;

        private Stage6ContactReviewMetricsV1(
            bool hasContact,
            float contactBallSpeed,
            float awayFromGoalSpeed,
            float goalwardSpeed,
            float verticalSpeed,
            float impulseMagnitude)
        {
            HasContact = hasContact;
            ContactBallSpeed = contactBallSpeed;
            AwayFromGoalSpeed = awayFromGoalSpeed;
            GoalwardSpeed = goalwardSpeed;
            VerticalSpeed = verticalSpeed;
            ImpulseMagnitude = impulseMagnitude;
        }

        public static Stage6ContactReviewMetricsV1 FromResult(AttemptResult result)
        {
            if (result == null || !result.HasFirstGoalkeeperContactKinematics)
            {
                return default;
            }

            var velocity = result.FirstGoalkeeperContactBallVelocityLocal;
            return new Stage6ContactReviewMetricsV1(
                true,
                velocity.magnitude,
                Mathf.Max(0f, velocity.z),
                Mathf.Max(0f, -velocity.z),
                velocity.y,
                result.FirstGoalkeeperContactImpulseLocal.magnitude);
        }
    }

    public static class Stage6ContactReviewReplayCatalogV1
    {
        public const ulong DefaultMasterSeed = 20260803UL;

        public static bool TryParse(
            string json,
            ulong defaultMasterSeed,
            out Stage6ReplayKeyV1[] keys,
            out string error)
        {
            keys = Array.Empty<Stage6ReplayKeyV1>();
            if (string.IsNullOrWhiteSpace(json) || defaultMasterSeed == 0UL)
            {
                error = "Replay manifest JSON and default master seed are required.";
                return false;
            }

            ReplayManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<ReplayManifest>(json);
            }
            catch (Exception exception)
            {
                error = $"Replay manifest JSON is invalid: {exception.Message}";
                return false;
            }
            if (manifest?.entries == null || manifest.entries.Length == 0)
            {
                error = "Replay manifest contains no entries.";
                return false;
            }

            var parsed = new List<Stage6ReplayKeyV1>(manifest.entries.Length);
            var unique = new HashSet<string>();
            foreach (var entry in manifest.entries)
            {
                if (entry == null || entry.arena_id < 0 || entry.attempt_id <= 0)
                {
                    error = "Replay manifest contains an invalid arena or attempt ID.";
                    return false;
                }
                var masterSeed = ReadMasterSeed(
                    entry.replay_arguments,
                    defaultMasterSeed);
                var identity = $"{masterSeed}:{entry.arena_id}:{entry.attempt_id}";
                if (!unique.Add(identity))
                {
                    error = $"Replay manifest contains duplicate key {identity}.";
                    return false;
                }
                parsed.Add(new Stage6ReplayKeyV1(
                    masterSeed,
                    entry.arena_id,
                    entry.attempt_id,
                    entry.shot_style));
            }

            keys = parsed.ToArray();
            error = string.Empty;
            return true;
        }

        private static ulong ReadMasterSeed(string[] arguments, ulong fallback)
        {
            const string prefix = "--stage6-replay-master-seed=";
            if (arguments == null)
            {
                return fallback;
            }
            foreach (var argument in arguments)
            {
                if (argument != null &&
                    argument.StartsWith(prefix, StringComparison.Ordinal) &&
                    ulong.TryParse(argument.Substring(prefix.Length), out var seed) &&
                    seed > 0UL)
                {
                    return seed;
                }
            }
            return fallback;
        }

        [Serializable]
        private sealed class ReplayManifest
        {
            public ReplayEntry[] entries;
        }

        [Serializable]
        private sealed class ReplayEntry
        {
            public int arena_id;
            public long attempt_id;
            public string shot_style;
            public string[] replay_arguments;
        }
    }
}
