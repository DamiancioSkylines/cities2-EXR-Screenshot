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
            var originalScreenWidth = Screen.width;
            var originalScreenHeight = Screen.height;
            var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
            var targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
            var targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);
            Mod.LOG.Info($"Initiating Raw Linear EXR Capture: {targetWidth}x{targetHeight} (Scale: {scale}x)");

            // 2. Setup Capture Render Target texture and Render Target Handle
            var captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
            captureRT.name = "EXR_Capture_Target";
            captureRT.Create();
            var captureRTHandle = RTHandles.Alloc(captureRT);
            
            // --------------------------------------------------------
            // 1. Force Camera to recognize the high-res target
            RenderTexture superResRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.DefaultHDR);
            RenderTexture originalTarget = mainCam.targetTexture;
            mainCam.targetTexture = superResRT;
            // 2. Resize RTHandle system so G-Buffers (Depth/Normals) match the target
            RTHandles.SetReferenceSize(targetWidth, targetHeight);
            // --------------------------------------------------------
            
            
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
            
            // 5. Change size of Buffers
            if (scale > 1.0f) ScalableBufferManager.ResizeBuffers(scale, scale);
            
            var readbackFinished = false;
            var frameCaptured = false;
            
            capturePass.OnBufferReady = (ctx, hdrBuffer) =>
            {
                HDUtils.BlitCameraTexture(ctx.cmd, hdrBuffer, captureRTHandle);
                frameCaptured = true;
                
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
            
            capturePass.RequestFrame();
            
            yield return new WaitUntil(() => frameCaptured);
            yield return new WaitUntil(() => readbackFinished);

            // --------------------------------------------------------
            mainCam.targetTexture = originalTarget;
            RenderTexture.ReleaseTemporary(superResRT);
            RTHandles.SetReferenceSize(originalScreenWidth, originalScreenHeight);
            // --------------------------------------------------------
            
            // 6. Restore Buffer size
            if (scale > 1.0f) ScalableBufferManager.ResizeBuffers(1.0f, 1.0f);
            
            // Cleanup
            if (targetVolume) targetVolume.customPasses.Remove(capturePass);
            captureRTHandle.Release();
            captureRT.Release();
            Object.Destroy(captureRT);
            
            Mod.LOG.Info("EXR capture routine complete.");
        }
    }
}