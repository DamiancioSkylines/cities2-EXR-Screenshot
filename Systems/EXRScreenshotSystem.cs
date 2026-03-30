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

        public EXRScreenshotSystem()
        {
            if (Instance != null) Mod.LOG.Warn("Duplicate EXRScreenshotSystem detected.");
            Instance = this;
            Mod.LOG.Info("EXRScreenshotSystem initialized.");
        }
        
        private bool _isCapturing = false;
        
        public void CaptureEXR()
        {
            if (_isCapturing) { Mod.LOG.Info("Capture already in progress, ignoring."); return; }
            GameManager.instance.StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            _isCapturing = true;

            var mainCam = Camera.main;
            // This is only for highly unlikely NRE 'System.NullReferenceException' 
            if (!mainCam) yield break;
            try
            {
                // 1. Prepare Target Size for Render Target Texture
                var originalRTWidth = RTHandles.rtHandleProperties.currentViewportSize.x;
                var originalRTHeight = RTHandles.rtHandleProperties.currentViewportSize.y;
                var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
                var targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
                var targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);
                Mod.LOG.Info($"Initiating Raw Linear EXR Capture: {targetWidth}x{targetHeight} (Scale: {scale}x)");

                // 2. Setup Capture Render Target texture and Render Target Handle
                var captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
                captureRT.name = "EXR_Capture_Target";
                captureRT.Create();
                var captureRTHandle = RTHandles.Alloc(captureRT);

                // 3. Force Camera to recognize the high-res target
                var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
                var originalAllowDynRes = hdData.allowDynamicResolution;
                hdData.allowDynamicResolution = false; // Disable DLSS/FSR for capture frame
                var superResRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.DefaultHDR);
                var originalTarget = mainCam.targetTexture;
                mainCam.targetTexture = superResRT;
                // Resize RTHandle system so G-Buffers (Depth/Normals) match the target
                RTHandles.SetReferenceSize(targetWidth, targetHeight);


                // 4. Setup Custom Pass
                var targetVolume = Object.FindObjectsByType<CustomPassVolume>(FindObjectsSortMode.None)
                    .FirstOrDefault(v => v.name == "EXR_Capture_Volume");

                if (!targetVolume)
                {
                    targetVolume = new GameObject("EXR_Capture_Volume").AddComponent<CustomPassVolume>();
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

                capturePass.OnBufferReady = (ctx, hdrBuffer) =>
                {
                    HDUtils.BlitCameraTexture(ctx.cmd, hdrBuffer, captureRTHandle);
                    frameCaptured = true;

                    ctx.cmd.RequestAsyncReadback(captureRT, request =>
                    {
                        if (request.hasError)
                        {
                            Mod.LOG.Error("GPU Readback error.");
                            readbackFinished = true;
                            return;
                        }

                        //Cast custom enum to Unity's expected EXRFlags
                        var compressionFlag = (Texture2D.EXRFlags)Mod.Setting.CompressionDropdown;

                        // EncodeNativeArrayToEXR is a Unity API — must run on main thread.
                        var exrBytes = ImageConversion.EncodeNativeArrayToEXR(
                            request.GetData<byte>(),
                            captureRT.graphicsFormat,
                            (uint)targetWidth,
                            (uint)targetHeight,
                            0,
                            compressionFlag
                            //Texture2D.EXRFlags.CompressZIP
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
                                var path = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR",
                                    $"Screenshot_{timestamp}.exr");
                                var dir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                File.WriteAllBytes(path, encodedBytes);
                                Mod.LOG.Info(
                                    $"Saved EXR: {path} ({targetWidth}x{targetHeight}) using compression {Mod.Setting.CompressionDropdown}");
                            }
                            catch (Exception e)
                            {
                                Mod.LOG.Error($"IO Error: {e.Message}");
                            }
                            finally
                            {
                                readbackFinished = true;
                            }
                        });
                    });
                };

                capturePass.RequestFrame();
                // Wait for the frame to be captured
                yield return new WaitUntil(() => frameCaptured);

                // Restore Camera stuff after frame has been captured
                mainCam.targetTexture = originalTarget;
                RenderTexture.ReleaseTemporary(superResRT);
                // As we need to render to a higher resolution than normal for a short period of time when we want Super Sample.
                // And after takin screenshot we do not require this resolution any more, the additional memory allocated is wasted.
                // To avoid that, only way to reset the current maximum resolution is using ResetReferenceSize instead of SetReferenceSize that can only increase but not decrease size.
                // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@13.1/manual/rthandle-system-using.html
                RTHandles.ResetReferenceSize(originalRTWidth, originalRTHeight);
                hdData.allowDynamicResolution = originalAllowDynRes; // Restore DLSS/FSR


                // Wait for readback/disk — game should already be running normally
                yield return new WaitUntil(() => readbackFinished);

                // Clean-up captureRT stays alive until readback is done, aka consumed band no longer needed by the camera. THEN release
                if (targetVolume) targetVolume.customPasses.Remove(capturePass);
                captureRTHandle.Release();
                captureRT.Release();
                Object.Destroy(captureRT);

                Mod.LOG.Info("EXR capture routine complete.");
            }
            finally { _isCapturing = false; }
        }
    }
}