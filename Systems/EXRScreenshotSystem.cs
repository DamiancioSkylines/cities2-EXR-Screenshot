using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.IO;
using System.Linq;
using Game.SceneFlow;
using Object = UnityEngine.Object;

namespace EXRScreenshot.Systems
{
    public class EXRScreenshotSystem
    {
        public static EXRScreenshotSystem Instance;
        private bool _isCapturing;
        
        public EXRScreenshotSystem()
        {
            _isCapturing = false;
            if (Instance != null) Mod.LOG.Warn("Duplicate EXRScreenshotSystem detected.");
            Instance = this;
            if (Mod.Setting.DebugLogging) Mod.LOG.Info("EXRScreenshotSystem initialized.");
        }
        
        public void CaptureEXR()
        {
            if (_isCapturing) { Mod.LOG.Info("Capture already in progress, ignoring."); return; }
            GameManager.instance.StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            _isCapturing = true;
            var mainCam = Camera.main; // This is only for highly unlikely NRE 'System.NullReferenceException' 
            if (!mainCam) yield break;
            string currentMetadata = null;
                
            if (Mod.Setting.MetadataLogging)
            {
                currentMetadata = VolumeInspection.GetActiveMetadata();
            }

            try
            {
                // 1. Prepare Target Size for Render Target Texture
                var originalRTWidth = RTHandles.rtHandleProperties.currentViewportSize.x;
                var originalRTHeight = RTHandles.rtHandleProperties.currentViewportSize.y;
                var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
                var targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
                var targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);
                
                Mod.LOG.Info($"EXR capture coroutine started: {targetWidth}x{targetHeight} (Scale: {scale}x)");

                // 2. Setup Capture Render Target texture and Render Target Handle
                var captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
                captureRT.name = "EXRScreenshot_Capture_Target";
                captureRT.Create();
                var captureRTHandle = RTHandles.Alloc(captureRT);

                // 3. Force Camera to recognize the high-res target
                var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
                var originalAllowDynRes = hdData.allowDynamicResolution;
                hdData.allowDynamicResolution = false; // "Disable" DLSS/FSR for capture frame
                
                // superResRT acts as the temporary high-res 'canvas' for the camera.
                // 24-bit depth is required here to ensure geometry/shadows are calculated correctly at scale.
                // RenderTextureFormat.DefaultHDR ensures compatibility with the engine's internal rendering.
                var superResRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.DefaultHDR);
                var originalTarget = mainCam.targetTexture;
                mainCam.targetTexture = superResRT;
                
                // Resize RTHandle system so G-Buffers (Depth/Normals) match the target
                RTHandles.SetReferenceSize(targetWidth, targetHeight);
                
                // We wait for several frames to let SSR, AO, SSGI to resolve better
                // O frames can break screenshots when glass is in the view not sure why so minimum should be at least 1 frame
                // 1 frame is very noisy in SSGI and SSAO
                // 16-32 frames is recommended for "Perfect" SSR/Temporal stability, but going all the way to 128 is possible but with some diminishing erturns
                // Dynamically read user setting to allow temporal effects (SSR, SSGI, AO) to resolve
                int warmupFrames = (int)Mod.Setting.AccumulationFramesDropdown;
                if (Mod.Setting.DebugLogging && warmupFrames > 0)
                {
                    Mod.LOG.Info($"[EXRScreenshotSystem] Warming up for {warmupFrames} accumulation frames...");
                }
                for (int i = 0; i < warmupFrames; i++) yield return new WaitForEndOfFrame();

                // 4. Setup Custom Pass
                var targetVolume = Object.FindObjectsByType<CustomPassVolume>(FindObjectsSortMode.None)
                    .FirstOrDefault(v => v.name == "EXRScreenshot_CaptureVolume");

                if (!targetVolume)
                {
                    targetVolume = new GameObject("EXRScreenshot_CaptureVolume").AddComponent<CustomPassVolume>();
                    targetVolume.isGlobal = true;
                }

                targetVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
                var capturePass = targetVolume.customPasses.OfType<EXRCapturePass>().FirstOrDefault();
                if (capturePass == null)
                {
                    capturePass = new EXRCapturePass();
                    targetVolume.customPasses.Add(capturePass);
                }

                var readbackFinished = false;
                var frameCaptured = false;

                capturePass.OnBufferReady = (ctx, colorBuffer) =>
                {
                    // Grab all relevant exposure parameters might use this later
                    /*
                    var exp = ctx.hdCamera.volumeStack.GetComponent<Exposure>();
                    var metaString = $"--- Exposure Settings ---\n";
                    metaString += $"Mode: {exp.mode.value}\n";
                    metaString += $"Fixed Exposure (EV100): {exp.fixedExposure.value:F2}\n";
                    metaString += $"Compensation: {exp.compensation.value:F2}\n";
                    metaString += $"Limit Min: {exp.limitMin.value:F2}\n";
                    metaString += $"Limit Max: {exp.limitMax.value:F2}\n";
                    */
                    
                    HDUtils.BlitCameraTexture(ctx.cmd, colorBuffer, captureRTHandle);
                    frameCaptured = true;

                    ctx.cmd.RequestAsyncReadback(captureRT, request =>
                    {
                        if (request.hasError)
                        {
                            Mod.LOG.Error("GPU Readback error.");
                            readbackFinished = true;
                            return;
                        }

                        // Cast custom enum to Unity's expected EXRFlags
                        var compressionFlag = (Texture2D.EXRFlags)Mod.Setting.CompressionDropdown;

                        // EncodeNativeArrayToEXR is a Unity API — must run on main thread.
                        var exrBytes = ImageConversion.EncodeNativeArrayToEXR(
                            request.GetData<byte>(),
                            captureRT.graphicsFormat,
                            (uint)targetWidth,
                            (uint)targetHeight,
                            0,
                            compressionFlag
                        );

                        // Copy encoded bytes to managed array before handing off.
                        // NativeArray and exrBytes are only valid on this thread.
                        var encodedBytes = exrBytes.ToArray();
                        exrBytes.Dispose();

                        // Only the file write goes to background thread — safe, no Unity APIs.
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                                var rawPath = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", $"Screenshot_{timestamp}.exr");
                                var cleanPath = Path.GetFullPath(rawPath);
                                var dir = Path.GetDirectoryName(cleanPath);
                                
                                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                                // Save EXR
                                File.WriteAllBytes(cleanPath, encodedBytes);
                                Mod.LOG.Info($"Saved EXR: {cleanPath}");
                                
                                // Save Metadata
                                if (!Mod.Setting.MetadataLogging || currentMetadata == null) return;
                                var metadataPath = rawPath.Replace(".exr", ".txt");
                                File.WriteAllText(metadataPath, currentMetadata);
                            }
                            catch (Exception e) { Mod.LOG.Error($"IO Error: {e.Message}"); }
                            finally { readbackFinished = true; }
                        });
                    });
                };

                capturePass.RequestFrame();
                // Wait for the frame to be captured
                yield return new WaitUntil(() => frameCaptured);

                // Restore Camera stuff after frame has been captured
                mainCam.targetTexture = originalTarget;
                RenderTexture.ReleaseTemporary(superResRT);
                // CRITICAL: Shrink the RTHandle system back to original size to free VRAM
                // As we need to render to a higher resolution than normal for a short period of time when we want Super Sample Resolution.
                // After takin screenshot we do not require this resolution any more, the additional memory allocated is wasted.
                // To avoid that, only way to reset the current maximum resolution is using ResetReferenceSize instead of SetReferenceSize that can only increase but not decrease size.
                // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@13.1/manual/rthandle-system-using.html
                RTHandles.ResetReferenceSize(originalRTWidth, originalRTHeight);
                
                // Restore DLSS/FSR ability to reduce internal resolution
                hdData.allowDynamicResolution = originalAllowDynRes;

                // Wait for readback/disk — game should already be running normally
                yield return new WaitUntil(() => readbackFinished);

                // Clean-up captureRT stays alive until readback is done, aka consumed band no longer needed by the camera. THEN release
                if (targetVolume) targetVolume.customPasses.Remove(capturePass);
                captureRTHandle.Release();
                captureRT.Release();
                Object.Destroy(captureRT);

                Mod.LOG.Info("EXR capture coroutine complete.");
            }
            finally { _isCapturing = false; }
        }
    }
}