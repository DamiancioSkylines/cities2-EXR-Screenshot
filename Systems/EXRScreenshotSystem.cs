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
        private bool _isCapturing;
        
        public EXRScreenshotSystem()
        {
            _isCapturing = false;
            if (Mod.Setting.DebugLogging) Mod.LOG.Info("[EXRScreenshotSystem] EXRScreenshotSystem initialized.");
        }
        
        public void CaptureEXR()
        {
            if (_isCapturing)
            {
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] Capture already in progress, ignoring.");}
                return;
            }
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
                try{currentMetadata = VolumeInspection.GetActiveMetadata();}
                catch (Exception e){Mod.LOG.Error($"[EXRScreenshotSystem] Metadata failed: {e.Message}");}
            }

            try
            {
                // Prepare target resolution
                var originalRTWidth = RTHandles.rtHandleProperties.currentViewportSize.x;
                var originalRTHeight = RTHandles.rtHandleProperties.currentViewportSize.y;
                var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
                var targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
                var targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info($"[EXRScreenshotSystem] EXR capture coroutine started: {targetWidth}x{targetHeight} (Scale: {scale}x)"); }

                // Setup Capture Target
                var captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
                captureRT.name = "EXRScreenshot_Capture_Target";
                captureRT.Create();
                var captureRTHandle = RTHandles.Alloc(captureRT);

                // Force Camera to recognize the high-res target
                var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
                var originalAllowDynRes = hdData.allowDynamicResolution;
                hdData.allowDynamicResolution = false; // "Disable" DLSS/FSR for capture frame

                // cameraRT acts as the temporary target for camera to render over time, because resolution change is initially empty.
                // 24-bit depth and DefaultHDR is default game setup.
                var cameraRT =
                    RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.DefaultHDR);
                var originalTarget = mainCam.targetTexture;
                mainCam.targetTexture = cameraRT;

                RTHandles.SetReferenceSize(targetWidth, targetHeight);

                // Wait for several frames while game renders on cameraRT, when done copy colour buffer using EXRCapturePass and blit to captureRT

                // Warmup because SSR, AO, SSGI need more frames to resolve.
                // No warmup frames break glass not sure why, while more frames suffer diminishing returns, denoising is post process I think.
                var warmupFrames = (int)Mod.Setting.AccumulationFramesDropdown;
                if (Mod.Setting.DebugLogging && warmupFrames > 0) { Mod.LOG.Info($"[EXRScreenshotSystem] Warming up for {warmupFrames} accumulation frames..."); }
                for (var i = 0; i < warmupFrames; i++) yield return new WaitForEndOfFrame();

                // Setup Custom Pass
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

                var exportFinished = false;
                var frameCaptured = false;

                capturePass.OnBufferReady = (ctx, colorBuffer) =>
                {
                    HDUtils.BlitCameraTexture(ctx.cmd, colorBuffer, captureRTHandle);
                    frameCaptured = true;

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var exrPath = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", $"Screenshot_{timestamp}.exr"));
                    var textPath = Path.ChangeExtension(exrPath, ".txt");
                    var exrDir = Path.GetDirectoryName(exrPath);
                    
                    ctx.cmd.RequestAsyncReadback(captureRT, request =>
                    {
                        try
                        {
                            if (request.hasError)
                            {
                                Mod.LOG.Error("[EXRScreenshotSystem] GPU Readback error.");
                                exportFinished = true;
                                return;
                            }
                            
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
                            var encodedBytes = exrBytes.ToArray();
                            exrBytes.Dispose();
                            
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {

                                    if (!string.IsNullOrEmpty(exrDir) && !Directory.Exists(exrDir)) Directory.CreateDirectory(exrDir);

                                    // Save EXR
                                    File.WriteAllBytes(exrPath, encodedBytes);
                                    if (Mod.Setting.DebugLogging) { Mod.LOG.Info($"[EXRScreenshotSystem] Saved EXR: {exrPath}"); }

                                    // Save Metadata
                                    if (Mod.Setting.MetadataLogging && currentMetadata != null)
                                    {
                                        File.WriteAllText(textPath, currentMetadata);
                                    }
                                }
                                catch (Exception e) { Mod.LOG.Error($"[EXRScreenshotSystem] IO Error: {e.Message}"); }
                                finally { exportFinished = true; }
                            });
                        }
                        catch (Exception e)
                        {
                            Mod.LOG.Error($"[EXRScreenshotSystem] Readback processing failed: {e.Message}");
                            // Just in case smth shitfaced
                            exportFinished = true;
                        }
                    });
                };

                capturePass.RequestFrame();
                // Wait for the frame to be captured
                yield return new WaitUntil(() => frameCaptured);

                // Restore Camera stuff after frame has been captured
                mainCam.targetTexture = originalTarget;
                RenderTexture.ReleaseTemporary(cameraRT);
                // Most Important: Shrink the RTHandle back to original size to free VRAM
                // Only way to reset the current maximum resolution is using ResetReferenceSize instead of SetReferenceSize that can only increase but not decrease size.
                // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@13.1/manual/rthandle-system-using.html
                RTHandles.ResetReferenceSize(originalRTWidth, originalRTHeight);
                
                // Restore DLSS/FSR ability to reduce internal resolution
                hdData.allowDynamicResolution = originalAllowDynRes;

                // Wait for readback/disk — game should already be running normally
                yield return new WaitUntil(() => exportFinished);

                // Clean-up captureRT stays alive until readback is done, aka consumed and no longer needed by the camera. THEN release
                if (targetVolume) targetVolume.customPasses.Remove(capturePass);
                captureRTHandle.Release();
                captureRT.Release();
                Object.Destroy(captureRT);

                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] EXR capture coroutine complete."); }
            }
            finally
            {
                RestoreCamera();
                _isCapturing = false;
            }
        }

        // todo move restoration to this helper method
        private void RestoreCamera()
        {
            
        }
    }
}