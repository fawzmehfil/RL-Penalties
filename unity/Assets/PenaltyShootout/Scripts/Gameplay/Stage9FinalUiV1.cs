using System;
using UnityEngine;
using UnityEngine.UI;

namespace PenaltyShootout.Gameplay
{
    public sealed class Stage9FinalUiV1 : MonoBehaviour
    {
        [SerializeField] private Stage9PenaltyAudioV1 audioDirector;
        [SerializeField] private Button[] analysisButtons;
        [SerializeField] private Button[] aboutButtons;
        [SerializeField] private Button aboutCloseButton;
        [SerializeField] private GameObject aboutPanel;
        [SerializeField] private Text aboutText;
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider effectsSlider;
        [SerializeField] private Slider ambienceSlider;
        [SerializeField] private string buildId = "stage9-development";

        public void Configure(
            Stage9PenaltyAudioV1 audio,
            Button[] analysis,
            Button[] about,
            Button closeAbout,
            GameObject aboutObject,
            Text aboutLabel,
            Slider master,
            Slider effects,
            Slider ambience,
            string runtimeBuildId)
        {
            audioDirector = audio;
            analysisButtons = analysis;
            aboutButtons = about;
            aboutCloseButton = closeAbout;
            aboutPanel = aboutObject;
            aboutText = aboutLabel;
            masterSlider = master;
            effectsSlider = effects;
            ambienceSlider = ambience;
            buildId = runtimeBuildId;
        }

        private void Start()
        {
            BindButtons(analysisButtons, OpenAnalysis);
            BindButtons(aboutButtons, ShowAbout);
            aboutCloseButton?.onClick.AddListener(HideAbout);
            ConfigureSlider(masterSlider, audioDirector?.MasterVolume ?? 0.8f,
                value => audioDirector?.SetMasterVolume(value));
            ConfigureSlider(effectsSlider, audioDirector?.EffectsVolume ?? 0.85f,
                value => audioDirector?.SetEffectsVolume(value));
            ConfigureSlider(ambienceSlider, audioDirector?.AmbienceVolume ?? 0.12f,
                value => audioDirector?.SetAmbienceVolume(value));
            if (aboutText != null)
            {
                aboutText.text =
                    "PENALTY SHOOTOUT RL\n" +
                    "GoalkeeperControl-v2 / native split seed 001\n" +
                    "35 visible-state observations / 40 ms delay\n" +
                    "rounded-football-v1\n" + buildId;
            }
            if (aboutPanel != null)
            {
                aboutPanel.SetActive(false);
            }
        }

        public void OpenAnalysis()
        {
            audioDirector?.PlayUiConfirm();
            var path = System.IO.Path.Combine(
                Application.streamingAssetsPath,
                "Stage8Analysis",
                "index.html");
            Application.OpenURL(new Uri(path).AbsoluteUri);
        }

        private void ShowAbout()
        {
            audioDirector?.PlayUiConfirm();
            aboutPanel?.SetActive(true);
        }

        private void HideAbout()
        {
            audioDirector?.PlayUiBack();
            aboutPanel?.SetActive(false);
        }

        private static void BindButtons(Button[] buttons, UnityEngine.Events.UnityAction action)
        {
            if (buttons == null)
            {
                return;
            }
            foreach (var button in buttons)
            {
                button?.onClick.AddListener(action);
            }
        }

        private static void ConfigureSlider(
            Slider slider,
            float value,
            UnityEngine.Events.UnityAction<float> action)
        {
            if (slider == null)
            {
                return;
            }
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(action);
        }
    }

}
