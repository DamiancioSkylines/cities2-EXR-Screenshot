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
        
        private EXRScreenshotSystem _exrScreenshotSystem;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(Setting));
            Setting.RegisterKeyBindings();
            
            _exrScreenshotSystem = new EXRScreenshotSystem();

            _takeScreenshotAction = Setting.GetAction(TakeScreenshotActionName);
            _takeScreenshotAction.shouldBeEnabled = true;
            _takeScreenshotAction.onInteraction += OnScreenshotInteraction;

            AssetDatabase.global.LoadSettings(nameof(EXRScreenshot), Setting, new Setting(this));

            if (Setting.DebugLogging) LOG.Info(nameof(OnLoad));
        }

        private void OnScreenshotInteraction(ProxyAction action, InputActionPhase phase)
        {
            if (phase != InputActionPhase.Canceled) return;
            _exrScreenshotSystem?.CaptureEXR();
        }

        public void OnDispose()
        {
            if (_takeScreenshotAction is not null)
            {
                _takeScreenshotAction.onInteraction -= OnScreenshotInteraction;
                _takeScreenshotAction = null;
            }

            if (_exrScreenshotSystem is not null)
            {
                _exrScreenshotSystem.Cleanup();
                _exrScreenshotSystem = null;
            }
            
            if (Setting is not null)
            {
                Setting.UnregisterInOptionsUI();
                Setting = null;
            }
        }
    }
}