using System;
using System.Collections.Generic;
using Colossal;

namespace EXRScreenshot.Settings
{
    public class LocaleEn : IDictionarySource
    {
        private readonly Setting setting;
        public LocaleEn(Setting setting)
        {
            this.setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // Mod Name and Tab Name
                { setting.GetSettingsLocaleID(), "EXR Screenshot" },
                { setting.GetOptionTabLocaleID(Setting.MainTab), "Main" },
                
                // Group Labels if used
                { setting.GetOptionGroupLocaleID(Setting.ResetGroup), "Reset" }, 
                { setting.GetOptionGroupLocaleID(Setting.SettingsGroup), "EXR Screenshot Settings" },
                
                // Shortcuts labels and descriptions
                { setting.GetOptionLabelLocaleID(nameof(Setting.KeyTakeScreenshot)), "Take EXR screenshot " },
                { setting.GetOptionDescLocaleID(nameof(Setting.KeyTakeScreenshot)), "Shortcut to take EXR screenshot" },

                // ActionName ID overall it's not visible anywhere I think
                { setting.GetBindingKeyLocaleID(Mod.TakeScreenshotActionName), "TakeScreenshot Key" },
                { setting.GetBindingMapLocaleID(), "EXR Screenshot Mod Settings" },
                
                // ModeDropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.ModeDropdown)), "Screenshot Mode" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ModeDropdown)), "Choose method for taking screenshots \n**New Method** grabs screenshot from  buffer before post processing \n**Old Method** grabs the screenshot after post process"},
                
                // ScreenshotMethodEnum labels
                { setting.GetEnumValueLocaleID(Setting.ScreenshotMethodEnum.NewMethod), "New Method" },
                { setting.GetEnumValueLocaleID(Setting.ScreenshotMethodEnum.OldMethod), "Old Method" },
                
                // Reset labels and description
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetShortcuts)), "Reset shortcuts" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetShortcuts)), "Reset all shortcuts of the mod"},
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetSettings)), "Reset all settings" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetSettings)), "Reset shortcuts and settings of the mod"},

                // Super Resolution settings labels and descriptions
                { setting.GetOptionLabelLocaleID(nameof(Setting.TakeSuperResolution)), "Take Super Resolution Screenshots" },
                { setting.GetOptionDescLocaleID(nameof(Setting.TakeSuperResolution)), "Enable to take screenshots at minimum 4K resolution else doubles your resolution potentially making big files" },
                { setting.GetOptionLabelLocaleID(nameof(Setting.SupersampleScale)), "Screenshots Scale" },
                { setting.GetOptionDescLocaleID(nameof(Setting.SupersampleScale)), "Change final scale of the screenshot \n Currently not working correct keep it at 1" },
            };
            
        }

        public void Unload() { }
    }
}
