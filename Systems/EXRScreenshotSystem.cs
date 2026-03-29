using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using EXRScreenshot.Settings;
using Game.SceneFlow;

namespace EXRScreenshot.Systems
{
    public class EXRScreenshotSystem
    {
        public static EXRScreenshotSystem Instance;

        // We use R16G16B16A16_SFloat (RGBAHalf) as it is the most compatible 
        // high-bit-depth format for Unity's EXR encoder.
        private static readonly GraphicsFormat CaptureFormat = GraphicsFormat.R16G16B16A16_SFloat;
        private bool _isCapturing = false;

        public EXRScreenshotSystem()
        {
            Instance = this;
            Mod.LOG.Info("EXRScreenshotSystem initialized.");
        }

        public void CaptureEXR()
        {
            if (_isCapturing) return;
            GameManager.instance.StartCoroutine(CaptureSequence());
        }

        private IEnumerator CaptureSequence()
        {
            _isCapturing = true;

            // 1. Calculate target resolution
            float scale = Mod.Setting.SupersampleScale;
            int width = Mathf.RoundToInt(Screen.width * scale);
            int height = Mathf.RoundToInt(Screen.height * scale);

            Mod.LOG.Info($"[Capture] Starting sequence: {width}x{height} (Scale: {scale}x)");

            // 2. Prepare Camera and Force Resolution Scale
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                Mod.LOG.Error("Main Camera not found.");
                _isCapturing = false;
                yield break;
            }

            var hdData = mainCam.GetComponent<HDAdditionalCameraData>();
            bool originalAllowDynamicRes = hdData.allowDynamicResolution;

            try 
            {
                // Force engine to 100% scale using the enum index fix for CS1117
                DynamicResolutionHandler.SetDynamicResScaler(() => 100f, (DynamicResScalePolicyType)1);
                DynamicResolutionHandler.SetActiveDynamicScalerSlot(DynamicResScalerSlot.User);
                hdData.allowDynamicResolution = true;
                
                // Re-initialize RTHandles for the high-res pass
                RTHandles.SetReferenceSize(width, height);
            }
            catch (Exception e)
            {
                Mod.LOG.Error($"[Setup Error] {e.Message}");
            }

            // 3. Setup the Capture Pass and Volume
            var (volume, pass) = GetOrCreateCapturePass();
            RTHandle captureRT = RTHandles.Alloc(
                width, height, 
                colorFormat: CaptureFormat, 
                name: "EXRCaptureTemp",
                useDynamicScale: false // Crucial: Don't let the engine scale this one
            );

            bool bufferReady = false;
            pass.OnBufferReady = (ctx, cameraBuffer) =>
            {
                // Blit from the camera buffer (which is now high-res) to our capture RT
                // This also handles format conversion to RGBAHalf
                ctx.cmd.Blit(cameraBuffer, captureRT);
                bufferReady = true;
            };

            // Wait for reallocation to settle (approx 15-20 frames)
            for (int i = 0; i < 20; i++) yield return new WaitForEndOfFrame();

            // Request the frame
            pass.RequestFrame();

            // Wait until the pass has executed
            float timeout = Time.realtimeSinceStartup + 2.0f;
            while (!bufferReady && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (bufferReady)
            {
                // 4. Readback and Save
                AsyncGPUReadback.Request(captureRT, 0, CaptureFormat, (request) =>
                {
                    if (request.hasError)
                    {
                        Mod.LOG.Error("GPU Readback error.");
                    }
                    else
                    {
                        var data = request.GetData<byte>();
                        SaveEXR(data, CaptureFormat, width, height);
                    }
                });
            }
            else
            {
                Mod.LOG.Error("Capture timed out or failed to trigger.");
            }

            // 5. Cleanup
            Mod.LOG.Info("[Cleanup] Reverting changes...");
            hdData.allowDynamicResolution = originalAllowDynamicRes;
            RTHandles.SetReferenceSize(Screen.width, Screen.height);
            
            pass.OnBufferReady = null;
            captureRT.Release();
            Cleanup(volume, pass);
            
            _isCapturing = false;
        }

        private (CustomPassVolume, EXRCapturePass) GetOrCreateCapturePass()
        {
            var volObj = GameObject.Find("EXR_Capture_Volume");
            if (volObj == null) volObj = new GameObject("EXR_Capture_Volume");

            var vol = volObj.GetComponent<CustomPassVolume>() ?? volObj.AddComponent<CustomPassVolume>();
            vol.injectionPoint = CustomPassInjectionPoint.AfterPostProcess; // Capture final output

            var pass = vol.customPasses.OfType<EXRCapturePass>().FirstOrDefault();
            if (pass == null)
            {
                pass = new EXRCapturePass();
                vol.customPasses.Add(pass);
            }

            return (vol, pass);
        }

        private void Cleanup(CustomPassVolume volume, EXRCapturePass pass)
        {
            if (volume != null) volume.customPasses.Remove(pass);
        }

        private void SaveEXR(NativeArray<byte> rawData, GraphicsFormat format, int width, int height)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string folder = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR");
                string filePath = Path.Combine(folder, $"Screenshot_{timestamp}.exr");

                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var exrData = ImageConversion.EncodeNativeArrayToEXR(
                    rawData, format, (uint)width, (uint)height, 0, Texture2D.EXRFlags.CompressZIP);
                
                File.WriteAllBytes(filePath, exrData.ToArray());
                exrData.Dispose();

                Mod.LOG.Info($"[IO] High-Res EXR Saved: {width}x{height} at {filePath}");
            }
            catch (Exception e)
            {
                Mod.LOG.Error($"IO Error: {e.Message}");
            }
        }
    }
}