using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using UnityEngine.InputSystem;
using EXRScreenshot.Settings;
using EXRScreenshot.Systems;
using JetBrains.Annotations;
using UnityEngine;

namespace EXRScreenshot
{
    [UsedImplicitly]
    public class Mod : IMod
    {
        public static ILog LOG = LogManager.GetLogger("EXR Screenshot").SetShowsErrorsInUI(false);
        public static Setting MSetting { get; private set; }
        private static ProxyAction MButtonAction;

        public const string TakeScreenshotActionName = "TakeScrenshot";

        public void OnLoad(UpdateSystem updateSystem)
        {
            LOG.Info(nameof(OnLoad));

            MSetting = new Setting(this); // Initialize the Setting
            MSetting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(MSetting));

            MSetting.RegisterKeyBindings();

            MButtonAction = MSetting.GetAction(TakeScreenshotActionName);
            MButtonAction.shouldBeEnabled = true;

            MButtonAction.onInteraction += (_, phase) =>
            {
                if (phase == InputActionPhase.Canceled)
                {
                    // Generate a unique filename with timestamp
                    string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string folderPath = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR");
                    string filePath = Path.Combine(folderPath, $"Screenshot_{timestamp}.exr");

                    switch (MSetting.ModeDropdown)
                    {
                        case Setting.ScreenshotMethodEnum.NewMethod:
                            EXRRecorder recorder = new EXRRecorder();
                            // Get value from mod settings
                            // float currentScale = MSetting.SupersampleScale; 
                            // Do high-fidelity capture
                            recorder.CaptureProEXR(filePath, MSetting.SupersampleScale);
                            break;
                        case Setting.ScreenshotMethodEnum.OldMethod:
                            LOG.Info("EXR Screenshot: Hotkey for TakeScreenshot activated");
                            // Do high-fidelity normal screenshot
                            MakingScreenshot.TakeScreenshot(MSetting.TakeSuperResolution);
                        break;
                    }
                    
                }
            };

            AssetDatabase.global.LoadSettings(nameof(EXRScreenshot), MSetting, new Setting(this));
        }
        
        public void OnDispose()
        {
            LOG.Info(nameof(OnDispose));
            if (MSetting != null)
            {
                MSetting.UnregisterInOptionsUI();
                MSetting = null;
            }
        }
    }
}