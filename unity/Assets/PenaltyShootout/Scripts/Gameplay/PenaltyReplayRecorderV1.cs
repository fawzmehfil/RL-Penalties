using System;
using System.Collections.Generic;
using System.IO;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [Serializable]
    public sealed class PenaltyReplaySessionV1
    {
        public string ReplayContractId = KernelConstants.PenaltyReplayContractId;
        public string SetContractId = KernelConstants.PenaltySetContractId;
        public string SessionId;
        public string CreatedUtc;
        public ulong SessionSeed;
        public string BuildId;
        public string GitCommit;
        public string EnvironmentId = KernelConstants.EnvironmentId;
        public string InputContractId = KernelConstants.PlayerPenaltyInputContractId;
        public string ShotContractId = KernelConstants.PlayerShotContractId;
        public string ShotPhysicsId = KernelConstants.PlayerShotPhysicsId;
        public string ScenarioSuiteId = KernelConstants.PlayerInteractiveScenarioSuiteId;
        public string MotorContractId =
            KernelConstants.GoalkeeperForwardMotorContractId;
        public string GloveHandlingId =
            KernelConstants.GoalkeeperGloveHandlingContractId;
        public string InputConfigHash;
        public string InterceptionModelHash;
        public string TimingModelHash;
        public PenaltySetScoreV1 Score = new PenaltySetScoreV1();
        public List<PenaltyReplayAttemptV1> Attempts =
            new List<PenaltyReplayAttemptV1>(PlayerPenaltyInputMathV1.ShotsPerSet);
    }

    [Serializable]
    public sealed class PenaltyReplayAttemptV1
    {
        public int SetShotIndex;
        public long AttemptId;
        public PlayerPenaltyShotRequestV1 Request;
        public ResolvedPlayerShotV1 ResolvedShot;
        public bool HasLaunch;
        public float LaunchAttemptTime;
        public AttemptOutcome Outcome;
        public bool HasMeasuredCrossing;
        public Vector3 MeasuredCrossingLocal;
        public float AttemptTime;
        public List<PenaltyReplayFrameV1> Frames = new List<PenaltyReplayFrameV1>(128);
        public List<PenaltyReplayKeeperCommandV1> KeeperCommands =
            new List<PenaltyReplayKeeperCommandV1>(64);
        public List<PenaltyReplayContactV1> Contacts =
            new List<PenaltyReplayContactV1>(8);
    }

    [Serializable]
    public struct PenaltyReplayFrameV1
    {
        public int PhysicsTick;
        public float AttemptTime;
        public float BallFlightTime;
        public AttemptPhase Phase;
        public Vector3 BallPositionLocal;
        public Quaternion BallRotation;
        public Vector3 BallVelocityLocal;
        public Vector3 BallSpin;
        public Vector3 KeeperRootLocal;
        public Vector3 LeftGloveLocal;
        public Vector3 RightGloveLocal;
        public GoalkeeperControlMotorState KeeperState;
        public float KeeperReach;
        public GoalkeeperControlCommand ActiveCommand;
    }

    [Serializable]
    public struct PenaltyReplayKeeperCommandV1
    {
        public int DecisionIndex;
        public int PhysicsTick;
        public float BallFlightTime;
        public GoalkeeperControlCommand Command;
    }

    [Serializable]
    public struct PenaltyReplayContactV1
    {
        public float AttemptTime;
        public ContactKind Kind;
        public GoalkeeperContactPart GoalkeeperPart;
        public Vector3 PointLocal;
        public Vector3 NormalLocal;
        public Vector3 ImpulseLocal;
        public Vector3 RelativeVelocityLocal;
    }

    [DefaultExecutionOrder(-100)]
    public sealed class PenaltyReplayRecorderV1 : MonoBehaviour
    {
        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private Stage7RuntimeManifestV1 runtimeManifest;

        private PenaltyReplaySessionV1 session;
        private PenaltyReplayAttemptV1 currentAttempt;
        private string lastWriteError = string.Empty;

        public PenaltyReplaySessionV1 Session => session;
        public string LastWriteError => lastWriteError;
        public string LastWrittenPath { get; private set; } = string.Empty;

        public void Configure(
            PenaltyAreaController areaController,
            Stage7RuntimeManifestV1 manifest)
        {
            controller = areaController;
            runtimeManifest = manifest;
        }

        private void OnEnable()
        {
            if (controller == null)
            {
                return;
            }
            controller.ShotLaunched += OnShotLaunched;
            controller.GoalkeeperControlCommandAccepted += OnKeeperCommand;
            controller.ContactRecorded += OnContactRecorded;
            controller.AttemptCompleted += OnAttemptCompleted;
        }

        private void OnDisable()
        {
            if (controller == null)
            {
                return;
            }
            controller.ShotLaunched -= OnShotLaunched;
            controller.GoalkeeperControlCommandAccepted -= OnKeeperCommand;
            controller.ContactRecorded -= OnContactRecorded;
            controller.AttemptCompleted -= OnAttemptCompleted;
        }

        public void StartSession(ulong sessionSeed)
        {
            var manifest = runtimeManifest;
            session = new PenaltyReplaySessionV1
            {
                SessionId = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                SessionSeed = sessionSeed,
                BuildId = manifest == null ? "development" : manifest.BuildId,
                GitCommit = manifest == null ? "unknown" : manifest.GitCommit,
                InputConfigHash = manifest == null
                    ? "unknown"
                    : manifest.InputConfigHash,
                InterceptionModelHash = manifest == null
                    ? "unknown"
                    : manifest.InterceptionModelHash,
                TimingModelHash = manifest == null
                    ? "unknown"
                    : manifest.TimingModelHash,
            };
            currentAttempt = null;
            LastWrittenPath = string.Empty;
            lastWriteError = string.Empty;
        }

        public void BeginAttempt(
            int setShotIndex,
            PlayerPenaltyShotRequestV1 request)
        {
            if (session == null || currentAttempt != null || controller == null)
            {
                return;
            }

            currentAttempt = new PenaltyReplayAttemptV1
            {
                SetShotIndex = setShotIndex,
                AttemptId = controller.AttemptId,
                Request = request,
            };
        }

        public void SetScore(PenaltySetScoreV1 score)
        {
            if (session == null || score == null)
            {
                return;
            }
            session.Score = new PenaltySetScoreV1
            {
                ValidShots = score.ValidShots,
                Goals = score.Goals,
                Saves = score.Saves,
                Misses = score.Misses,
            };
        }

        private void FixedUpdate()
        {
            if (currentAttempt == null || controller == null)
            {
                return;
            }
            var phase = controller.Phase;
            if (phase != AttemptPhase.RunUp &&
                phase != AttemptPhase.BallInFlight &&
                phase != AttemptPhase.Terminal)
            {
                return;
            }

            currentAttempt.Frames.Add(new PenaltyReplayFrameV1
            {
                PhysicsTick = controller.PhysicsTick,
                AttemptTime = controller.AttemptTime,
                BallFlightTime = controller.BallFlightTime,
                Phase = phase,
                BallPositionLocal = controller.BallLocalPosition,
                BallRotation = controller.Ball == null
                    ? Quaternion.identity
                    : controller.Ball.rotation,
                BallVelocityLocal = controller.BallLocalVelocity,
                BallSpin = controller.BallAngularVelocity,
                KeeperRootLocal = controller.GoalkeeperControlLocalPosition,
                LeftGloveLocal = controller.GoalkeeperControlLeftGloveLocal,
                RightGloveLocal = controller.GoalkeeperControlRightGloveLocal,
                KeeperState = controller.GoalkeeperControlMotorState,
                KeeperReach = controller.GoalkeeperControlReachExtension,
                ActiveCommand = controller.GoalkeeperActiveControlCommand,
            });
        }

        private void OnShotLaunched(PlayerShotLaunchEventV1 launch)
        {
            if (currentAttempt == null || currentAttempt.AttemptId != launch.AttemptId)
            {
                return;
            }
            currentAttempt.HasLaunch = true;
            currentAttempt.LaunchAttemptTime = launch.AttemptTime;
            currentAttempt.ResolvedShot = launch.Scenario.PlayerShot;
        }

        private void OnKeeperCommand(GoalkeeperControlCommandEventV1 accepted)
        {
            if (currentAttempt == null || currentAttempt.AttemptId != accepted.AttemptId)
            {
                return;
            }
            currentAttempt.KeeperCommands.Add(new PenaltyReplayKeeperCommandV1
            {
                DecisionIndex = accepted.DecisionIndex,
                PhysicsTick = accepted.PhysicsTick,
                BallFlightTime = accepted.BallFlightTime,
                Command = accepted.Command,
            });
        }

        private void OnContactRecorded(BallContactReplayEventV1 contact)
        {
            if (currentAttempt == null || currentAttempt.AttemptId != contact.AttemptId)
            {
                return;
            }
            var origin = controller.ArenaOrigin;
            currentAttempt.Contacts.Add(new PenaltyReplayContactV1
            {
                AttemptTime = contact.AttemptTime,
                Kind = contact.Kind,
                GoalkeeperPart = contact.GoalkeeperPart,
                PointLocal = origin.InverseTransformPoint(contact.Kinematics.PointWorld),
                NormalLocal = origin.InverseTransformDirection(contact.Kinematics.NormalWorld),
                ImpulseLocal = origin.InverseTransformDirection(contact.Kinematics.ImpulseWorld),
                RelativeVelocityLocal = origin.InverseTransformDirection(
                    contact.Kinematics.RelativeVelocityWorld),
            });
        }

        private void OnAttemptCompleted(AttemptResult result)
        {
            if (currentAttempt == null || currentAttempt.AttemptId != result.AttemptId)
            {
                return;
            }
            currentAttempt.Outcome = result.Outcome;
            currentAttempt.HasMeasuredCrossing = result.HasCentrePlaneIntersection;
            currentAttempt.MeasuredCrossingLocal =
                result.MeasuredCentrePlaneIntersectionLocal;
            currentAttempt.AttemptTime = result.AttemptTime;
            session.Attempts.Add(currentAttempt);
            currentAttempt = null;
        }

        public bool CompleteAndWrite(out string path, out string error)
        {
            path = string.Empty;
            error = string.Empty;
            if (session == null || currentAttempt != null)
            {
                error = "Replay session is incomplete.";
                return false;
            }

            try
            {
                var directory = Path.Combine(
                    Application.persistentDataPath,
                    "Replays");
                Directory.CreateDirectory(directory);
                var filename = $"penalty-set-{session.SessionId}.json";
                path = Path.Combine(directory, filename);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(session, true));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
                LastWrittenPath = path;
                lastWriteError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                lastWriteError = error;
                return false;
            }
        }
    }
}
