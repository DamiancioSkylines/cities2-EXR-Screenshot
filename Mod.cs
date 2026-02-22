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

        public const string KeyTakeScreenshotName = "MyButtonAction"; // Unique name for TakeScreenshot action

        public void OnLoad(UpdateSystem updateSystem)
        {
            LOG.Info(nameof(OnLoad));

            MSetting = new Setting(this); // Initialise the Setting
            MSetting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(MSetting));

            MSetting.RegisterKeyBindings();

            MButtonAction = MSetting.GetAction(KeyTakeScreenshotName);
            MButtonAction.shouldBeEnabled = true;

            MButtonAction.onInteraction += (_, phase) =>
            {
                if (phase == InputActionPhase.Canceled)
                {
                    // 1. Create the recorder
                    EXRRecorder recorder = new EXRRecorder();
                    // 2. Generate a unique filename with timestamp
                    string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string fileName = $"Capture_{timestamp}.exr";
                    string filePath = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, fileName);

                    // 3. Fire the high-fidelity capture
                    recorder.CaptureProEXR(filePath);
                    
                    LOG.Info($"EXR Capture Triggered: {fileName}");
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