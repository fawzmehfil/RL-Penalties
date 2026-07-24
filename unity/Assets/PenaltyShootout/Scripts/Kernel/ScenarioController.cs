using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class ScenarioController : MonoBehaviour, IAttemptResettable
    {
        [SerializeField]
        private ShotDistributionConfig configuration;

        [SerializeField]
        private int arenaId;

        [SerializeField]
        private ulong masterSeed = 20260723UL;

        private long lastAttemptId;
        private ulong lastSeed;

        public ShotDistributionConfig Configuration
        {
            get => configuration;
            set => configuration = value;
        }

        public int ArenaId
        {
            get => arenaId;
            set => arenaId = value;
        }

        public ulong MasterSeed
        {
            get => masterSeed;
            set => masterSeed = value;
        }

        public ScenarioInstance Sample(
            long attemptId,
            Vector3 gravity,
            float fixedTimestep)
        {
            if (configuration == null)
            {
                throw new InvalidOperationException("Scenario configuration is missing.");
            }

            lastAttemptId = attemptId;
            lastSeed = Pcg32.DeriveSeed(masterSeed, arenaId, attemptId);
            return ProceduralShotGenerator.Sample(
                configuration,
                lastSeed,
                gravity,
                fixedTimestep);
        }

        public void ResetForAttempt(long attemptId, ulong seed)
        {
            lastAttemptId = attemptId;
            lastSeed = seed;
        }

        public bool ValidateReset(out string error)
        {
            if (lastAttemptId < 0 || lastSeed == 0UL)
            {
                error = "Scenario seed state is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
