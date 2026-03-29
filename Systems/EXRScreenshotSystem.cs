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

        public void CaptureEXR()
        {
            GameManager.instance.StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            var mainCam = Camera.main;
            // This is only for highly unlikely 'System.NullReferenceException' 
            if (!mainCam) yield break;
            
            // 1. Prepare Target Size for Render Target Texture
            var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
            var targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
            var targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);
            Mod.LOG.Info($"Initiating Raw Linear EXR Capture: {targetWidth}x{targetHeight} (Scale: {scale}x)");

            // 2. Setup Capture Render Target texture and Render Target Handle
            var captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
            captureRT.name = "EXR_Capture_Target";
            captureRT.Create();
            var captureRTHandle = RTHandles.Alloc(captureRT);

            // 3. Setup Custom Pass Reformat this code ToDo
            var targetVolume = Object.FindObjectsByType<CustomPassVolume>(FindObjectsSortMode.None)
                .FirstOrDefault(v => v.name == "EXR_Capture_Volume");
            
            if (targetVolume == null)
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
            
            // 5. Change size of Buffers
            if (scale > 1.0f) ScalableBufferManager.ResizeBuffers(scale, scale);
            
            capturePass.RequestFrame();
            
            
            capturePass.OnBufferReady = (ctx, hdrBuffer) =>
            {
                HDUtils.BlitCameraTexture(ctx.cmd, hdrBuffer, captureRTHandle);

                ctx.cmd.RequestAsyncReadback(captureRT, request => {
                    if (request.hasError) {
                        Mod.LOG.Error("GPU Readback error: The request returned an error state.");
                    } else {
                        var data = request.GetData<byte>();
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        var path = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", $"Screenshot_{timestamp}.exr");
                        
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                        var exrBytes = ImageConversion.EncodeNativeArrayToEXR(
                            data, 
                            captureRT.graphicsFormat, 
                            (uint)targetWidth, 
                            (uint)targetHeight, 
                            0, 
                            Texture2D.EXRFlags.CompressZIP
                        );
                        
                        File.WriteAllBytes(path, exrBytes.ToArray());
                        exrBytes.Dispose();
                        
                        Mod.LOG.Info($"Saved EXR: {path} ({targetWidth}x{targetHeight})");
                    }
                    readbackFinished = true;
                });
            };

            // 6. Restore Buffer size
            if (scale > 1.0f)
            {
                ScalableBufferManager.ResizeBuffers(1.0f, 1.0f);
            }

            yield return new WaitUntil(() => readbackFinished);

            // Cleanup
            if (targetVolume) targetVolume.customPasses.Remove(capturePass);
            
            captureRTHandle.Release();
            captureRT.Release();
            // Dead code candidate
            Object.Destroy(captureRT);
            
            Mod.LOG.Info("EXR capture routine complete.");
        }
    }
}