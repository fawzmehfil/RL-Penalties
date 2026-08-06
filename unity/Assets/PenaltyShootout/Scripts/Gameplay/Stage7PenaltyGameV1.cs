using System;
using System.Collections;
using PenaltyShootout.Kernel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage7PenaltyGameV1 : MonoBehaviour
    {
        private const string PointerSensitivityKey =
            "stage7.player-penalty-input-v1.pointer-sensitivity";
        private const string KeyboardAimSpeedKey =
            "stage7.player-penalty-input-v1.keyboard-aim-speed";
        private const string ReticleContrastKey =
            "stage7.player-penalty-input-v1.reticle-contrast";
        private const string FullscreenKey =
            "stage7.player-penalty-input-v1.fullscreen";

        [SerializeField] private PenaltyAreaController controller;
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private PlayerPenaltyInputConfigV1 inputConfiguration;
        [SerializeField] private Stage7PenaltyHudV1 hud;
        [SerializeField] private Stage7PenaltyCameraDirectorV1 cameraDirector;
        [SerializeField] private Stage7PenaltyAudioV1 audioDirector;
        [SerializeField] private PenaltyReplayRecorderV1 replayRecorder;

        private PlayerPenaltyInputConfigV1 runtimeConfiguration;
        private readonly PenaltySetScoreV1 score = new PenaltySetScoreV1();
        private Stage7GameplayStateV1 state = Stage7GameplayStateV1.Boot;
        private Stage7GameplayStateV1 stateBeforePause;
        private InputAction aimPointerAction;
        private InputAction aimKeyboardAction;
        private InputAction shootAction;
        private InputAction curveAction;
        private InputAction pauseAction;
        private Vector2 aim;
        private Vector2 lockedAim;
        private float sideSpin;
        private float lockedTimingQuality;
        private float aimingStartTime;
        private float chargingStartTime;
        private float resultStartTime;
        private float reticleContrast = 1f;
        private ulong sessionSeed;
        private PlayerShotInputDeviceV1 activeInputDevice =
            PlayerShotInputDeviceV1.Pointer;
        private bool transitioning;
        private bool startupComplete;
        private bool shootInputArmed;
        private float performanceWindowStart;
        private int performanceFrameCount;
        private bool performanceSampleLogged;

        public Stage7GameplayStateV1 State => state;
        public PenaltySetScoreV1 Score => score;
        public ulong SessionSeed => sessionSeed;
        public float LastMeasuredFramesPerSecond { get; private set; }

        public void Configure(
            PenaltyAreaController areaController,
            PlayerInput input,
            InputActionAsset actions,
            PlayerPenaltyInputConfigV1 configuration,
            Stage7PenaltyHudV1 penaltyHud,
            Stage7PenaltyCameraDirectorV1 camera,
            Stage7PenaltyAudioV1 audio,
            PenaltyReplayRecorderV1 replay)
        {
            controller = areaController;
            playerInput = input;
            inputActions = actions;
            inputConfiguration = configuration;
            hud = penaltyHud;
            cameraDirector = camera;
            audioDirector = audio;
            replayRecorder = replay;
        }

        private void Awake()
        {
            if (inputConfiguration != null)
            {
                CreateRuntimeConfiguration();
            }
            sessionSeed = CreateSessionSeed();
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.AttemptCompleted += OnAttemptCompleted;
                controller.ShotLaunched += OnShotLaunched;
                controller.ContactRecorded += OnContactRecorded;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.AttemptCompleted -= OnAttemptCompleted;
                controller.ShotLaunched -= OnShotLaunched;
                controller.ContactRecorded -= OnContactRecorded;
            }
            SetCursorForGameplay(false);
            Time.timeScale = 1f;
        }

        private void Start()
        {
            if (!ValidateRuntime(out var error))
            {
                Debug.LogError(error, this);
                enabled = false;
                return;
            }

            BindInput();
            BindUi();
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            performanceWindowStart = Time.realtimeSinceStartup + 2f;
            controller.AutoRun = false;
            controller.ShowDebugUi = false;
            replayRecorder?.StartSession(sessionSeed);
            hud.SetScore(score);
            hud.HideOutcome();
            hud.ShowPause(false);
            hud.ShowComplete(score, false);
            hud.SetFade(1f);
            ApplyStoredFullscreen();
            SetCursorForGameplay(true);
            StartCoroutine(PrepareNextShot(true));
            startupComplete = true;
        }

        private bool ValidateRuntime(out string error)
        {
            if (runtimeConfiguration == null && inputConfiguration != null)
            {
                CreateRuntimeConfiguration();
            }
            if (controller == null || playerInput == null ||
                runtimeConfiguration == null || hud == null ||
                cameraDirector == null || replayRecorder == null)
            {
                error =
                    "Stage 7 gameplay dependencies are incomplete: " +
                    $"controller={controller != null}, playerInput={playerInput != null}, " +
                    $"input={inputConfiguration != null}, runtimeInput={runtimeConfiguration != null}, " +
                    $"hud={hud != null}, camera={cameraDirector != null}, " +
                    $"replay={replayRecorder != null}.";
                return false;
            }
            if (!runtimeConfiguration.Validate(out error))
            {
                return false;
            }
            if (!controller.UsesHumanShots || controller.GameplayObservationDelayTicks != 2 ||
                controller.GoalkeeperGloveHandling == null ||
                controller.GoalkeeperGloveHandling.HandlingVersion != 1)
            {
                error = "Stage 7 requires Stage 6 football physics, 40 ms visibility, and Glove Handling v1.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void CreateRuntimeConfiguration()
        {
            runtimeConfiguration = Instantiate(inputConfiguration);
            runtimeConfiguration.name = inputConfiguration.name + " (Runtime)";
            runtimeConfiguration.PointerSensitivity = PlayerPrefs.GetFloat(
                PointerSensitivityKey,
                runtimeConfiguration.PointerSensitivity);
            runtimeConfiguration.KeyboardAimSpeed = PlayerPrefs.GetFloat(
                KeyboardAimSpeedKey,
                runtimeConfiguration.KeyboardAimSpeed);
            reticleContrast = PlayerPrefs.GetFloat(ReticleContrastKey, 1f);
        }

        private void BindInput()
        {
            if (playerInput.actions == null)
            {
                playerInput.actions = inputActions != null
                    ? Instantiate(inputActions)
                    : PlayerPenaltyInputActionsV1.Create();
                playerInput.defaultActionMap = "Gameplay";
            }
            aimPointerAction = playerInput.actions["AimPointer"];
            aimKeyboardAction = playerInput.actions["AimKeyboard"];
            shootAction = playerInput.actions["Shoot"];
            curveAction = playerInput.actions["Curve"];
            pauseAction = playerInput.actions["Pause"];
            if (aimPointerAction == null || aimKeyboardAction == null ||
                shootAction == null || curveAction == null || pauseAction == null)
            {
                throw new InvalidOperationException(
                    "player-penalty-input-v1 action map is incomplete.");
            }
            playerInput.actions.Enable();
        }

        private void BindUi()
        {
            hud.ResumeButton?.onClick.AddListener(Resume);
            hud.RestartButton?.onClick.AddListener(RestartSet);
            hud.FullscreenButton?.onClick.AddListener(ToggleFullscreen);
            hud.QuitButton?.onClick.AddListener(Quit);
            hud.PlayAgainButton?.onClick.AddListener(RestartSet);
            if (hud.PointerSensitivity != null)
            {
                hud.PointerSensitivity.minValue = 0.5f;
                hud.PointerSensitivity.maxValue = 2f;
                hud.PointerSensitivity.value = runtimeConfiguration.PointerSensitivity;
                hud.PointerSensitivity.onValueChanged.AddListener(SetPointerSensitivity);
            }
            if (hud.KeyboardAimSpeed != null)
            {
                hud.KeyboardAimSpeed.minValue = 0.5f;
                hud.KeyboardAimSpeed.maxValue = 2f;
                hud.KeyboardAimSpeed.value = runtimeConfiguration.KeyboardAimSpeed;
                hud.KeyboardAimSpeed.onValueChanged.AddListener(SetKeyboardAimSpeed);
            }
            if (hud.ReticleContrast != null)
            {
                hud.ReticleContrast.minValue = 0.5f;
                hud.ReticleContrast.maxValue = 1f;
                hud.ReticleContrast.value = reticleContrast;
                hud.ReticleContrast.onValueChanged.AddListener(SetReticleContrast);
            }
        }

        private void Update()
        {
            SamplePerformance();
            if (pauseAction != null && pauseAction.WasPressedThisFrame())
            {
                if (state == Stage7GameplayStateV1.Paused)
                {
                    Resume();
                }
                else if (state != Stage7GameplayStateV1.SetComplete)
                {
                    Pause();
                }
                return;
            }
            if (state == Stage7GameplayStateV1.Paused || transitioning)
            {
                return;
            }

            switch (state)
            {
                case Stage7GameplayStateV1.Preparing:
                    if (controller.IsAwaitingPreparedPlayerShot &&
                        controller.Phase == AttemptPhase.Ready)
                    {
                        EnterAiming();
                    }
                    break;
                case Stage7GameplayStateV1.Aiming:
                    UpdateAim();
                    UpdateCurve();
                    if (!shootAction.IsPressed())
                    {
                        shootInputArmed = true;
                    }
                    if (shootInputArmed && shootAction.WasPressedThisFrame())
                    {
                        shootInputArmed = false;
                        BeginCharge();
                    }
                    break;
                case Stage7GameplayStateV1.Charging:
                    UpdateCurve();
                    UpdateChargingHud();
                    if (shootAction.WasReleasedThisFrame())
                    {
                        ReleaseShot();
                    }
                    break;
                case Stage7GameplayStateV1.RunUp:
                    if (controller.Phase == AttemptPhase.BallInFlight)
                    {
                        SetState(Stage7GameplayStateV1.BallInFlight);
                    }
                    break;
                case Stage7GameplayStateV1.Result:
                case Stage7GameplayStateV1.TechnicalRetry:
                    if (Time.unscaledTime - resultStartTime >=
                        runtimeConfiguration.ResultHoldSeconds)
                    {
                        if (score.Complete)
                        {
                            CompleteSet();
                        }
                        else
                        {
                            StartCoroutine(PrepareNextShot(false));
                        }
                    }
                    break;
            }
        }

        private void SamplePerformance()
        {
            if (performanceSampleLogged)
            {
                return;
            }
            var now = Time.realtimeSinceStartup;
            if (now < performanceWindowStart)
            {
                return;
            }
            performanceFrameCount++;
            var elapsed = now - performanceWindowStart;
            if (elapsed < 10f)
            {
                return;
            }
            LastMeasuredFramesPerSecond = performanceFrameCount /
                Mathf.Max(elapsed, 1e-3f);
            performanceSampleLogged = true;
            Debug.Log(
                $"Stage7 performance sample: {LastMeasuredFramesPerSecond:F1} FPS " +
                $"at {Screen.width}x{Screen.height}.",
                this);
        }

        private void UpdateAim()
        {
            var pointer = aimPointerAction.ReadValue<Vector2>();
            var keyboard = aimKeyboardAction.ReadValue<Vector2>();
            var minimumScreen = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
            if (pointer.sqrMagnitude > 0.01f)
            {
                aim += pointer / minimumScreen *
                    (2f * runtimeConfiguration.PointerSensitivity);
                activeInputDevice = PlayerShotInputDeviceV1.Pointer;
            }
            if (keyboard.sqrMagnitude > 0.01f)
            {
                aim += Vector2.ClampMagnitude(keyboard, 1f) *
                    runtimeConfiguration.KeyboardAimSpeed *
                    Time.unscaledDeltaTime;
                activeInputDevice = PlayerShotInputDeviceV1.Keyboard;
            }
            aim = PlayerPenaltyInputMathV1.ClampAim(aim);
            var quality = PlayerPenaltyInputMathV1.ComposureQuality(
                Time.unscaledTime - aimingStartTime,
                runtimeConfiguration);
            hud.SetAim(AimWorld(aim), 1f, quality, reticleContrast);
            hud.SetPowerAndCurve(0f, sideSpin);
        }

        private void UpdateCurve()
        {
            var curve = curveAction.ReadValue<float>();
            if (Mathf.Abs(curve) > 0.01f)
            {
                sideSpin = Mathf.Clamp(
                    sideSpin + curve * runtimeConfiguration.CurveRatePerSecond *
                    Time.unscaledDeltaTime,
                    -1f,
                    1f);
            }
        }

        private void BeginCharge()
        {
            lockedAim = aim;
            lockedTimingQuality = PlayerPenaltyInputMathV1.ComposureQuality(
                Time.unscaledTime - aimingStartTime,
                runtimeConfiguration);
            chargingStartTime = Time.unscaledTime;
            SetState(Stage7GameplayStateV1.Charging);
            UpdateChargingHud();
        }

        private void UpdateChargingHud()
        {
            var elapsed = Time.unscaledTime - chargingStartTime;
            var power = PlayerPenaltyInputMathV1.PowerForHold(
                elapsed,
                runtimeConfiguration);
            var alpha = 1f - Mathf.Clamp01(
                elapsed / runtimeConfiguration.ReticleFadeSeconds);
            hud.SetAim(
                AimWorld(lockedAim),
                alpha,
                lockedTimingQuality,
                reticleContrast);
            hud.SetPowerAndCurve(power, sideSpin);
        }

        private void ReleaseShot()
        {
            var chargeDuration = Mathf.Max(0f, Time.unscaledTime - chargingStartTime);
            var power = PlayerPenaltyInputMathV1.PowerForHold(
                chargeDuration,
                runtimeConfiguration);
            var request = PlayerPenaltyInputMathV1.BuildRequest(
                lockedAim,
                power,
                sideSpin,
                lockedTimingQuality,
                chargeDuration,
                sessionSeed,
                score.ValidShots,
                activeInputDevice,
                runtimeConfiguration);
            if (!controller.TrySubmitPreparedPlayerShot(request, out var error))
            {
                Debug.LogError(error, this);
                hud.ShowTechnicalRetry("SHOT RESET");
                EnterAiming();
                return;
            }

            replayRecorder.BeginAttempt(score.ValidShots, request);
            SetState(Stage7GameplayStateV1.RunUp);
            SetCursorForGameplay(false);
        }

        public bool TrySubmitAutomatedShot(
            PlayerPenaltyShotRequestV1 request,
            out string error)
        {
            if (state != Stage7GameplayStateV1.Aiming)
            {
                error = "Automated shots require Aiming state.";
                return false;
            }
            if (!controller.TrySubmitPreparedPlayerShot(request, out error))
            {
                return false;
            }
            replayRecorder.BeginAttempt(score.ValidShots, request);
            SetState(Stage7GameplayStateV1.RunUp);
            return true;
        }

        private IEnumerator PrepareNextShot(bool initial)
        {
            transitioning = true;
            if (!initial)
            {
                yield return Fade(0f, 1f, runtimeConfiguration.FadeSeconds);
            }
            hud.HideOutcome();
            if (!controller.PrepareNextPlayerAttempt(out var error))
            {
                Debug.LogError(error, this);
                hud.ShowTechnicalRetry("SHOT RESET");
                SetState(Stage7GameplayStateV1.TechnicalRetry);
                resultStartTime = Time.unscaledTime;
                transitioning = false;
                yield break;
            }
            aim = Vector2.zero;
            lockedAim = Vector2.zero;
            sideSpin = 0f;
            hud.SetScore(score);
            hud.SetPowerAndCurve(0f, 0f);
            SetState(Stage7GameplayStateV1.Preparing);
            yield return new WaitUntil(
                () => controller.IsAwaitingPreparedPlayerShot &&
                    controller.Phase == AttemptPhase.Ready);
            EnterAiming();
            yield return Fade(1f, 0f, runtimeConfiguration.FadeSeconds);
            transitioning = false;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            var start = Time.unscaledTime;
            while (Time.unscaledTime - start < duration)
            {
                var value = Mathf.InverseLerp(start, start + duration, Time.unscaledTime);
                hud.SetFade(Mathf.Lerp(from, to, value));
                yield return null;
            }
            hud.SetFade(to);
        }

        private void EnterAiming()
        {
            aimingStartTime = Time.unscaledTime;
            shootInputArmed = false;
            hud.HideOutcome();
            SetState(Stage7GameplayStateV1.Aiming);
            SetCursorForGameplay(true);
        }

        private void OnShotLaunched(PlayerShotLaunchEventV1 launch)
        {
            audioDirector?.PlayStrike();
        }

        private void OnContactRecorded(BallContactReplayEventV1 contact)
        {
            audioDirector?.PlayContact(contact);
        }

        private void OnAttemptCompleted(AttemptResult result)
        {
            var valid = score.Record(result.Outcome);
            replayRecorder.SetScore(score);
            hud.SetScore(score);
            hud.ShowOutcome(result.Outcome);
            audioDirector?.PlayOutcome(result.Outcome);
            resultStartTime = Time.unscaledTime;
            SetState(valid
                ? Stage7GameplayStateV1.Result
                : Stage7GameplayStateV1.TechnicalRetry);
        }

        private void CompleteSet()
        {
            if (state == Stage7GameplayStateV1.SetComplete)
            {
                return;
            }
            replayRecorder.SetScore(score);
            if (!replayRecorder.CompleteAndWrite(out _, out var error))
            {
                Debug.LogWarning($"Replay was retained in memory: {error}", this);
            }
            SetState(Stage7GameplayStateV1.SetComplete);
            hud.ShowComplete(score, true);
            SetCursorForGameplay(false);
        }

        private void Pause()
        {
            if (state == Stage7GameplayStateV1.Charging)
            {
                EnterAiming();
            }
            stateBeforePause = state;
            SetState(Stage7GameplayStateV1.Paused);
            Time.timeScale = 0f;
            hud.ShowPause(true);
            SetCursorForGameplay(false);
        }

        private void Resume()
        {
            if (state != Stage7GameplayStateV1.Paused)
            {
                return;
            }
            Time.timeScale = 1f;
            hud.ShowPause(false);
            SetState(stateBeforePause);
            SetCursorForGameplay(
                state == Stage7GameplayStateV1.Aiming ||
                state == Stage7GameplayStateV1.Charging);
        }

        private void RestartSet()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            PlayerPrefs.SetInt(FullscreenKey, Screen.fullScreen ? 1 : 0);
            PlayerPrefs.Save();
            audioDirector?.PlayUi();
        }

        private void ApplyStoredFullscreen()
        {
            if (PlayerPrefs.HasKey(FullscreenKey))
            {
                Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) != 0;
            }
        }

        private void SetPointerSensitivity(float value)
        {
            runtimeConfiguration.PointerSensitivity = value;
            PlayerPrefs.SetFloat(PointerSensitivityKey, value);
            PlayerPrefs.Save();
        }

        private void SetKeyboardAimSpeed(float value)
        {
            runtimeConfiguration.KeyboardAimSpeed = value;
            PlayerPrefs.SetFloat(KeyboardAimSpeedKey, value);
            PlayerPrefs.Save();
        }

        private void SetReticleContrast(float value)
        {
            reticleContrast = value;
            PlayerPrefs.SetFloat(ReticleContrastKey, value);
            PlayerPrefs.Save();
        }

        private Vector3 AimWorld(Vector2 commandAim)
        {
            var local = new Vector3(
                commandAim.x * PlayerShotResolverV1.MaximumAimX,
                Mathf.Lerp(
                    PlayerShotResolverV1.MinimumAimHeight,
                    PlayerShotResolverV1.MaximumAimHeight,
                    (commandAim.y + 1f) * 0.5f),
                0f);
            return controller.ArenaOrigin.TransformPoint(local);
        }

        private void SetState(Stage7GameplayStateV1 next)
        {
            state = next;
            cameraDirector?.SetState(next);
        }

        private static ulong CreateSessionSeed()
        {
            var ticks = unchecked((ulong)DateTime.UtcNow.Ticks);
            var seed = Pcg32.DeriveSeed(
                ticks,
                unchecked((int)(ticks >> 32)),
                Environment.TickCount);
            return seed == 0UL ? 1UL : seed;
        }

        private static void SetCursorForGameplay(bool gameplay)
        {
            Cursor.visible = !gameplay;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && startupComplete && enabled &&
                state != Stage7GameplayStateV1.Paused &&
                state != Stage7GameplayStateV1.SetComplete)
            {
                Pause();
            }
        }
    }
}
