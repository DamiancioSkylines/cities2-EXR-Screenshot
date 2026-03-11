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
            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(Setting));
            if (Setting.DebugLogging) {LOG.Info(nameof(OnLoad));}
            
            Setting.RegisterKeyBindings();

            _takeScreenshotAction = Setting.GetAction(TakeScreenshotActionName);
            _takeScreenshotAction.shouldBeEnabled = true;

            _takeScreenshotAction.onInteraction += (_, phase) =>
            {
                var screenshotSystem = new EXRScreenshotSystem();
                if (phase == InputActionPhase.Canceled)
                {
                    if (Setting.DebugLogging) { VolumeInspection.LogGlobalStack();}
                    screenshotSystem.CaptureEXR();
                }
            };

            AssetDatabase.global.LoadSettings(nameof(EXRScreenshot), Setting, new Setting(this));
        }
        
        public void OnDispose()
        {
            // LOG.Info(nameof(OnDispose));
            if (Setting != null)
            {
                Setting.UnregisterInOptionsUI();
                Setting = null;
            }
        }
    }
}