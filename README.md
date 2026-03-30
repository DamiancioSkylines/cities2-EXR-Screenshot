# 📸 Professional Workflow: Linear HDR & LUT Creation

> [!IMPORTANT]
> **WORK IN PROGRESS (WIP):**
> This mod and the associated colour grading workflow are not final. Methodologies, and recommended DaVinci Resolve transforms may change as I further understand the HDRP pipeline. This methodology is new and not battle-tested—but if you want to be on the colour bleeding edge, this will get you started.

## Quick Concept Summary
* **The EXR is the "Negative":** It’s the raw light data. It is not supposed to look like a "finished" image when you open it.
* **The Metadata is the "Notes":** It tells you how the engine was "thinking" during the capture (check the debug logs for even more info).
* **The Goal:** You get a professional base for creating LUTs that will help you achieve exactly the same look in DaVinci as will do when imported into the game's actual Post-Processing stack.
* **Essentially:** The Mod provides the pure light data, without post-processing.

---

## 1. Why capture "Linear" data?
Standard screenshots are "baked." They already include the game's contrast, post-exposure, saturation (aka photo mode settings or Lumina colour adjustment settings or climate prefab settings), colour space transforms and final tonemapping.

If you create a LUT from a standard screenshot, you are grading on top of an already "final" image. It’s essentially **"double LUTing"**—trying to achieve a look on top of a look that was already forced onto the pixels.

While many *Cities: Skylines 1* LUTs were made this way, it was only "passable" because they were applied in gamma space in URP, acting more like Instagram filters than professional colour grading anyway. *Cities: Skylines 2* uses **HDRP**, a vastly superior pipeline. To make a high-quality LUT, you must treat the engine with more nuance. A true LUT is applied to the linear/log HDR lighting colour buffer, not an 8-bit PNG. Ignoring this wastes the potential of the game's modern rendering.

### This mod captures the data BEFORE Post-Processing:

* **Bit-Depth:** I export `R16G16B16A16_SFloat` data. *Note: Due to missing header metadata in Unity's EXR encoder, external software might report it as 32-bit, but the data itself is high-precision 16-bit.* The mod grabs this directly from the GPU. Since the game internally renders this buffer in `R11G11B10`, this 16-bit file ensures absolutely zero data loss.
* **Temporal Effects & Aliasing:** Because I grab the buffer before post-processing, there is no denoising, antialiasing, or regular temporal accumulation for effects like SSGI. The mod waits 2 frames for these to settle a bit (I can give you more wait frames options if anyone wants). 
* **Super Resolution:** Use the Super Resolution setting to effectively fake anti-antialiasing in your screenshot. The mod will temporarily turn off all upscaling tech and force native resolution and then multiplies it, giving you vastly more data for your DaVinci workflow.
* **Linear Math:** Pixel values represent the actual intensity of light in the scene, not just "screen brightness."
* **Zero Clamping:** Highlights (like the sun or streetlamps) can have values way above 1.0. This "headroom" allows you to recover cloud details or adjust exposure in post with zero quality loss.

*(Thus why the histogram will not be perfectly comb-free to my best understanding, and the nature of the whole beast).*

---

## 2. Explaining the Metadata (.txt)
Every EXR comes with an extra text file containing the "Camera Settings" at the exact moment of capture. Also, you can turn on more detailed logging to see some more info if you are interested, but this probably needs some more work or move to the metadata file.

Screenshots are affected by **Automatic Histogram**—it's like or is the auto-exposure/eye adaptation before the final user post-exposure happens in photo mode settings, climate prefabs, or Lumina colour adjustments.

It would be logical to disable that for capture, but note that it will not be disabled later for the end user. It probably will not work for the day/night cycle because exposure range limits are vastly different from day and night, from my findings.

To get an accurate middle grey to establish your desired pivot, you will eventually need to make some kind of reference decal for that, when screenshotted, will give you a perfect reference point. The rest of the authoring will just be the colour stuff. 

**This is currently in the R&D phase, so please manage expectations while I still need to finalize the methodology, and have not found time to actually test new LUT creation.**

---

## 3. Mastering the DaVinci Resolve Workflow
To create LUTs that actually work perfectly with CS2's HDRP pipeline, you must understand what **The "External" LUT** does. When you set Lumina or the game's tonemapping to "External," Unity applies all color operations in HDR and expects a **Log-encoded 3D LUT** (specifically Alexa LogC El1000). If you author a LUT in sRGB, you are applying linear math to log data—which ruins the image.

### A. Prep: Get the Unity Transforms
1. Open Unity Hub, create a new "High Definition 3D sample" project.
2. Go to **Window > Package Manager**, select **High Definition RP**.
3. Under Samples, select **Import** next to **Additional Post-processing Data**.
4. Navigate to: `Assets\Samples\High Definition RP\[Version]\Additional Post-processing Data\Cube LUTs`.
5. Copy these LUTs (specifically `Linear to Unity Log r1` and `Unity Log to sRGB r1`) into your DaVinci Resolve LUT folder.

### B. Project Setup in Resolve
* **Color Science:** DaVinci YRGB
* **Timeline Color Space:** Rec.709 (Scene)
* **Output Color Space:** sRGB

### C. The Node Pipeline
Import your EXR screenshot (Scene Linear) and go to the Color Tab. Build this exact node tree:
1.  **Node 1 (Input Transform):** Right-click > LUT > Unity > `Linear to Unity Log r1`.
2.  **Node 2 to X (The Grade):** This is where you do your creative grading (Exposure, Saturation, Balance). **Crucial:** You are now grading in Log space, which is exactly how professional colorists work.
3.  **Final Node (Output Transform / Film Emulation):** Right-click > LUT > Unity > `Unity Log to sRGB r1`.

**Why this works:** Since your LUT incorporates the final conversion to sRGB, you are essentially doing the tonemapping inside the LUT.
*Note: This is the basic setup, but really you can make a different sandwich with the Linear data. Just make sure the final output is targeting sRGB.*

### D. Exporting the LUT for the Game
1.  Right-click your graded clip and select **Generate LUT (CUBE) > 33 Point Cube**.
2.  **Note for Lumina users:** Lumina expects a 32-size LUT. Use a free online tool to convert your 33-point CUBE to a 32-point Generic CUBE. *(Possibly making a Python script for this will work just as well, and note that the LUT data should is BGR ordered (iirc) check more Unity and Lumina documentation)*.
3.  Drop the generated file into your `ModsData\Lumina\LUTS` folder.
4.  In-game (via Lumina), set **Tonemapping Mode to External** and load your LUT.
