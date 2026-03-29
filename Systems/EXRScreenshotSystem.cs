using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Unity.Collections;
using System.Collections;
using System.IO;
using System.Linq;
using EXRScreenshot.Settings;
using Game.SceneFlow;
using Game.UI;

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
            if (Mod.Setting == null) yield break;

            Camera mainCam = Camera.main;
            if (mainCam == null) yield break;

            var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
            if (hdData == null) yield break;

            float scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
            int targetWidth = Mathf.RoundToInt(mainCam.pixelWidth * scale);
            int targetHeight = Mathf.RoundToInt(mainCam.pixelHeight * scale);

            Mod.LOG.Info($"Initiating Raw Linear EXR Capture: {targetWidth}x{targetHeight} (Scale: {scale}x)");

            // 1. Setup Capture Texture 
            RenderTexture captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
            captureRT.name = "EXR_Capture_Target";
            captureRT.Create();

            // HDRP requires RTHandles for its utility methods
            RTHandle captureHandle = RTHandles.Alloc(captureRT);

            // 2. Setup Custom Pass
            CustomPassVolume targetVolume = Object.FindObjectsByType<CustomPassVolume>(FindObjectsSortMode.None)
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

            // 3. State Management
            int originalCullingMask = mainCam.cullingMask;
            bool wasUiVisible = GameManager.instance.userInterface.view.enabled;
            bool readbackFinished = false;

            capturePass.OnBufferReady = (ctx, hdrBuffer) =>
            {
                // Now passing the RTHandle instead of the RenderTexture to satisfy HDUtils.BlitCameraTexture
                HDUtils.BlitCameraTexture(ctx.cmd, hdrBuffer, captureHandle);

                ctx.cmd.RequestAsyncReadback(captureRT, (AsyncGPUReadbackRequest request) => {
                    if (request.hasError) {
                        Mod.LOG.Error("GPU Readback error: The request returned an error state.");
                    } else {
                        var data = request.GetData<byte>();
                        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string path = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", $"Screenshot_{timestamp}.exr");
                        
                        string dir = Path.GetDirectoryName(path);
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
                        
                        Mod.LOG.Info($"Saved Valid EXR: {path} ({targetWidth}x{targetHeight})");
                    }
                    readbackFinished = true;
                });
            };

            // 4. Trigger Rendering
            GameManager.instance.userInterface.view.enabled = false;
            mainCam.cullingMask &= ~(1 << 5); 

            if (scale > 1.0f)
            {
                ScalableBufferManager.ResizeBuffers(scale, scale);
            }

            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForEndOfFrame();
            }
            
            capturePass.RequestFrame();
            
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            // 5. Restore State
            if (scale > 1.0f)
            {
                ScalableBufferManager.ResizeBuffers(1.0f, 1.0f);
            }
            
            mainCam.cullingMask = originalCullingMask;
            GameManager.instance.userInterface.view.enabled = wasUiVisible;

            yield return new WaitUntil(() => readbackFinished);

            // Cleanup
            if (targetVolume != null) targetVolume.customPasses.Remove(capturePass);
            
            captureHandle.Release(); // Release the RTHandle wrapper
            captureRT.Release();
            Object.Destroy(captureRT);
            
            Mod.LOG.Info("EXR capture sequence complete.");
        }
    }
}