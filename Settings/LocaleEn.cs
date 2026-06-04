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
                //{ setting.GetOptionGroupLocaleID(Setting.ResetGroup), "Reset" }, 
                { setting.GetOptionGroupLocaleID(Setting.SettingsGroup), "EXR Screenshot Settings" },
                { setting.GetOptionGroupLocaleID(Setting.LinksGroup), "Links" }, 
                
                // Shortcuts
                { setting.GetOptionLabelLocaleID(nameof(Setting.KeyTakeScreenshot)), "Take EXR screenshot " },
                { setting.GetOptionDescLocaleID(nameof(Setting.KeyTakeScreenshot)), "Takes EXR screenshot." },

                // ActionName ID overall it's not visible anywhere I think
                { setting.GetBindingKeyLocaleID(Mod.TakeScreenshotActionName), "TakeScreenshot Key" },
                { setting.GetBindingMapLocaleID(), "EXR Screenshot Mod Settings" },
                
                // Reset
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetShortcuts)), "Reset shortcuts" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetShortcuts)), "Reset all shortcuts of the mod"},
                { setting.GetOptionLabelLocaleID(nameof(Setting.ResetSettings)), "Reset all settings     " },
                { setting.GetOptionDescLocaleID(nameof(Setting.ResetSettings)), "Reset shortcuts and settings of the mod"},
                
                // Donate
                { setting.GetOptionLabelLocaleID(nameof(Setting.DonateLink)), "Donate on Paypal" },
                { setting.GetOptionDescLocaleID(nameof(Setting.DonateLink)), "Opens paypal donation page"},
                
                // Github
                { setting.GetOptionLabelLocaleID(nameof(Setting.GithubLink)), "Source Code on Github" },
                { setting.GetOptionDescLocaleID(nameof(Setting.GithubLink)), "Access to source code, and detailed explanation to answer as many questions you might have."},
                
                // Mod Version
                { setting.GetOptionLabelLocaleID(nameof(Setting.ModVersion)), "Mod Version"},
                { setting.GetOptionDescLocaleID(nameof(Setting.ModVersion)), "Installed version of mod"},
                
                // Super Resolution
                { setting.GetOptionLabelLocaleID(nameof(Setting.TakeSuperResolution)), "Take Super Resolution Screenshots" },
                { setting.GetOptionDescLocaleID(nameof(Setting.TakeSuperResolution)), "Enable to capture at a higher resolution than your native resolution." },
                { setting.GetOptionLabelLocaleID(nameof(Setting.SupersampleScale)), "Super Resolution Scale" },
                { setting.GetOptionDescLocaleID(nameof(Setting.SupersampleScale)), "Multiplier for the internal render resolution." +
                    "\n Upscaling like DLSS or FSR lowers base internal render resolution inside the GPU buffer that this mod sources the image from." +
                    "\n Mod will turn upscaling off temporarily during capture and restore original state after, effectively using native resolution as base of resolution scaling." +
                    "\n Bigger scale takes longer to complete."
                },
                
                // Compression Dropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.CompressionDropdown)), "EXR Compression" },
                { setting.GetOptionDescLocaleID(nameof(Setting.CompressionDropdown)), 
                    "Choose compression method. <All options are Lossless>.\n But compression runs on the main thread, so trade off speed for size on disk:" +
                    "\n <None>: Instant encoding; no compression, very large files." +
                    "\n <ZIP> : High-efficiency compression; slower to encode, but smallest file size." +
                    "\n <RLE> : Balanced; fast encoding using run-length logic." +
                    "\n <PIZ> : Performance optimized; high-speed compression, slightly larger than ZIP"
                },
                
                // Compression Methods 
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.None), "None (Uncompressed)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressZIP), "ZIP (Maximum Compression)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressRLE), "RLE (Run-Length)" },
                { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.CompressPIZ), "PIZ (Wavelet - Recommended)" },
                // { setting.GetEnumValueLocaleID(Setting.CompressionMethodEnum.OutputAsFloat), "Float" }, // Not used
                
                // Accumulation Dropdown
                { setting.GetOptionLabelLocaleID(nameof(Setting.AccumulationFramesDropdown)), "Accumulation Frames" },
                { setting.GetOptionDescLocaleID(nameof(Setting.AccumulationFramesDropdown)), 
                    "Chose amount of accumulation frames before screenshot is taken. " +
                    "\n This will result in longer capture but will accumulate more SSAO, SSGI for more resolved image" +
                    "\n Simply giving you less noise and more accurate contrast." +
                    "\n 16 - 32 frames is recommended, you can go above but returns are diminishing" +
                    "\n Previous baseline was 2 frames."
                },
                
                // Accumulation Frames
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.OneFrame), "1 frame" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.TwoFrames), "2 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.FourFrames), "4 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.EightFrames), "8 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.SixteenFrames), "16 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.ThirtyTwoFrames), "32 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.SixtyFourFrames), "64 frames" },
                { setting.GetEnumValueLocaleID(Setting.AccumulationFramesEnum.OneHundredTwentyEightFrames), "128 frames" },
                
                // Debug Logging Toggle
                { setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "Debug Logging" },
                { setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "Output technical details to the mod log file." },
                
                // Metadata Logging Toggle
                { setting.GetOptionLabelLocaleID(nameof(Setting.MetadataLogging)), "Export Scene Metadata" },
                { setting.GetOptionDescLocaleID(nameof(Setting.MetadataLogging)), 
                    "Saves a companion .txt file with every screenshot, logging the active render state and Volume overrides." +
                    // Generally this should be in readme because this is still not enough info for the user.
                    //"\n <Auto-Exposure Pipeline:> The game looks through Center-Weighted mask, to calculate a Automatic Histogram, targeting a 12.5% mid-grey average scene luminance. Exposure adapts over time and is constrained by <limitMin> and <limitMax> stops (e.g. to prevent night scenes from being over-exposed).**Importantly, this auto-exposure is applied in-game BEFORE the LUT stage, meaning your EXR captures exactly what the LUT 'sees.'**" +
                    //"\n <Pre-Exposed State:> Further this Auto-Exposure is applied during shading using the previous frame’s computed exposure value. The captured EXR therefore contains exposure-adjusted scene radiance ('pre-tonemapping'), with a one-frame adaptation delay that is typically imperceptible." +
                    //"\n <Practical Note:> Because auto-exposure is already applied. This means the image is ready for grading and does not require massive gain adjustments to be viewable." +
                    //"\n <Note:> All other post-processing values (<Tonemapping, LUT, PostExposure, ColorAdjustments, Bloom etc.>) are NOT baked in EXR. Use this log to replicate the game look in Resolve, or ignore it if you intend to author custom Lumina presets for full control over your look. Else be aware that Climate volumes vary significantly by season and their type (Boreal vs. Tropical vs. Standard) and game feature specific overrides for Sunrise/Sunset, Night, Day." +
                    "\n Refer to the GitHub technical reference I have gathered."
                },
                
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
