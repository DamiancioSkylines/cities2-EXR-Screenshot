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
                
                // Group Labels
                { setting.GetOptionGroupLocaleID(Setting.ResetGroup), "Reset" }, 
                { setting.GetOptionGroupLocaleID(Setting.SettingsGroup), "EXR Screenshot Settings" },
                
                // Shortcuts
                { setting.GetOptionLabelLocaleID(nameof(Setting.KeyTakeScreenshot)), "Take EXR screenshot " },
                { setting.GetOptionDescLocaleID(nameof(Setting.KeyTakeScreenshot)), "Shortcut to take EXR screenshot" },

                // ActionName ID overall it's not visible anywhere I think
                { setting.GetBindingKeyLocaleID(Mod.TakeScreenshotActionName), "TakeScreenshot Key" },
                { setting.GetBindingMapLocaleID(), "EXR Screenshot Mod Settings" },
                
                // Reset
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetShortcuts)), "Reset shortcuts" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetShortcuts)), "Reset all shortcuts of the mod"},
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetSettings)), "Reset all settings" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetSettings)), "Reset shortcuts and settings of the mod"},
                
                // Super Resolution
                { setting.GetOptionLabelLocaleID(nameof(Setting.TakeSuperResolution)), "Take Super Resolution Screenshots" },
                { setting.GetOptionDescLocaleID(nameof(Setting.TakeSuperResolution)), "Enable to capture at a higher resolution than your native resolution." },
                { setting.GetOptionLabelLocaleID(nameof(Setting.SupersampleScale)), "Resolution Scale" },
                { setting.GetOptionDescLocaleID(nameof(Setting.SupersampleScale)), "Multiplier for the internal render resolution.\n Upscaling like DLSS or FSR lowers base internal render resolution inside the GPU buffer that this mod sources the image from." },
                
                // Compression Dropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.CompressionDropdown)), "EXR Compression" },
                { setting.GetOptionDescLocaleID(nameof(Setting.CompressionDropdown)), 
                    "Choose compression method. <All options are Lossless>.\n But compression runs on the main thread, so trade off speed for size on disk:" +
                    "\n <None>: Huge file size, no processing." +
                    "\n <ZIP> : Excellent compression, but slow to save." +
                    "\n <RLE> : Fast, run-length encoding." +
                    "\n <PIZ> : Fastest! But not as small as ZIP"
                },
                
                // Compression Methods 
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.None), "None (Uncompressed)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressRLE), "RLE (Run-Length)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressZIP), "ZIP (Maximum Compression)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressPIZ), "PIZ (Wavelet - Recommended)" },
                // { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.OutputAsFloat), "Float" }, // Not used
                
                // Debug Logging Toggle
                { setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "Enable Debug Logging" },
                { setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "Output technical rendering details to the log file." }
                
                /*
                // ModeDropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.ModeDropdown)), "Screenshot Mode" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ModeDropdown)), "Choose method for taking screenshots \n**New Method** grabs screenshot from  buffer before post processing \n**Old Method** grabs the screenshot after post process"},


                // Screenshot Method Dropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.ModeDropdown)), "Capture Method" },
                { setting.GetEnumValueLocaleID(Setting.ScreenshotMethodEnum.NewMethod), "Advanced (RTHandle)" },
                { setting.GetEnumValueLocaleID(Setting.ScreenshotMethodEnum.OldMethod), "Basic (Standard)" },
                */
            };
            
        }

        public void Unload() { }
    }
}
