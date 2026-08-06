using PenaltyShootout.Kernel;
using UnityEngine;
using UnityEngine.UI;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage7PenaltyHudV1 : MonoBehaviour
    {
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private RectTransform reticle;
        [SerializeField] private RectTransform composureRing;
        [SerializeField] private CanvasGroup aimingGroup;
        [SerializeField] private Image powerFill;
        [SerializeField] private RectTransform curveMarker;
        [SerializeField] private Text shotText;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text outcomeText;
        [SerializeField] private Text technicalText;
        [SerializeField] private CanvasGroup screenFade;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject completePanel;
        [SerializeField] private Text completeScoreText;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button fullscreenButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Slider pointerSensitivity;
        [SerializeField] private Slider keyboardAimSpeed;
        [SerializeField] private Slider reticleContrast;

        public Button ResumeButton => resumeButton;
        public Button RestartButton => restartButton;
        public Button FullscreenButton => fullscreenButton;
        public Button QuitButton => quitButton;
        public Button PlayAgainButton => playAgainButton;
        public Slider PointerSensitivity => pointerSensitivity;
        public Slider KeyboardAimSpeed => keyboardAimSpeed;
        public Slider ReticleContrast => reticleContrast;
        public Camera GameplayCamera
        {
            get => gameplayCamera;
            set => gameplayCamera = value;
        }

        public void Configure(
            Camera camera,
            RectTransform reticleTransform,
            RectTransform composureTransform,
            CanvasGroup aimGroup,
            Image power,
            RectTransform curve,
            Text shot,
            Text score,
            Text outcome,
            Text technical,
            CanvasGroup fade,
            GameObject pause,
            GameObject complete,
            Text completeScore,
            Button resume,
            Button restart,
            Button fullscreen,
            Button quit,
            Button playAgain,
            Slider pointer,
            Slider keyboard,
            Slider contrast)
        {
            gameplayCamera = camera;
            reticle = reticleTransform;
            composureRing = composureTransform;
            aimingGroup = aimGroup;
            powerFill = power;
            curveMarker = curve;
            shotText = shot;
            scoreText = score;
            outcomeText = outcome;
            technicalText = technical;
            screenFade = fade;
            pausePanel = pause;
            completePanel = complete;
            completeScoreText = completeScore;
            resumeButton = resume;
            restartButton = restart;
            fullscreenButton = fullscreen;
            quitButton = quit;
            playAgainButton = playAgain;
            pointerSensitivity = pointer;
            keyboardAimSpeed = keyboard;
            reticleContrast = contrast;
        }

        public void SetAim(
            Vector3 worldTarget,
            float alpha,
            float composureQuality,
            float reticleContrastValue)
        {
            if (gameplayCamera == null || reticle == null)
            {
                return;
            }
            reticle.position = gameplayCamera.WorldToScreenPoint(worldTarget);
            if (composureRing != null)
            {
                composureRing.position = reticle.position;
                var scale = Mathf.Lerp(1.35f, 0.72f, composureQuality);
                composureRing.localScale = Vector3.one * scale;
            }
            if (aimingGroup != null)
            {
                aimingGroup.alpha = Mathf.Clamp01(alpha * reticleContrastValue);
            }
        }

        public void SetPowerAndCurve(float power, float sideSpin)
        {
            if (powerFill != null)
            {
                powerFill.fillAmount = Mathf.Clamp01(power);
            }
            if (curveMarker != null)
            {
                var position = curveMarker.anchoredPosition;
                position.x = Mathf.Clamp(sideSpin, -1f, 1f) * 72f;
                curveMarker.anchoredPosition = position;
            }
        }

        public void SetScore(PenaltySetScoreV1 score)
        {
            if (shotText != null)
            {
                shotText.text = score.Complete
                    ? "SET COMPLETE"
                    : $"SHOT {score.ValidShots + 1} / {PlayerPenaltyInputMathV1.ShotsPerSet}";
            }
            if (scoreText != null)
            {
                scoreText.text =
                    $"GOALS  {score.Goals}     SAVES  {score.Saves}     MISSES  {score.Misses}";
            }
        }

        public void ShowOutcome(AttemptOutcome outcome)
        {
            if (outcomeText == null)
            {
                return;
            }
            outcomeText.text = OutcomeLabel(outcome);
            outcomeText.gameObject.SetActive(true);
        }

        public void HideOutcome()
        {
            if (outcomeText != null)
            {
                outcomeText.gameObject.SetActive(false);
            }
            if (technicalText != null)
            {
                technicalText.gameObject.SetActive(false);
            }
        }

        public void ShowTechnicalRetry(string message)
        {
            if (technicalText == null)
            {
                return;
            }
            technicalText.text = string.IsNullOrWhiteSpace(message)
                ? "SHOT RESET"
                : message;
            technicalText.gameObject.SetActive(true);
        }

        public void ShowPause(bool visible)
        {
            if (pausePanel != null)
            {
                pausePanel.SetActive(visible);
            }
        }

        public void ShowComplete(PenaltySetScoreV1 score, bool visible)
        {
            if (completePanel != null)
            {
                completePanel.SetActive(visible);
            }
            if (completeScoreText != null)
            {
                completeScoreText.text =
                    $"{score.Goals} / {PlayerPenaltyInputMathV1.ShotsPerSet} SCORED\n" +
                    $"{score.Saves} SAVED   {score.Misses} MISSED";
            }
        }

        public void SetFade(float alpha)
        {
            if (screenFade != null)
            {
                screenFade.alpha = Mathf.Clamp01(alpha);
                screenFade.blocksRaycasts = alpha > 0.95f;
            }
        }

        private static string OutcomeLabel(AttemptOutcome outcome)
        {
            switch (outcome)
            {
                case AttemptOutcome.Goal:
                    return "GOAL";
                case AttemptOutcome.Saved:
                case AttemptOutcome.BlockedThenOut:
                    return "SAVED";
                case AttemptOutcome.PostOrCrossbarOut:
                    return "OFF THE FRAME";
                case AttemptOutcome.MissHigh:
                case AttemptOutcome.MissWide:
                    return "OFF TARGET";
                default:
                    return "RETRY";
            }
        }
    }
}
