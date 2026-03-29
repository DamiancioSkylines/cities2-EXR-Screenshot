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
        public static Setting Setting { get; private set; }

        private static ProxyAction _takeScreenshotAction;
        public const string TakeScreenshotActionName = "TakeScrenshot";

        public void OnLoad(UpdateSystem updateSystem)
        {
            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(Setting));

            // Instantiate the system once. The constructor sets EXRScreenshotSystem.Instance.
            new EXRScreenshotSystem();

            Setting.RegisterKeyBindings();

            _takeScreenshotAction = Setting.GetAction(TakeScreenshotActionName);
            _takeScreenshotAction.shouldBeEnabled = true;

            _takeScreenshotAction.onInteraction += (_, phase) =>
            {
                if (phase != InputActionPhase.Canceled) return;

                if (EXRScreenshotSystem.Instance != null)
                {
                    if (Setting.DebugLogging) VolumeInspection.LogGlobalStack();
                    EXRScreenshotSystem.Instance.CaptureEXR();
                }
                else
                {
                    LOG.Error("EXRScreenshotSystem is not initialized!");
                }
            };

            AssetDatabase.global.LoadSettings(nameof(EXRScreenshot), Setting, new Setting(this));

            if (Setting.DebugLogging) LOG.Info(nameof(OnLoad));
        }

        public void OnDispose()
        {
            if (Setting != null)
            {
                Setting.UnregisterInOptionsUI();
                Setting = null;
            }
        }
    }
}