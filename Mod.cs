using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using UnityEngine;
using UnityEngine.InputSystem;
using EXRScreenshot.Settings;
using EXRScreenshot.Systems;

namespace EXRScreenshot
{
    public class Mod : IMod
    {
        public static ILog LOG = LogManager.GetLogger("EXR Screenshot").SetShowsErrorsInUI(false);
        public static Setting MSetting { get; private set; }
        public static ProxyAction MButtonAction;

        public const string KeyTakeScreenshotName = "MyButtonAction"; // Unique name for TakeScreenshot action

        public void OnLoad(UpdateSystem updateSystem)
        {
            LOG.Info(nameof(OnLoad));

            MSetting = new Setting(this); // Initialize the Setting
            MSetting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEn(MSetting));

            MSetting.RegisterKeyBindings();

            MButtonAction = MSetting.GetAction(KeyTakeScreenshotName);
            MButtonAction.shouldBeEnabled = true;

            MButtonAction.onInteraction += (_, phase) =>
            {
                if (phase == InputActionPhase.Canceled)
                {
                    LOG.Info("EXR Screenshot: Hotkey for TakeScreenshot activated");
                    // Pass the setting to TakeScreenshot()
                    MakingScreenshot.TakeScreenshot(MSetting.TakeSuperResolution);
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