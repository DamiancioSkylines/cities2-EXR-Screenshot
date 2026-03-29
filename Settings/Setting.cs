using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;

namespace EXRScreenshot.Settings
{
    /// <summary>
    /// Represents the mod's settings class, handling UI presentation, keybindings, and various mod-specific parameters.
    /// This class extends <see cref="ModSetting"/> to integrate with the game's settings menu.
    /// </summary>
    /// <remarks>
    /// This class defines UI groups, tab order, and keyboard/gamepad actions for various mod functionalities
    /// such as vehicle control, camera behaviour, and general mod settings.
    /// </remarks>
    // Mod settings class, handling UI and keybindings.
    [FileLocation("ModsSettings/" + nameof(EXRScreenshot))]
    // Define the order of groups within the tabs
    [SettingsUIGroupOrder(SettingsGroup,ResetGroup)]
    // Show group names
    [SettingsUIShowGroupName(SettingsGroup,ResetGroup)]
    // Define tab order e.g
    // [SettingsUITabOrder(MainTab, KSecondTab)]

    [SettingsUIKeyboardAction(Mod.TakeScreenshotActionName, usages: new [] { Usages.kMenuUsage, "MyUsage" }, interactions: new [] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string MainTab = "MainTab";
        public const string SettingsGroup = "EXRSettingsGroup";
        public const string ResetGroup = "EXRResetGroup";

        /// <summary>
        /// Initializes a new instance of the <see cref="Setting"/> class.
        /// </summary>
        /// <param name="mod">The mod instance associated with these settings.</param>
        public Setting(IMod mod) : base(mod) { }
        
        /// <summary>
        /// Defines the available modes for taking screenshots
        /// </summary>
        public enum ScreenshotMethodEnum
        {
            NewMethod = 0,
            OldMethod = 1,
        }
        
        
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public enum CompressionMethodEnum
        {
            None = 0,
            //OutputAsFloat = 1, // Float is 32 bit per channel, instead of 16-bit that actual game colour buffer is. For mod use case it just doubles the size of file for no gain.
            CompressZIP = 2,
            CompressRLE = 4,
            CompressPIZ = 8,
        }


        [SettingsUIKeyboardBinding(BindingKeyboard.F1, Mod.TakeScreenshotActionName, shift: true)]
        [SettingsUISection(MainTab, SettingsGroup)]
        public ProxyBinding KeyTakeScreenshot { get; set; }
        
        
        /// <summary>
        /// Gets or sets the currently selected screenshot taking method.
        /// </summary>
        [SettingsUIHidden]
        [SettingsUISection(MainTab, SettingsGroup)]
        public ScreenshotMethodEnum ModeDropdown { get; set; } = ScreenshotMethodEnum.NewMethod;
        
        /// <summary>
        /// Gets or sets the currently selected screenshot compression method.
        /// </summary>
        [SettingsUISection(MainTab, SettingsGroup)]
        public CompressionMethodEnum CompressionDropdown { get; set; } = CompressionMethodEnum.CompressPIZ;
        
        //[SettingsUIHidden]
        [SettingsUISection(MainTab, SettingsGroup)]
        [SettingsUISlider(min = 1.0f, max = 4.0f, step = 0.5f, unit = "Scale")]
        public float SupersampleScale { get; set; } = 1.0f;
        
        //[SettingsUIHidden]
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool TakeSuperResolution { get; set; }
        
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool DebugLogging { get; set; } = false;
        

        [SettingsUIButton]
        [SettingsUIButtonGroup("Reset")]
        [SettingsUISection(MainTab, ResetGroup)]
        public bool ResetShortcuts
        {
            set
            {
                Mod.LOG.Info("EXR Screenshot: Reset key bindings");
                ResetKeyBindings();
            }
        }
        [SettingsUIButton]
        [SettingsUIButtonGroup("Reset")]
        [SettingsUISection(MainTab, ResetGroup)]
        public bool ResetSettings
        {
            set
            {
                Mod.LOG.Info("EXR Screenshot: Reset all settings");
                ResetKeyBindings();
                SetDefaults();
            }
        }
        
        /// <summary>
        /// Retrieves a list of dropdown items for an integer dropdown UI element.
        /// </summary>
        /// <returns>An array of <see cref="DropdownItem{T}"/> representing integer options.</returns>
        public DropdownItem<int>[] GetIntDropdownItems()
        {
            var items = new List<DropdownItem<int>>();

            for (var i = 0; i < 3; i += 1)
            {
                items.Add(new DropdownItem<int>
                {
                    value = i,
                    displayName = i.ToString(),
                });
            }

            return items.ToArray();
        }
        
        public override void SetDefaults()
        {
            TakeSuperResolution = false;
            SupersampleScale = 1.0f;
            ModeDropdown = ScreenshotMethodEnum.NewMethod;
            CompressionDropdown = CompressionMethodEnum.CompressPIZ;
        }
    }
}