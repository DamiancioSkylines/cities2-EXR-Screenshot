# EXR Screenshot
Is Cities Skylines 2 mod, providing clean, high dynamic range plate for proper LUT creation.

LUT (Look-Up Table) is a static, highly performant mapping file that transforms input colours to specific output colours. It replaces the engine’s tonemapping, allowing to make your own together with colour adjustments in other word colour grading. In a way it is a static texture that shader reads from instead of calculating values in real time.

### **LUT pipeline**

Game relies on explicit engine pipeline when forming image with external LUT involved.

> [!IMPORTANT]
> scene linear Rec.709 (auto exposure) > effects > post exposure > all color adjustments > LinearToLogC (EI1000 0-1 range) > **External LUT** > linear Rec.709 > sRGB > Display

This means we can not simply take normal screenshot to design your LUT around it, because it will never look remotely close later in game. A regular screenshot is taken too late in the pipeline.
EXR Screenshot will be taken when the game colours are still in scene linear Rec.709 colour space, in full HDR range of this pipeline, before all color adjustments and effects in game.

Automatic exposure is applied in the EXR screenshot. Thats is what we want and will be always feed into our LUT in game, so both day a night have usable range, instead of being drastically over or underexposed.

While post exposure and all color adjustments are not included in the EXR, in game are applied before LUT that is accurate and desired colour science behaviour, which we address later in Lumina preset, else this will be controlled by climate or user with Photo mode.


> [!NOTE]
> In Short EXR will have auto-exposure but not post-exposure, colour effects or any postprocess effects like bloom etc, but also not have any antialiasing baked into pixels. This is why you can take super resolution screenshot with the mod, and inside DaVinci viewport, it will look like its anti aliased image.

> [!TIP]
>  For better DaVinci view **Project settings > Image Scaling > Resize filter** set to **Smoother**.


# Creating LUT

To view your grade exactly in same way as game sees it, we can set up colour management matching what game does in its pipeline when using external LUT’s.

### Prerequisites
First you need extract the exact transform LUT's directly from the Unity Engine:

1. Open Unity Hub and create a **High Definition 3D** sample project.
2. Go to: **Window > Package Manager** > **High Definition RP** > **Samples** > **Additional Post-processing Data** > **Import**.
3. In project tab Navigate to: `Assets\Samples\High Definition RP\[Version]\Additional Post-processing Data\Cube LUTs`
4. Copy all .cube files into your DaVinci Resolve LUT folder **Shift+9 > Colour Management > Open LUT Folder > create "Unity" subfolder**.

Now import some EXR Screenshots and drop them on the timeline, multiple screenshots can be combined into compound clip so they will share the node graph.

---

### Project Setup in Resolve

### Easy method

You can use unity LUT files in two main ways, but we will do the easy one that will work both for export and preview.

Inside Project Settings **Shift + 9 > Color Management**:

![Project Setting](</Documentation/Project Settings.png> "Project Settings")


<details>
<summary>Other methods</summary>

---

### Node sandwich method

Instead of using acquired LUT's in Color Management is to use them as sandwich of nodes around your colour grade for preview only and separate one just for export.
In the Colour Tab node graph, use `Alt + S` to create 3-node sequence, right click on node and select listed LUT's.

Preview:
- First node: `Linear to Unity Log r1`
- Middle node(s): Your creative colour grading
- Last node: `Unity Log to sRGB r1`

Export:
- First node: `Disabled`
- Middle node(s): Your creative colour grading
- Last node: `Disabled`
- Export node: `Unity Log to Linear r1`

---

### CST sandwich method
Instead of using acquired LUT's on preview nodes you can use custom Color Space Transform effect

Preview:

First node (add this in node effects):

![CST Input](</Documentation/CST Input.png> "CST Input")

Middle node(s): Your creative colour grading

Last node (add this in node effects) :

![CST Input](</Documentation/CST Output.png> "CST Output")

Export:
- First node: `Disabled`
- Middle node(s): Your creative colour grading
- Last node: `Disabled`
- Export node: Unity Log to Linear r1

> Easy method will match with game preview the best others deviate little more, but CST method would take better advantage of HDR range of EXR screenshot.


--- 
</details>

Super resolution images are minimally brighter than normal native game view, probably because autoexposure have more pixels to accurately measure from, so you can do final check without super resolution screenshot.

> [!TIP]
> Last thing for the best comparison is to reduce the bloom effect intensity to **0** in the Photomode, or with **Lumina NGX** for more persistence.
Temporarily enable **NGX** in **Lumina** options, in mod panel select ***Post Processing Settings > Bloom > intensity**, click on **>_** symbol set it to **0**.
You can save this change as NGX Preset for later too.

--- 

### LUT Design


You can use any tools that operate globally on colour itself rather than pixels, otherwise they will not have any effect in exported LUT.
All the tools you need are in Color tab of DaVinci **Shift + 6**

| Have effect on LUT                       | No effect on LUT  |
|------------------------------------------|-------------------|
| Col Boost, Sat, Hue, Temp, Tint          | Mid/detail        |
| Contrast, Pivot                          | Hi/Light          |
| Primaries Wheels, Log Wheels, HDR Wheels | Shad              |
| RGB mixer                                | Qualifier, Keying |
| Curves ( Master, Hue vs Hue etc.)        | Masking, Windows  |
| Color Slice                              | Sharpening        |
| Color Warper                             | Blurring           |


Crucial thing in you establish your exposure and contrast early in node graph.
If you export the LUT with initial project setup you will get LUT that will be equivalent to tonemapper set to "None".

Most creative part is how you will treat the colours, after you establish your foundation this where majority of your vision comes in.

You can split your Creative Look into couple separate nodes, for organization. For example:

> Contrast > Saturation > Adjustment > Creative Look > Final Adjustment

Consider that your LUT will be used in different lighting conditions. Give your look room to breathe, avoid pushing the look as hard as possible, that you would for fixed video footage.

---

### Exporting the LUT for the Game

In DaVinci Resolve, inside the colour tab you are in, right-click your graded clip and select:

**Generate LUT (CUBE) > 33-Point Cube**, save it in **ModsData\Lumina\LUTs** folder.

> [!TIP]
> If you ever want to import your LUT back into DaVinci, a 33-point resolution LUT will degrade. For that case, you should export at 65-point resolution instead.

---

### Using LUT in Game

To prevent seasonal climate colour overrides affecting your custom LUT use it this way:

1. Open Lumina menu and tick **ENABLE LUMINA VOLUME**
2. Set LUT Contribution to **1**
3. Zero out conflicting parameters: Set Post Exposure, Contrast, Saturation, Hue, Tint, and Temp to **0**[^1]
4. Set Shadows, Midtones Highlights to **0.2385156**[^2]
5. Set the **Tonemapping Mode** to **External** select your **LUT**  from the list, and click **Load LUT**

If you are using RZColor you should also disable the settings for now.

If everything went right, your LUT should look almost exactly as you saw it in DaVinci.

You can also share preset later in LUT Pack mod later, basic guide how to make pack is in official Lumina wiki, my LUT Packs have code slightly modified to support sharing presets.

---

### Links
- [Mod page](https://mods.paradoxplaza.com/mods/139839/Windows)
- [Mod discussion on Paradox forum](https://forum.paradoxplaza.com/forum/threads/exr-screenshot.1916082/)
- [Technical deeper dive](TECHNICAL.md)
- [Source code on GitHub](https://github.com/DamiancioSkylines/cities2-EXR-Screenshot)
- [Making LUT Pack](https://skylinx.gitbook.io/lumina/packages/publish-lut-pack-to-paradox-mods)
- [Ko-Fi](https://ko-fi.com/damiancioskylines)

[^1]: Vanilla sunset climate prefab automatically pushes Contrast to positive value, your custom LUT can suddenly look crushed and ruined in-game, even though it looked flawless in Resolve. Be aware that Climate volumes vary significantly by season and their type (Boreal vs. Tropical vs. Standard) and game feature specific overrides for Dusk, Dawn, Day, and Night, and they will change parameters.

[^2]: Each of those is Vector made of 4 components, first 3 are RGB channels and last one is Weight, the game's internal Season Colour Overrides use three separate RGB float values rather than a single value for each channel, with weight usually set to 0. While Photo mode set all three channels to 1 and uses weight component instead, acting more like exposure offset in range of shadows midtones or highlights. This vector acts as multiplier, value in gamma space that is converted and applies in linear space, and Lumina sets all 4 components at once to singular value, so to get identity multiplier of 1, I did read the code line by line and did the math by hand, plot it on graph and that's the closest to neutral value you can get, with  +0.0000527% overshoot, until Lumina patches.
