using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Unity.Collections;
using System.IO;
using System.Linq;

namespace EXRScreenshot.Systems
{
    public class EXRScreenshotSystem
    {
        /// <summary>
        /// Captures HDR data and logs a complete map of the HDRP Custom Pass stack.
        /// </summary>
        public void CaptureProEXR(string filePath, float userScale)
        {
            // 1. LIST EVERYTHING
            // We find every single volume to see the full "rendering schedule"
            CustomPassVolume[] volumes = Object.FindObjectsOfType<CustomPassVolume>()
                                            .OrderByDescending(v => v.priority).ToArray();
            
            //Mod.LOG.Info($"--- HDRP CUSTOM PASS MAP (Total Volumes: {volumes.Length}) ---");

            CustomPassVolume targetVolume = null;

            foreach (var v in volumes)
            {
                //string status = v.enabled ? "ACTIVE" : "DISABLED";
                //Mod.LOG.Info($"Volume: [{v.name}] | Injection: {v.injectionPoint} | Priority: {v.priority} | Status: {status}");
                
                // Identify the best volume for our RAW capture
                if (v.enabled && v.injectionPoint == CustomPassInjectionPoint.BeforePostProcess && targetVolume == null)
                {
                    targetVolume = v;
                }

                /*
                for (int i = 0; i < v.customPasses.Count; i++)
                {
                    var p = v.customPasses[i];
                    string pStatus = p.enabled ? "ON" : "OFF";
                    Mod.LOG.Info($"  --> [{i}] {p.name} (Class: {p.GetType().Name}) [{pStatus}]");
                }
                */
            }
            //Mod.LOG.Info("---------------------------------------------------------");

            // 2. EXECUTE CAPTURE
            if (targetVolume == null)
            {
                Mod.LOG.Error("No suitable 'BeforePostProcess' volume found to inject capture pass.");
                return;
            }

            var capturePass = targetVolume.AddPassOfType<EXRCapturePass>() as EXRCapturePass;
            if (capturePass == null) return;

            capturePass.name = "EXR_Raw_Capture_Pass";

            // Move to Index 0 so we are the VERY FIRST thing this volume does
            if (targetVolume.customPasses.Count > 1)
            {
                targetVolume.customPasses.RemoveAt(targetVolume.customPasses.Count - 1);
                targetVolume.customPasses.Insert(0, capturePass);
                //Mod.LOG.Info($"EXR: Injected capture into [{targetVolume.name}] at index 0.");
            }
            
            capturePass.OnBufferReady = (hdrBuffer) => 
            {
                Vector2 renderScale = hdrBuffer.rtHandleProperties.rtHandleScale;
                Camera mainCam = Camera.main;
                int screenWidth = mainCam != null ? mainCam.pixelWidth : hdrBuffer.rt.width;
                int screenHeight = mainCam != null ? mainCam.pixelHeight : hdrBuffer.rt.height;

                int targetWidth = Mathf.RoundToInt(screenWidth * userScale);
                int targetHeight = Mathf.RoundToInt(screenHeight * userScale);

                RenderTexture tempRGBA = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

                RenderTexture previousActive = RenderTexture.active;
                Graphics.SetRenderTarget(tempRGBA);
                GL.Clear(true, true, Color.clear);
                
                Graphics.Blit(hdrBuffer, tempRGBA, new Vector2(renderScale.x, renderScale.y), Vector2.zero);
                RenderTexture.active = previousActive;

                AsyncGPUReadback.Request(tempRGBA, 0, (request) => 
                {
                    try
                    {
                        if (request.hasError) return;
                        var rawData = request.GetData<byte>();
                        NativeArray<byte> exrData = ImageConversion.EncodeNativeArrayToEXR(
                            rawData, tempRGBA.graphicsFormat, (uint)targetWidth, (uint)targetHeight, 0, Texture2D.EXRFlags.CompressZIP
                        );
                        SaveNativeArrayToDisk(exrData, filePath, targetWidth, targetHeight);
                        exrData.Dispose();
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(tempRGBA);
                        if (targetVolume != null && capturePass != null)
                            targetVolume.customPasses.Remove(capturePass);
                    }
                });
            };

            capturePass.RequestFrame();
        }

        private void SaveNativeArrayToDisk(NativeArray<byte> exrBytes, string path, int width, int height)
        {
            try 
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) 
                    Directory.CreateDirectory(directory);
                
                File.WriteAllBytes(path, exrBytes.ToArray());
                Mod.LOG.Info($"EXR Screenshot Success: {width}x{height} saved to {path}");
            }
            catch (System.Exception e) 
            { 
                Mod.LOG.Error($"{e.Message}"); 
            }
        }
    }
}