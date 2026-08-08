using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using JetBrains.Annotations;
using UnityEngine;

namespace EXRScreenshot.Settings
{
    /// <summary>
    /// Represents the mod's settings class, handling UI presentation, keybindings, and various mod-specific parameters.
    /// This class extends <see cref="ModSetting"/> to integrate with the game's settings menu.
    /// </summary>
    /// <remarks>
    /// This class defines UI groups, tab order, and keyboard/gamepad actions for various mod functionalities and general mod settings.
    /// </remarks>
    // Mod settings class, handling UI and keybindings.
    [FileLocation("ModsSettings/" + nameof(EXRScreenshot))]
    // Define the order of groups within the tabs
    [SettingsUIGroupOrder(SettingsGroup,LinksGroup)]
    // Show group names
    [SettingsUIShowGroupName(SettingsGroup,LinksGroup)]
    // Define tab order e.g
    // [SettingsUITabOrder(MainTab, KSecondTab)]

    [SettingsUIKeyboardAction(Mod.TakeScreenshotActionName, usages: new [] { Usages.kMenuUsage, "MyUsage" }, interactions: new [] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string MainTab = "MainTab";
        public const string SettingsGroup = "EXRSettingsGroup";
        //public const string ResetGroup = "EXRResetGroup";
        public const string LinksGroup = "EXRLinksGroup";

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
            //OutputAsFloat = 1, // Float is 32 bit per channel, instead of 32-bit total that actual game colour buffer is. For mod use case it just doubles the size of file for no gain.
            CompressZIP = 2,
            CompressRLE = 4,
            CompressPIZ = 8,
        }
        
        /// <summary>
        /// Defines the amount of accumulation wait frame for taking screenshots
        /// </summary>
        public enum AccumulationFramesEnum
        {
            // None = 0, // 0 accumulation frames Breaks the image.
            OneFrame = 1,
            TwoFrames = 2,
            FourFrames = 4,
            EightFrames = 8,
            SixteenFrames = 16,
            ThirtyTwoFrames = 32,
            SixtyFourFrames = 64,
            OneHundredTwentyEightFrames = 128,
        }
        
        /// <summary>
        /// Gets and shows the currently set mod version from .csproj
        /// </summary>
        [SettingsUISection(MainTab, SettingsGroup)]
        public string ModVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);


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
        
        /// <summary>
        /// Gets or sets the currently selected screenshot accumulation wait frames.
        /// </summary>
        [SettingsUISection(MainTab, SettingsGroup)]
        public AccumulationFramesEnum AccumulationFramesDropdown { get; set; } = AccumulationFramesEnum.TwoFrames;
        
        //[SettingsUIHidden]
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool TakeSuperResolution { get; set; }
        
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool MetadataLogging { get; set; }
        
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool DebugLogging { get; set; }
        
        // This button is actually not needed as reset exist in shortcut widget
        [SettingsUIHidden]
        [SettingsUIButton]
        [SettingsUIButtonGroup("Reset")]
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool ResetShortcuts
        {
            // ReSharper disable once ValueParameterNotUsed
            set
            {
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[Settings] Reset key bindings");}
                ResetKeyBindings();
            }
        }
        
        
        [SettingsUIButton]
        [SettingsUIButtonGroup("Reset")]
        [SettingsUISection(MainTab, SettingsGroup)]
        public bool ResetSettings
        {
            // ReSharper disable once ValueParameterNotUsed
            set
            {
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[Settings] Reset all settings");}
                ResetKeyBindings();
                SetDefaults();
            }
        }
        
        [SettingsUIButton]
        [SettingsUIButtonGroup("Links")]
        [SettingsUISection(MainTab, LinksGroup)]
        [UsedImplicitly]
        public bool DonateLink {
            // ReSharper disable once ValueParameterNotUsed
            set => Application.OpenURL("https://www.paypal.com/donate/?hosted_button_id=8VN8P4VJDAKKA");
        }
        
        [SettingsUIButton]
        [SettingsUIButtonGroup("Links")]
        [SettingsUISection(MainTab, LinksGroup)]
        [UsedImplicitly]
        public bool GithubLink {
            // ReSharper disable once ValueParameterNotUsed
            set => Application.OpenURL("https://github.com/DamiancioSkylines/cities2-EXR-Screenshot");
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
            AccumulationFramesDropdown = AccumulationFramesEnum.SixteenFrames;
            DebugLogging = false;
            MetadataLogging = false;
        }
    }
}