# EXR Screenshot Mod  Technical Reference
## Cities Skylines 2 / HDRP 14 Linear Capture Pipeline

This document traces the complete rendering pipeline from photons to pixels, sourced directly from decompiled game assemblies and verified against `Color.hlsl`. It is intended for colorists who need to understand the exact state of the captured buffer in order to author a LUT or grade in DaVinci Resolve.

---

## 1. Capture Buffer Specification

| Property | Value |
| :--- | :--- |
| **Buffer** | `cameraColorBuffer` at `CustomPassInjectionPoint.BeforePostProcess` |
| **Format** | `B10G11R11_UFloatPack32` (RGB111110Float) |
| **Encoding** | Linear scene-referred, HDR, unsigned float, **no alpha channel** |
| **Bit depth** | 11/11/10 bits per channel (R/G/B) — shared 32 bits |
| **Dynamic range** | Unclamped positive values; scene radiance in exposure-normalized units |
| **Color space** | Linear (no gamma applied, no tonemapping) |

> **Note on format precision:** The R11G11B10 format has a maximum value of ~65504 (same as fp16). It cannot represent negative values. Any highly specular or very dark pixels may lose precision compared to a full fp16 capture, but for scene-linear LUT authoring this is sufficient.

---

## 2. The Volume Stack — Priority Order

CS2 manages post-processing through multiple `Volume` objects. `VolumeManager` blends them in priority order — higher priority wins any parameter conflict. All game-owned runtime volumes use `HideFlags.DontSave` or `DontDestroyOnLoad`, which is why they are invisible to `Object.FindObjectsByType<Volume>()`.

| Priority | Volume Name | Visibility | Owner System | Key Parameters |
| :--- | :--- | :--- | :--- | :--- |
| **-1** | `Render Settings` (scene asset) | Visible | Game scene | Sky (PhysicallyBasedSky), Volumetric Fog, SSAO, SSR, Cloud Layer, Wind |
| **-1** | `Post Processing Settings` (scene asset) | Visible | Game scene | Exposure (AutomaticHistogram, limitMin −5, limitMax +15), Bloom, ACES Tonemapping, Motion Blur, Film Grain, Shadows/Midtones/Highlights |
| **50** | `ClimateControlVolume` | **Hidden** | `ClimateRenderSystem` | Weather colour overrides blended from `WeatherPrefab.overrideableProperties`: ColorAdjustments (PostExposure, Contrast, ColorFilter, HueShift, Saturation), ShadowsMidtonesHighlights, WhiteBalance, VolumetricClouds, DistanceClouds, Fog, AtmosphereProperties |
| **51** | `CameraControllerVolume` | **Hidden** | `CameraUpdateSystem` | Depth of Field, cascade shadow settings |
| **1000** | `LightingPostProcessVolume` | Visible | `LightingSystem` | **Exposure** (AutomaticHistogram, limitMin/Max animated by time-of-day), **ColorAdjustments** (colorFilter + contrast animated Night → Sunrise → Day → Sunset via `DayNightCycleData`), **Tonemapping** (External LUT mode, `lutTexture` dynamically blended between NightLUT / SunriseLUT / DayLUT via `DayNight/LUTBlend.compute`), `IndirectLightingController`, `PhysicallyBasedSky` tints |
| **1980** | `LuminaVolume` | Visible | `RenderEffectsSystem` | **Global Overrides:** Lighting Dimmers, Ambient Occlusion, Color Adjustments, Custom Tonemapping, Volumetric Cloud & Shadow parameters |
| **2000** | `CinematicControlVolume` | **Hidden** | `PhotoModeRenderSystem` | Photo Mode / Cinematic camera: ColorAdjustments, WhiteBalance, ShadowsMidtonesHighlights, Vignette, Bloom, MotionBlur, DoF, Fog, Sky, FilmGrain, DistanceClouds, VolumetricClouds. **Weight = 0 when Photo Mode is inactive.** |

### Non-Volume Visual Modifiers (Simulation Level)

Some mods operate at the **Simulation/ECS level** and do not appear in the Volume stack because they modify source data *before* the rendering engine reads it.

**Time and Weather Anarchy** — Direct ECS injection into `ClimateSystem` and `PlanetarySystem`. It does not create an HDRP Volume; instead it overrides raw simulation variables (`Precipitation`, `Cloudiness`, `Time`). Because it changes the data that `ClimateControlVolume` (priority 50) uses to calculate its visual weights, the effects are fully realized in the captured buffer.

### Vanilla gameplay (no Photo Mode, no Lumina)
The effective grading stack in order of increasing authority is:

```
Post Processing Settings (base exposure limits, ACES fallback)
  ↓ overridden by
ClimateControlVolume (season & weather colour: SpringColorsOverride, etc.)
  ↓ overridden by
LightingPostProcessVolume (time-of-day colour filter, contrast, LUT blend)
```

### Technical Capture Note

The EXR is captured at `BeforePostProcess`, so it contains the full results of Lumina's lighting/cloud overrides and Time and Weather Anarchy's atmospheric changes. Post-processing effects (Bloom, Grain, Tonemapping, etc.) are logged in the metadata but are **not** baked into the EXR pixels.

---

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

`LightingPostProcessVolume` (priority 1000) overrides `colorFilter` and `contrast` for any parameters it has active. The climate volume's **PostExposure** is the only PostExposure contributor under vanilla gameplay (LightingPostProcessVolume does not set PostExposure), making it directly relevant to your EXR offset.

---

## 4. The LightingSystem Time-of-Day LUT Blend

`LightingSystem` manages a three-way GPU LUT blend on `LightingPostProcessVolume` (priority 1000) using `DayNight/LUTBlend.compute`:

```
Night ──────→ SunriseAndSunsetLUT ──────→ DayLUT ──────→ SunriseAndSunsetLUT ──────→ Night
```

This blended `Texture3D` is assigned to `Tonemapping.lutTexture` (mode = External). The strength is controlled by `lutContribution` (default 0.5 in vanilla). Simultaneously, `ColorAdjustments.colorFilter` and `.contrast` are animated through Night/Sunrise/Day/Sunset values.

**The exposure window is also narrowed at night:**
- Day: `limitMin = DayExposureMin`, `limitMax = DayExposureMax`
- Night: `limitMin = lerp(NightExposureLowMin, DayExposureMin, delta)`, `limitMax = NightExposureMax`

---

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
       • Color Filter   (multiplied in linear)
       • Channel Mixer  (R/G/B cross-channel matrix)
       • Hue Shift      (hueShift / 360)
       • Saturation     (saturation / 100 + 1)
       • Contrast       (contrast / 100 + 1)
       • Lift / Gamma / Gain
       • Shadows / Midtones / Highlights  (values converted to linear first)
       • Split Toning
       • Curves: Master, R, G, B, HueVsHue, HueVsSat, LumVsSat, SatVsSat
       • Tonemapping:   ACES_APPROX / ACES_FULL / Neutral / Custom Hable /
                        External LUT (blended at lutContribution)
     Result: an accumulated 32³ texture encoding the full grade + tonemapper.
     THIS IS HASH-CACHED — only re-dispatches when a parameter changes.

[15] Lens Flare data-driven compositing pass

[16] ── UberPass (UberPost.compute "Uber") ────────────────────────────────────
     Single compute dispatch; operates on every pixel:
       • Lens Distortion
       • Chromatic Aberration
       • Vignette  (procedural or masked)
       • Bloom compositing  (adds bloomTexture × bloomParams)
       • PostExposure gain:  z = pow(2, postExposure.value)
       • Log mapping:        colorLutSpace = saturate(LinearToLogC(color × z))
       • LUT sampling:       SampleLUT3D(logLut, colorLutSpace)
     Output: fully graded, tonemapped, bloom-composited result

[17] Debug: push ColorLog fullscreen debug texture (if enabled)
[18] CustomPostProcessPass    VolumeComponent-based: AfterPostProcess slot
[19] FXAAPass                 (if dynamic resolution + FXAA enabled)
[20] FinalPass                → backbuffer, with optional flip-Y
```

### The EXR Capture Point

`CustomPassInjectionPoint.BeforePostProcess` runs **before** `RenderPostProcess()` is called — i.e., before step [1] above.

---

## 6. What Is (and Is Not) in the EXR

### ✅ Included in the capture buffer

| Item | Notes |
| :--- | :--- |
| Full HDR scene rendering | All deferred / forward geometry, indirect lighting, GI |
| Direct and indirect lighting | Sunlight, moonlight, streetlights, emissives |
| Volumetric fog and atmosphere | Physically-based sky integration |
| SSR / SSGI | Screen-space reflections and global illumination |
| All particle / VFX effects | Rain, snow, aurora, etc. |
| Auto-exposure from previous frame | The scene buffer is **pre-exposed**: shaders multiply scene radiance by the exposure value from the prior frame's `DynamicExposurePass` texture. One-frame adaptive lag; negligible for a steady scene. |

### ❌ Not included — applied after the capture point

| Item | Applied in |
| :--- | :--- |
| **TAA** | Step [5] — inside `RenderPostProcess` |
| **Depth of Field** | Step [7] |
| **Motion Blur** | Step [9] |
| **Bloom** | Step [13] — composited in UberPass |
| **White Balance** | Step [14] — baked into 3D LUT |
| **Contrast / Saturation / Hue Shift** | Step [14] — baked into 3D LUT |
| **Shadows / Midtones / Highlights** | Step [14] — baked into 3D LUT |
| **Color Filter** | Step [14] — baked into 3D LUT |
| **Tonemapping (ACES / game LUT)** | Step [14] — baked into 3D LUT |
| **PostExposure** | Step [16] UberPass — `color × pow(2, postExposure)` |
| **Log mapping** | Step [16] UberPass — `LinearToLogC` |
| **Vignette** | Step [16] UberPass |
| **Chromatic Aberration** | Step [16] UberPass |
| **Film Grain** | Step [20] FinalPass |

---

## 7. Reading the Metadata File

Each EXR is accompanied by a `.txt` file from `VolumeInspection.GetActiveMetadata()`, reading live values from `ClimateRenderSystem.fromWeatherPrefabs` and `toWeatherPrefabs`.

**`[FROM]` list** — weather prefabs currently fading *out*  
**`[TO]` list** — weather prefabs currently fading *in*

If FROM and TO are identical, the weather state is stable (no active transition).

```
[FROM][0] WeatherPrefab: DefaultContinental
  Component: ColorAdjustmentsProperties
    PostExposure = 0.5          ← applied in UberPass, NOT in your EXR
    Contrast = 0                ← baked into 3D LUT
    ColorFilter = (1,1,1,1)     ← baked into 3D LUT, multiplied in linear
    HueShift = 0
    Saturation = 0
  Component: ShadowsMidtonesHighlightsProperties
    Shadows = (1,1,1,0)         ← baked into 3D LUT
    ...
  Component: WhiteBalanceProperties
    Temperature = 0             ← baked into 3D LUT (LMS coefficients)
    Tint = 0
```

> **Important:** The climate volume sits at priority 50. `LightingPostProcessVolume` at priority 1000 overrides **Contrast** and **ColorFilter** during day/night transitions. The final baked values depend on the complete blended volume stack, not the climate prefab values in isolation.

---

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

A 32³ LUT has 32 divisions per axis — 32,768 nodes total. In linear space, the shadow region (0.0–0.1) receives only ~3 of those 32 steps, causing posterisation in dark areas. In LogC space the steps redistribute perceptually, giving roughly equal grid density across shadows, midtones, and highlights. This is why `LinearToLogC` is mandatory before LUT sampling, and why you must match it in Resolve.

---

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

---

## 11. Technical Verification & Further Reading

- **[Unity HDRP: Custom Pass Injection Points](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass-Injection-Points.html)** — confirms `BeforePostProcess` state (after lighting/transparency, before tonemapping/AA)
- **[Unity HDRP: Customizing with Custom Passes](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass.html)** — details how `CustomPassContext` provides zero-copy access to internal `RTHandle` buffers



# 📸 Professional Workflow: Linear HDR & LUT Creation

## 1. The Color Adjustments Rule: "Zeroing" the Engine

In Unity HDRP, parameters like **Post Exposure, Contrast, Hue Shift, and Saturation** belong to the `ColorAdjustments` volume. Because this mod captures the frame at `BeforePostProcess`, **none of these adjustments are baked into your EXR.** The EXR is completely immune to them.

However, you must still manage them for your custom LUT to look correct in-game.

**The "Double Grade" Problem:**
If you author a perfect LUT in DaVinci Resolve based on your pure EXR, and then load it into the game, the engine doesn't just apply your LUT. It applies your LUT *plus* whatever `ColorAdjustments` are active in the current weather/climate prefab or Lumina profile.

If the vanilla sunset climate prefab automatically pushes Contrast to +20, your custom LUT will suddenly look crushed and ruined in-game, even though it looked flawless in Resolve.

**The Solution:**
To ensure what you see in DaVinci Resolve is exactly a 1:1 match with what you see in-game, you must prevent the engine from adding its own color math on top of your LUT.

Before testing your LUT in-game, use the **Lumina** mod to lock the following values to their neutral baseline (`0`):
* Post Exposure = 0
* Contrast = 0
* Hue Shift = 0
* Saturation = 0
* Colour Filter = White (FFFFFF) If its ever get added.
* Shadows, Midtones, & Highlights: To your desired static value.
Note: Even though Lumina uses single-value sliders, the game's internal Season Colour Overrides are not neutral they use RGB values not single value. Manually setting them a consistent baseline regardless of the current in-game season.


By locking these to zero, you bypass the game's dynamic color adjustments entirely, ensuring your custom LUT is the *only* thing grading the final image.
---

## 2. Mastering the DaVinci Resolve Workflow

When you set Lumina's Tonemapping Mode to "External", Unity expects a **Log-encoded 3D LUT**. If you author a LUT using standard sRGB linear math, you will ruin the image's shadow precision and clip the highlights.

You must map the linear EXR into Unity's internal working color space: **ARRI Alexa LogC (Exposure Index 1000)**. You can do this using exact math, or by using Unity's official sample LUTs.

### Project Setup in Resolve 
* **Color Science:** DaVinci YRGB
* **Timeline Color Space:** Rec.709 (Scene)
* **Output Color Space:** sRGB

### Method A: Using Unity's Official LUTs
You need extract the exact transform LUTs directly from the Unity Engine:
1. Open Unity Hub and create a "High Definition 3D sample" project.
2. Go to **Window > Package Manager**, select **High Definition RP**.
3. Under Samples, click **Import** next to **Additional Post-processing Data**.
4. Navigate to: `Assets\Samples\High Definition RP\[Version]\Additional Post-processing Data\Cube LUTs`.
5. Copy `Linear to Unity Log r1.cube` and `Unity Log to sRGB r1.cube` and all the rest of LUTs into your DaVinci Resolve LUT folder.

### Method B: The Exact Engine Math (DCTL / CST) The Unity LogC Transform
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

<div align="center">

**Forward Transform (Linear → LogC):**

$$
f(x) = \begin{cases}
0.244161 \cdot \log_{10}(5.555556 \cdot x + 0.047996) + 0.386036 & \text{if } x > 0.011361 \\
5.301883 \cdot x + 0.092819 & \text{if } x \leq 0.011361
\end{cases}
$$

**Inverse Transform (LogC → Linear):**

$$
f^{-1}(x) = \begin{cases}
\dfrac{10^{(x - 0.386036)\ /\ 0.244161} - 0.047996}{5.555556} & \text{if } x > e \cdot \text{cut} + f \\
\dfrac{x - 0.092819}{5.301883} & \text{otherwise}
\end{cases}
$$

</div>

> The shader's `USE_PRECISE_LOGC` define defaults to `0`, meaning the fast path omits the linear segment for the majority of pixels. For LUT authoring the precise piecewise version is more accurate in deep shadows and should be preferred in your DCTL.

**Implementation in DaVinci Resolve:** Use a Color Space Transform (CST) node set to **Input: Linear** / **Output: ARRI LogC**, or implement the DCTL directly with the constants above. The standard ARRI LogC3 EI1000 preset in Resolve uses the same specification.

### The Node Pipeline
Import your EXR screenshots combine them into single compound clip and build this exact node tree in the Color Tab:

1.  **Node 1 (Input Transform):** Apply your Linear-to-LogC math, OR apply the `Linear to Unity Log r1` LUT.
2.  **Nodes 2 to X (The Grade):** Do your creative grading here (Exposure, Saturation, Balance). *Crucial: You are now grading in Log space, capturing the full HDR dynamic range.*
3.  **Final Node (Output Transform):** Map back to your display space. Apply the `Unity Log to sRGB r1` LUT.

*Why this works:* Your exported LUT will contain the mathematical "sandwich" of receiving a Log image, applying your grade, and tonemapping it down to sRGB for the player's monitor.

---

## 3. Exporting the LUT for the Game

1. In DaVinci Resolve, right-click your graded clip and select **Generate LUT (CUBE) > 33 Point Cube**.
2. **No Conversion Needed:** While Unity's HDRP natively utilizes a 32³ LUT grid, you *do not* need to use an external tool to convert your 33-point export. The **Lumina** mod features a built-in trilinear interpolator (`CubeLutLoader.cs`) that automatically resamples any size LUT down to a perfect 32³ grid upon import.
3. Drop your generated `.cube` file into your `ModsData\Lumina\LUTS` folder.
4. In-game, open Lumina, set **Tonemapping Mode to External**, and load your new LUT.

---

## 💡 Final Note: The Philosophy of Linear Grading

By using this mod and workflow, you are moving away from the "Instagram Filter" style of color grading common in the original *Cities: Skylines*. Instead, you are adopting a **Scene-Linear Workflow** used in major motion pictures and AAA game development.

**Why does this matter?** In a linear workflow, light behaves like light. When you increase "Gain" in Resolve, you are mathematically increasing the radiance of the sun and streetlamps, not just brightening pixels. This preserves the subtle gradients in your atmosphere and prevents the "chalky" or "deep-fried" look that occurs when grading compressed 8-bit screenshots.

This mod is a tool for the "Virtual Colorist." As we continue to decode the intricacies of the CS2 engine, this methodology will improve. If you discover a better node sandwich or a specific DCTL that yields better results, please share it with the community!

**Happy Grading!** 🎨
