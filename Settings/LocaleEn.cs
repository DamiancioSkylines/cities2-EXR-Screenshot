using System.Collections.Generic;
using Colossal;

namespace EXRScreenshot.Settings
{
    public class LocaleEn(Setting setting) : IDictionarySource
    {
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { setting.GetSettingsLocaleID(), "EXR Screenshot" }, // Mod Name
                { setting.GetOptionTabLocaleID(Setting.kSection), "Main" }, // Tab Name

                { setting.GetOptionGroupLocaleID(Setting.KButtonGroup), "My Button Group" }, // Button Group Name
                { setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "EXR Screenshot Shortcuts" }, // Keybinding Group Name for TakeScreenshot
                //{ setting.GetOptionGroupLocaleID(Setting.KSecondKeybindingGroup), "TakeScreenshotB Shortcut" }, // New Keybinding Group Name for TakeScreenshotB

                //{ setting.GetOptionLabelLocaleID(nameof(Setting.MyButton)), "My Button" }, // Button Label
                //{ setting.GetOptionDescLocaleID(nameof(Setting.MyButton)), "This is my simple button." }, // Button Description

                { setting.GetOptionLabelLocaleID(nameof(Setting.KeyTakeScreenshot)), "Take EXR screenshot" }, // Shortcut Label for TakeScreenshot
                { setting.GetOptionDescLocaleID(nameof(Setting.KeyTakeScreenshot)), "Hotkey to take EXR screenshot" }, // Shortcut Description for TakeScreenshot

                //{ setting.GetOptionLabelLocaleID(nameof(Setting.KeyTakeScreenshotB)), "Take EXR screenshot B" }, // New Shortcut Label for TakeScreenshotB
                //{ setting.GetOptionDescLocaleID(nameof(Setting.KeyTakeScreenshotB)), "Hotkey to take the second type of EXR screenshot currently does nothing" }, // New Shortcut Description for TakeScreenshotB

                { setting.GetBindingKeyLocaleID(Mod.KeyTakeScreenshotName), "TakeScreenshot Key" }, // Shortcut Key Label for TakeScreenshot

                { setting.GetBindingMapLocaleID(), "My Simple Mod Settings" },
                
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "Reset key bindings" },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), $"Reset all key bindings of the mod"
                },
                { setting.GetOptionLabelLocaleID(nameof(Setting.TakeSuperResolution)), "Take Super Resolution Screenshots" }, // Label for the checkbox
                { setting.GetOptionDescLocaleID(nameof(Setting.TakeSuperResolution)), "Enable to take screenshots at minimum 4K resolution else doubles your resolution potentially making big files" }, // Description for the checkbox
            };
            
        }

        public void Unload() { }
    }
}
