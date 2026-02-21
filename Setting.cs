using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;

namespace EXRScreenshot
{
    [FileLocation("ModsSettings/" + nameof(EXRScreenshot))]
    [SettingsUIGroupOrder(KButtonGroup, kKeybindingGroup)]
    [SettingsUIShowGroupName(kKeybindingGroup)]
    [SettingsUIKeyboardAction(Mod.KeyTakeScreenshotName, ActionType.Button, usages: new string[] { Usages.kMenuUsage, "MyUsage" }, interactions: new string[] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string KButtonGroup = "MyButton";
        public const string kKeybindingGroup = "MyKeybinding";

        public Setting(IMod mod) : base(mod) { }
        
        [SettingsUIKeyboardBinding(BindingKeyboard.F1, Mod.KeyTakeScreenshotName, shift: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding KeyTakeScreenshot { get; set; }

        [SettingsUISection(kSection, kKeybindingGroup)]
        public bool TakeSuperResolution { get; set; } // Added property for the checkbox

        [SettingsUISection(kSection, kKeybindingGroup)]
        public bool ResetBindings
        {
            set
            {
                Mod.LOG.Info("EXR Screenshot: Reset key bindings");
                ResetKeyBindings();
            }
        }

        public override void SetDefaults()
        {
            TakeSuperResolution = false; // Set default value
        }
    }
}