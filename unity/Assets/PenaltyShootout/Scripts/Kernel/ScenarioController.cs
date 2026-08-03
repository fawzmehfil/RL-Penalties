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
        private HumanShotDistributionConfigV1 humanShotConfiguration;

        [SerializeField]
        private PlayerShotPhysicsConfigV1 playerShotPhysicsConfiguration;

        [SerializeField]
        private bool useHumanShots;

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

        public HumanShotDistributionConfigV1 HumanShotConfiguration
        {
            get => humanShotConfiguration;
            set => humanShotConfiguration = value;
        }

        public PlayerShotPhysicsConfigV1 PlayerShotPhysicsConfiguration
        {
            get => playerShotPhysicsConfiguration;
            set => playerShotPhysicsConfiguration = value;
        }

        public bool UseHumanShots
        {
            get => useHumanShots;
            set => useHumanShots = value;
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
            if (useHumanShots)
            {
                if (humanShotConfiguration == null ||
                    playerShotPhysicsConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "Stage 6 shot configurations are missing.");
                }
            }
            else if (configuration == null)
            {
                throw new InvalidOperationException("Scenario configuration is missing.");
            }

            lastAttemptId = attemptId;
            lastSeed = Pcg32.DeriveSeed(masterSeed, arenaId, attemptId);
            var forcedHorizontalSide = ((arenaId + attemptId) & 1L) == 0L
                ? 1f
                : -1f;
            return useHumanShots
                ? HumanShotGeneratorV1.Sample(
                    humanShotConfiguration,
                    playerShotPhysicsConfiguration,
                    lastSeed,
                    gravity,
                    fixedTimestep,
                    forcedHorizontalSide)
                : ProceduralShotGenerator.Sample(
                    configuration,
                    lastSeed,
                    gravity,
                    fixedTimestep);
        }

        public bool ValidateScenario(ScenarioInstance scenario, out string error)
        {
            return useHumanShots
                ? HumanShotGeneratorV1.Validate(
                    scenario,
                    humanShotConfiguration,
                    playerShotPhysicsConfiguration,
                    out error)
                : ProceduralShotGenerator.ValidateOnTarget(
                    scenario,
                    configuration,
                    out error);
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
