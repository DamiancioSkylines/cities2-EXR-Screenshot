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
        public static readonly ILog LOG = LogManager.GetLogger(nameof(EXRScreenshot)).SetShowsErrorsInUI(false);
        private static Setting Setting { get; set; }
        private static ProxyAction _takeScreenshotAction;

        public const string TakeScreenshotActionName = "TakeScrenshot";

        public void OnLoad(UpdateSystem updateSystem)
        {
            // LOG.Info(nameof(OnLoad));

            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(Setting));

            Setting.RegisterKeyBindings();

            _takeScreenshotAction = Setting.GetAction(TakeScreenshotActionName);
            _takeScreenshotAction.shouldBeEnabled = true;

            _takeScreenshotAction.onInteraction += (_, phase) =>
            {
                if (phase == InputActionPhase.Canceled)
                {
                    if (Setting.DebugLogging)
                    {
                        VolumeInspection.LogGlobalStack();
                    }

                    // Generate a unique filename with timestamp
                    var timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var folderPath = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR");
                    var filePath = Path.Combine(folderPath, $"Screenshot_{timestamp}.exr");
                    
                    EXRScreenshotSystem screenshotSystem = new EXRScreenshotSystem();
                    // Get value from mod settings
                    // float currentScale = Setting.SupersampleScale; 
                    // Do high-fidelity capture
                    screenshotSystem.CaptureProEXR(filePath, Setting.SupersampleScale);
                }
            };

            AssetDatabase.global.LoadSettings(nameof(EXRScreenshot), Setting, new Setting(this));
        }
        
        public void OnDispose()
        {
            LOG.Info(nameof(OnDispose));
            if (Setting != null)
            {
                Setting.UnregisterInOptionsUI();
                Setting = null;
            }
        }
    }
}