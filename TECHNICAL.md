## Cities Skylines 2 / HDRP 14 Linear Capture Pipeline

This document traces the complete rendering pipeline, sourced directly from decompiled game assemblies and verified against `Color.hlsl`. It is intended for colourists and nerds who want to understand the exact state of the captured buffer more.

## 1. Capture Buffer Specification

| Property | Value |
| :--- | :--- |
| **Buffer** | `cameraColorBuffer` at `CustomPassInjectionPoint.BeforePostProcess` |
| **Format** | `B10G11R11_UFloatPack32` (RGB111110Float) |
| **Encoding** | Linear scene-referred, HDR, unsigned float, **no alpha channel** |
| **Bit depth** | 11/11/10 bits per channel (R/G/B) - shared 32 bits |
| **Dynamic range** | Unclamped positive values; scene radiance in exposure-normalized units |
| **Color space** | Linear (no gamma applied, no tonemapping) |

> [!Note]
> **Format precision:** The R11G11B10 format has a maximum value of ~65504 (same as fp16). It cannot represent negative values. Any highly specular or very dark pixels may lose precision compared to a full fp16 capture, but for scene-linear LUT authoring this is sufficient.

## 2. The Volume Stack - Priority Order

CS2 manages post-processing through multiple `Volume` objects. `VolumeManager` blends them in priority order - higher priority wins any parameter conflict. All game-owned runtime volumes use `HideFlags.DontSave` or `DontDestroyOnLoad`, which is why they are invisible to `Object.FindObjectsByType<Volume>()`.

| Priority | Volume Name | Visibility | Owner System | Key Parameters |
| :--- | :--- | :--- | :--- | :--- |
| **-1** | `Render Settings` (scene asset) | Visible | Game scene | Sky (PhysicallyBasedSky), Volumetric Fog, SSAO, SSR, Cloud Layer, Wind |
| **-1** | `Post Processing Settings` (scene asset) | Visible | Game scene | Exposure (AutomaticHistogram, limitMin −5, limitMax +15), Bloom, ACES Tonemapping, Motion Blur, Film Grain, Shadows/Midtones/Highlights |
| **50** | `ClimateControlVolume` | **Hidden** | `ClimateRenderSystem` | Weather colour overrides blended from `WeatherPrefab.overrideableProperties`: ColorAdjustments (PostExposure, Contrast, ColorFilter, HueShift, Saturation), ShadowsMidtonesHighlights, WhiteBalance, VolumetricClouds, DistanceClouds, Fog, AtmosphereProperties |
| **51** | `CameraControllerVolume` | **Hidden** | `CameraUpdateSystem` | Depth of Field, cascade shadow settings |
| **1000** | `LightingPostProcessVolume` | Visible | `LightingSystem` | **Exposure** (AutomaticHistogram, limitMin/Max animated by time-of-day), **ColorAdjustments** (colorFilter + contrast animated Night → Sunrise → Day → Sunset via `DayNightCycleData`), **Tonemapping** (External LUT mode, `lutTexture` dynamically blended between NightLUT / SunriseLUT / DayLUT via `DayNight/LUTBlend.compute`), `IndirectLightingController`, `PhysicallyBasedSky` tints |
| **2000** | `CinematicControlVolume` | **Hidden** | `PhotoModeRenderSystem` | Photo Mode / Cinematic camera: ColorAdjustments, WhiteBalance, ShadowsMidtonesHighlights, Vignette, Bloom, MotionBlur, DoF, Fog, Sky, FilmGrain, DistanceClouds, VolumetricClouds. **Weight = 0 when Photo Mode is inactive.** |
| **4900** | `RZColor` | Visible | `RenderEffectsSystem` | **Global Overrides:** LiftGammaGain, ColorCurves, ChannelMixer |
| **5000** | `LuminaVolume` | Visible | `RenderEffectsSystem` | **Global Overrides:** Lighting Dimmers, Ambient Occlusion, Color Adjustments, Custom Tonemapping, Volumetric Cloud & Shadow parameters |

### Non-Volume Visual Modifiers (Simulation Level)

Some mods operate at the **Simulation/ECS level** and do not appear in the Volume stack because they modify source data *before* the rendering engine reads it.

**Time and Weather Anarchy** - Direct ECS injection into `ClimateSystem` and `PlanetarySystem`. It does not create an HDRP Volume; instead it overrides raw simulation variables (`Precipitation`, `Cloudiness`, `Time`). Because it changes the data that `ClimateControlVolume` (priority 50) uses to calculate its visual weights, the effects are fully realized in the captured buffer.

### Vanilla gameplay (no Photo Mode, no Lumina)
The effective grading stack in order of increasing authority is:

```
Post Processing Settings (base exposure limits, ACES)
  ↓ overridden by
ClimateControlVolume (season & weather colour: SpringColorsOverride, etc.)
  ↓ overridden by
LightingPostProcessVolume (time-of-day colour filter, contrast)
```

### Technical Capture Note

The EXR is captured at `BeforePostProcess`, so it contains the full results of Lumina's lighting/cloud overrides and Time and Weather Anarchy's atmospheric changes. Post-processing effects (Bloom, Grain, Tonemapping, etc.) are logged in the metadata but are **not** baked into the EXR pixels.

## 3. The Climate Colour Override System

`ClimateSystem` → `ClimateRenderSystem` → `WeatherPropertiesStack`

Each game frame, `ClimateSystem` selects a set of `WeatherPrefab` objects representing the current season/weather. `ClimateRenderSystem` reads their `overrideableProperties` collections and calls `WeatherPropertiesStack.InterpolateOverrideData()` to blend between "from" and "to" states. The result is written directly to `ClimateControlVolume` (priority 50).

**The blended properties that affect colour grading:**

- `ColorAdjustmentsProperties` → PostExposure, Contrast, ColorFilter, HueShift, Saturation
- `ShadowsMidtonesHighlightsProperties` → Shadows, Midtones, Highlights and their limits
- `WhiteBalanceProperties` → Temperature, Tint
- `VignetteProperties` → Intensity, Roundness, etc.

**Example from Spring scene:**
```
DefaultContinental:   PostExposure=0.5, Contrast=0
SpringColorsOverride: PostExposure=0.7, Contrast=100, Saturation=26, WhiteBalance Temp=+3, Vignette=0.25
```

`LightingPostProcessVolume` (priority 1000) overrides `colorFilter` and `contrast` for any parameters it has active. The climate volume's **PostExposure** is the only PostExposure contributor under vanilla gameplay (LightingPostProcessVolume does not set PostExposure), making it directly relevant to your EXR offset, if you want to simulate it while authoring.

## 4. The LightingSystem Time-of-Day (And the Inactive LUT Blend)

`LightingSystem` manages a three-way GPU LUT blend on `LightingPostProcessVolume` (priority 1000) using `DayNight/LUTBlend.compute`:

```
Night ──────→ SunriseAndSunsetLUT ──────→ DayLUT ──────→ SunriseAndSunsetLUT ──────→ Night
```

`LightingPostProcessVolume` (priority 1000). While the engine contains logic (DayNight/LUTBlend.compute) to manage a three-way GPU LUT blend between Night, Sunrise/Sunset, and Day states, these LUTs are never actually enabled in vanilla gameplay.

Instead, the system relies purely on mathematical parameter animations: ColorAdjustments.colorFilter and .contrast are animated through Night/Sunrise/Day/Sunset values.

**The exposure window is also narrowed at night:**
- Day: `limitMin = DayExposureMin`, `limitMax = DayExposureMax`
- Night: `limitMin = lerp(NightExposureLowMin, DayExposureMin, delta)`, `limitMax = NightExposureMax`

## 5. The Full Post-Process Pipeline (Execution Order)

Traced from `HDRenderPipeline.RenderPostProcess()`:

```
[1]  StopNaNsPass             Sanitises NaN and Inf pixels in the HDR buffer
[2]  DynamicExposurePass      Reads scene histogram, computes EV100, writes to
                              1×1 R32G32_SFloat exposure texture for NEXT FRAME
[3]  DLSS / FSR2              (BeforePost upscaling, if enabled)
[4]  CustomPostProcessPass    VolumeComponent-based: BeforeTAA slot
[5]  TAA / SMAA               Temporal or morphological anti-aliasing
[6]  CustomPostProcessPass    VolumeComponent-based: BeforePostProcess slot
[7]  DepthOfFieldPass         Defocus blur
[8]  DLSS / FSR2              (AfterDepthOfField, if enabled)
[9]  MotionBlurPass           Camera + per-object motion blur
[10] CustomPostProcessPass    VolumeComponent-based: AfterPPBlurs slot
[11] PaniniProjectionPass     Wide-angle lens correction
[12] Lens Flare               Compute occlusion pass
[13] BloomPass                Produces bloomTexture for UberPass

[14] ── ColorGradingPass (LutBuilder3D.compute "KBuild") ──────────────────────
     BAKES the following into a 32³ R16G16B16A16 log-encoded 3D LUT:
       • White Balance  (LMS colour coefficients from Temperature / Tint)
       • Contrast       (contrast / 100 + 1)
       • Color Filter   (multiplied in linear)
       • Split Toning
       • Channel Mixer  (R/G/B cross-channel matrix)
       • Shadows / Midtones / Highlights  (values converted to linear first)
       • Lift / Gamma / Gain
       • Hue Shift      (hueShift / 360)
       • Saturation     (saturation / 100 + 1)
       • Curves: Master, R, G, B, HueVsHue, HueVsSat, LumVsSat, SatVsSat
       • Tonemapping:   ACES_APPROX / ACES_FULL / Neutral / Custom Hable /
                        External LUT (blended at lutContribution)
     Result: an accumulated 32³ texture encoding the full grade + tonemapper.
     THIS IS HASH-CACHED - only re-dispatches when a parameter changes.

[15] Lens Flare data-driven compositing pass

[16] ── UberPass (UberPost.compute "Uber") ────────────────────────────────────
     Single compute dispatch; operates on every pixel:
       • Lens Distortion
       • Chromatic Aberration
       • Bloom compositing  (adds bloomTexture × bloomParams)
       • Vignette  (procedural or masked)
       • PostExposure gain:  z = pow(2, postExposure.value)
       • Log mapping:        colorLutSpace = saturate(LinearToLogC(color × z))
       • LUT sampling:       SampleLUT3D(logLut, colorLutSpace)
     Output: fully graded, tonemapped, bloom-composited result

[17] Debug: push ColorLog fullscreen debug texture (if enabled)
[18] CustomPostProcessPass    VolumeComponent-based: AfterPostProcess slot
[19] FXAAPass                 (if dynamic resolution + FXAA enabled)
[20] FinalPass                → backbuffer, with optional flip-Y
```

`CustomPassInjectionPoint.BeforePostProcess` runs **before** `RenderPostProcess()` is called - i.e., before step [1] above. Note DynamicExposurePass from preview frame will affect the EXR explained below and later.

## 6. What Is (and Is Not) in the EXR

### Included in the capture buffer

| Item | Notes |
| :--- | :--- |
| Full HDR scene rendering | All deferred / forward geometry, indirect lighting, GI |
| Direct and indirect lighting | Sunlight, moonlight, streetlights, emissives |
| Volumetric fog and atmosphere | Physically-based sky integration |
| SSR / SSGI | Screen-space reflections and global illumination |
| All particle / VFX effects | Rain, snow, aurora, etc. |
| Auto-exposure from previous frame | The scene buffer is **pre-exposed**: shaders multiply scene radiance by the exposure value from the prior frame's `DynamicExposurePass` texture. One-frame adaptive lag; negligible for a steady scene. |

### Not included - applied after the capture point

| Item                                                   | Applied in                                                      |
|:-------------------------------------------------------|:----------------------------------------------------------------|
| **TAA**                                                | Step [5] - inside `RenderPostProcess`                           |
| **Depth of Field**                                     | Step [7]                                                        |
| **Motion Blur**                                        | Step [9]                                                        |
| **Lens Distortion, Chromatic Aberration**              | Step [16] UberPass                                              |
| **Bloom**                                              | Texture produced at step [13], composited in step [16] UberPass |
| **Vignette**                                           | Step [16] UberPass                                              |
| **PostExposure**                                       | Step [16] UberPass - `color × pow(2, postExposure)`             |
| **Log mapping**                                        | Step [16] UberPass - `LinearToLogC`                             |
| **White Balance**                                      | Baked in step [14] - applied by the LUT lookup in [16]          |
| **Contrast**                                           | Same                                                            |
| **Color Filter**                                       | Same                                                            |
| **Split Toning, Channel Mixer**                        | Same                                                            |
| **Shadows, Midtones, Highlights**                      | Same                                                            |
| **Lift, Gamma ,Gain**                                  | Same                                                            |
| **Hue Shift, Saturation, Curves**                      | Same                                                            |
| **Tonemapping (ACES / none / Neutral / external LUT)** | Same                                                            |
| **Film Grain**                                         | Step [20] FinalPass                                             |

Rows are listed in the order a pixel goes through them, not by step number. Step [14] only builds the grading LUT, which is applied when [16] samples it.

## 7. Reading the Metadata File

Each EXR screenshot is accompanied by a .txt metadata log generated by VolumeInspection.GetActiveMetadata(). This log captures a snapshot of the combined effective render volume stack, individual scene volumes, and day/night configurations. It effectively documents the intended final post-processing state of the normal game view before the EXR capture intercepts the raw linear buffer.

* **`[EFFECTIVE RENDER VOLUME STACK PARAMETERS]`** - Shows the final, mathematically blended output of all active overlapping volumes sorted by priority and weighted accordingly.
* **`[SCENE VOLUMES LIST]`** - Lists all individual candidate volumes found in the scene and Photo Mode ECS systems, along with their priorities, weights, and overridden component parameters.
* **`[TIME, COORDINATE, SEASON/WEATHER]"`** - Shows simulation values directly from TimeSystem, PlanetarySystem, and ClimateSystem.
* **`[WEATHER PREFABS & SEASON OVERRIDES FOR CLIMATE CONTROL]`** - Shows active WeatherPrefab assets registered in the ClimateRenderSystem that dynamically drive the parameters of the procedural ClimateControlVolume, if currently in transition it will be correctly reflected in `[EFFECTIVE RENDER VOLUME STACK PARAMETERS]`.
* **`[DAY/NIGHT CYCLE]`** - Shows settings from the game's managed DayNightCycleData asset, including sun elevation boundaries, exposure limits.

Effective Render Volume Stack ParametersComputes the final, mathematically blended parameter values across all active scene volumes, accounting for volume weight ($0.0 \dots 1.0$), priority layer sorting, and active override states.

With this file you can review and trace down from where the specific value comes from.

### Example Snippet

```
[EFFECTIVE RENDER VOLUME STACK PARAMETERS]
   (Note: Post-processing parameters below are logged for reference, but are NOT baked into EXR pixels, more info on github page)
   Component: ColorAdjustments
      ColorFilter = RGBA(1.000, 1.000, 1.000, 1.000)
      Contrast = 10
      PostExposure = 0.5
   Component: ShadowsMidtonesHighlights
      Shadows = RGBA(1.000, 1.000, 1.000, 0.000)
      ...

[SCENE VOLUMES LIST]
   [Volume: LightingPostProcessVolume] Priority: 1000 | Weight: 1 | Enabled: True
      Component: ColorAdjustments
         PostExposure = 0.5
   [Volume: Custom Climate Control Volume] Priority: 50 | Weight: 1 | Enabled: True
      Component: WhiteBalance
         Temperature = 5
      ...
[TIME, COORDINATE, SEASON/WEATHER]
   Current Date Time = 06/21/2024 12:00:00
   normalizedTime = 0.500
   normalizedDate = 0.470
   latitude = 61.5
   longitude = 23.7
   wind = 1.2
   hail = 0
   rainbow = 0
   precipitation = 0
   temperature = 18.5
   cloudiness = 0.2
   aurora = 0
   fog = 0.05
   isRaining = False
   isSnowing = False
   isPrecipitating = False
   classification = Continental
   currentSeasonName = Summer 
   
[WEATHER PREFABS & SEASON OVERRIDES FOR CLIMATE CONTROL]
   (Note: These prefabs dynamically drive the parameters of the active ClimateControlVolume above)
   WeatherPrefab: DefaultContinental
      Override Component: ColorAdjustmentsProperties
         PostExposure = 0.5
      Override Component: WhiteBalanceProperties
         Temperature = 0
         ...
   
[DAY/NIGHT CYCLE]
   (Note: Defines sun angle thresholds, exposure caps, time filters, and 3D LUT textures that are never enabled afaik)
   Asset Name: DayNightData
      DawnStartAngle = -0.1
      SunriseMidpointAngle = 2
      SunriseEndAngle = 5
      SunsetStartAngle = 5
      SunsetMidpointAngle = 2
      ...
      MoonLightColor = RGBA(0.807, 0.851, 0.925, 1.000)
      MoonShadowTint = RGBA(0.000, 0.000, 0.000, 1.000)
      ...
      UseLUT = False
      DayLUT = Ava 614
      NightLUT = Byers 11
      SunriseAndSunsetLUT = Vireo 37
      LutContribution = 0.5
```
> [!IMPORTANT]
> The climate volume sits at priority 50. `LightingPostProcessVolume` at priority 1000 overrides **Contrast** and **ColorFilter** during day/night transitions. The final baked values depend on the complete blended volume stack, not the climate prefab values in isolation.


## 8. Data Integrity Reference

| Property | Legacy CS1 (.png) | This Mod (.exr) |
| :--- | :--- | :--- |
| **Bit depth** | 8-bit integer | 11/11/10-bit float (positive HDR) |
| **Encoding** | sRGB gamma | **Linear scene-referred** |
| **Exposure** | Auto + PostExposure + tonemapper baked | **Auto only** (PostExposure excluded) |
| **Tonemapping** | Fully baked, irreversible | **Not applied** |
| **Colour grade** | Fully baked | **Not applied** |
| **Dynamic range** | 0–1, clamped | **Unclamped HDR headroom** |
| **LUT authoring suitability** | None (circular reference) | **Direct source material** |

### Why Log encoding matters for 32³

A 32³ LUT has 32 divisions per axis- 32,768 nodes total. In linear space, the shadow region (0.0–0.1) receives only ~3 of those 32 steps, causing posterisation in dark areas. In LogC space the steps redistribute perceptually, giving roughly equal grid density across shadows, midtones, and highlights. This is why `LinearToLogC` is mandatory before LUT sampling, and why you must match it in Resolve.

## 9. Mod Capture Point in Context

```
Scene rendered (all lights, GI, SSR, fog, VFX)
    ↓  auto-exposure from PREVIOUS frame applied to radiance during shading
    ↓
CustomPassInjectionPoint.BeforePostProcess
    ↓  ← [EXR CAPTURED HERE]  cameraColorBuffer = R11G11B10 linear HDR
    ↓
RenderPostProcess() begins:
  StopNaNs → DynamicExposurePass → DLSS/FSR → BeforeTAA → TAA
  → DoF → MotionBlur
  → Bloom
  → ColorGradingPass (LutBuilder3D): bakes grade + tonemapper into 32³ logLUT
  → UberPass: PostExposure × LinearToLogC → sample logLUT → Vignette → Bloom add
  → FXAA → FinalPass → display
```

## 10. Technical Verification & Further Reading

- **[Unity HDRP: Custom Pass Injection Points](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass-Injection-Points.html)** - confirms `BeforePostProcess` state (after lighting/transparency, before tonemapping/AA)
- **[Unity HDRP: Customizing with Custom Passes](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass.html)** - details how `CustomPassContext` provides zero-copy access to internal `RTHandle` buffers

---

### **LUT pipeline**

Game relies on explicit engine pipeline when forming image with external LUT involved.

> [!IMPORTANT]
> scene linear Rec.709 (auto exposure) > effects > post exposure > all color adjustments > LinearToLogC (EI1000 0-1 range) > **External LUT** > linear Rec.709 > sRGB > Display

In Unity HDRP, parameters like **Post Exposure, Contrast, Hue Shift, and Saturation** belong to the `ColorAdjustments` volume. Because this mod captures the frame at `BeforePostProcess`, **none of these adjustments are baked into your EXR.** The EXR is completely immune to them.

Auto-Exposure Pipeline: The game uses AutomaticHistogram metering with a Center-Weighted mask, adapting over time constrained by **limitMin** and **limitMax** stops, targeting a 12.5% mid-grey average scene luminance.

Auto-exposure is applied in the EXR screenshot. Thats is what we want and will be always feed into our LUT in game, so both day a night have usable range, instead of being drastically over or underexposed.
Further this auto-exposure is applied during shading inside shader using the previous frame’s computed exposure value. The captured EXR therefore contains exposure-adjusted scene radiance ('pre-tonemapping'), with a one-frame adaptation delay that is typically imperceptible.

> [!NOTE]
> In Short EXR will have auto-exposure but not post-exposure, colour effects or any postprocess effects like bloom etc, but also not have any antialiasing baked into pixels. This is why you can take super resolution screenshot with the mod, and inside DaVinci viewport, it will look like its anti aliased image.

> [!TIP]
>  For better DaVinci view **Project settings > Image Scaling > Resize filter** set to **Smoother**.


## Creating LUT

To view your grade exactly in same way as game sees it, we can set up colour management matching what game does in its pipeline when using external LUT’s.
When you set Lumina's Tonemapping Mode to "External", Unity expects a **Log-encoded 3D LUT**. If you author a LUT using standard sRGB linear math, you will ruin the image's shadow precision and clip the highlights.


### Prerequisites
If you don't have Unity installed yet, just follow Modding Toolchain guide, you will need for making Mod Pack Mod later too.
[Modding Toolchain paradoxwiki](https://cs2.paradoxwikis.com/Modding_Toolchain)

When you ready, you need extract the exact transform LUT's directly from the Unity Engine:

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
In the Color tab of DaVinci **Shift + 6** node graph, use **Alt + S** to create 3-node sequence, right click on node and select listed LUT's.

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
- Export node: `Unity Log to Linear r1`

> Easy method will match with game preview the best others deviate little more, but CST method would take better advantage of HDR range of EXR screenshot, because official LUT transforms domains are clamped to 0-1.

### DCTL method (I cant test this myself, but should be enough info for those who can)
Pro colorists often prefer using math nodes for maximum precision. You can use a Color Space Transform (CST) set to *Linear input* and *ARRI LogC3 output*, or use a DCTL with Unity's exact `Color.hlsl` formula:

Unity uses the **ARRI Alexa LogC (Exposure Index 1000)** spec as its internal LUT working space. The constants and piecewise function below are taken verbatim from `Color.hlsl` in the HDRP package (`Alexa LogC converters (El 1000)`):

**Constants:**

| Constant | Value | Role |
| :--- | :--- | :--- |
| `cut` | `0.011361` | Threshold below which the linear segment applies |
| `a` | `5.555556` | Scale inside the log argument |
| `b` | `0.047996` | Offset inside the log argument |
| `c` | `0.244161` | Log slope |
| `d` | `0.386036` | Log offset |
| `e` | `5.301883` | Linear segment slope |
| `f` | `0.092819` | Linear segment offset |

Here, `x` represents the Linear pixel value, and the function  represents the resulting LogC value.

**Forward Transform (Linear → LogC):**

```text
if x > cut:
    LogC = c * log10(a * x + b) + d
else:
    LogC = e * x + f
```

**Inverse Transform (LogC → Linear):**

```text
if x > e * cut + f:
    Linear = (10 ^ ((x - d) / c) - b) / a
else:
    Linear = (x - f) / e
```

> The shader's `USE_PRECISE_LOGC` define defaults to `0`, meaning the fast path omits the linear segment for the majority of pixels. For LUT authoring the precise piecewise version is more accurate in deep shadows and should be preferred in your DCTL.

Implement the DCTL directly with the constants above.

--- 

</details>

Super resolution images are minimally brighter than normal native game view, probably because autoexposure have more pixels to accurately measure from, so you can do final check without super resolution screenshot.

> [!TIP]
> Last thing for the best comparison is to reduce the bloom effect intensity to **0** in the Photomode, or with **Lumina NGX** for more persistence.
Temporarily enable **NGX** in **Lumina** options, in mod panel select ***Post Processing Settings > Bloom > intensity**, click on **>_** symbol set it to **0**.
You can save this change as NGX Preset for later too.

--- 

> [!NOTE]
> This  is similar as in official Unity documentation "Step: 2" but our EXR is not in Log colour space but Linear colour space https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@10.8/manual/LUT-Authoring-Resolve.html


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

If you are using RZColor you should also disable the settings for testing your LUT.

If everything went right, your LUT should look almost exactly as you saw it in DaVinci.

You can also share preset later in LUT Pack mod, basic guide how to make pack is in official Lumina wiki, my LUT Packs have code slightly modified to support sharing presets.

---

### Links
- [Mod page](https://mods.paradoxplaza.com/mods/139839/Windows)
- [Mod discussion on Paradox forum](https://forum.paradoxplaza.com/forum/threads/exr-screenshot.1916082/)
- [README](README.md)
- [Source code on GitHub](https://github.com/DamiancioSkylines/cities2-EXR-Screenshot)
- [Modding Toolchain paradoxwiki](https://cs2.paradoxwikis.com/Modding_Toolchain)
- [Making LUT Pack](https://skylinx.gitbook.io/lumina/packages/publish-lut-pack-to-paradox-mods)
- [Ko-Fi](https://ko-fi.com/damiancioskylines)


### Final Note: Scene Linear Workflow

By using this mod and workflow, you are moving away from the "Instagram Filter" style of colour grading common in the original *Cities: Skylines*. Instead, you are adopting a **Scene-Linear Workflow** used in major motion pictures and AAA game development.

**Why does this matter?** In a linear workflow, light behaves like light. When you increase "Gain" in Resolve, you are mathematically increasing the radiance of the sun and streetlamps, not just brightening pixels. This preserves the subtle gradients in your atmosphere and prevents the "chalky", "deep-fried" or "cracking" look that occurs when grading compressed 8-bit screenshots.

If you discover a better node sandwich or a specific DCTL that yields better results, or any correction to this document please share it in CSM community!

[^1]: Vanilla sunset climate prefab automatically pushes Contrast to positive value, your custom LUT can suddenly look crushed and ruined in-game, even though it looked flawless in Resolve. Be aware that Climate volumes vary significantly by season and their type (Boreal vs. Tropical vs. Standard) and game feature specific overrides for Dusk, Dawn, Day, and Night, and they will change parameters.

[^2]: Each of those is Vector made of 4 components, first 3 are RGB channels and last one is Weight, the game's internal Season Colour Overrides use three separate RGB float values rather than a single value for each channel, with weight usually set to 0. While Photo mode set all three channels to 1 and uses weight component instead, acting more like exposure offset in range of shadows midtones or highlights. This vector acts as multiplier, value in gamma space that is converted and applies in linear space, and Lumina sets all 4 components at once to singular value, so to get identity multiplier of 1, I did read the code line by line and did the math by hand, plot it on graph and that's the closest to neutral value you can get, with  +0.0000527% overshoot, until Lumina patches.

