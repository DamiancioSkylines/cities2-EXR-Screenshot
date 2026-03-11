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
        public void CaptureEXR()
        {
            var timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var folderPath = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Screenshots", "EXR"));
            var filePath = Path.Combine(folderPath, $"Screenshot_{timestamp}.exr");
            var fileName = $"Screenshot_{timestamp}.exr";
            
            // Find every single volume to see the full "rendering schedule"
            var volumes = Object.FindObjectsOfType<CustomPassVolume>()
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
                for (var i = 0; i < v.customPasses.Count; i++)
                {
                    var p = v.customPasses[i];
                    var pStatus = p.enabled ? "ON" : "OFF";
                    Mod.LOG.Info($"  --> [{i}] {p.name} (Class: {p.GetType().Name}) [{pStatus}]");
                }
                */
            }
            //Mod.LOG.Info("---------------------------------------------------------");
            
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
                // 1. Get the internal scale and viewport size from the buffer itself
                Vector2 renderScale = hdrBuffer.rtHandleProperties.rtHandleScale;
                var internalRes = hdrBuffer.rtHandleProperties.currentViewportSize;
                
                var bufferWidth = internalRes.x;
                var bufferHeight = internalRes.y;

                // Mod.LOG.Info($"Capturing internal buffer size: {bufferWidth}x{bufferHeight}");
                var tempTexture = RenderTexture.GetTemporary(bufferWidth, bufferHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

                var previousActive = RenderTexture.active;
                Graphics.SetRenderTarget(tempTexture);
                GL.Clear(true, true, Color.clear);
                
                Graphics.Blit(hdrBuffer, tempTexture, new Vector2(renderScale.x, renderScale.y), Vector2.zero);
                RenderTexture.active = previousActive;

                AsyncGPUReadback.Request(tempTexture, 0, (request) => 
                {
                    try
                    {
                        if (request.hasError) return;
                        var rawData = request.GetData<byte>();
                        var exrData = ImageConversion.EncodeNativeArrayToEXR(
                            rawData, tempTexture.graphicsFormat, (uint)bufferWidth, (uint)bufferHeight, 0, Texture2D.EXRFlags.CompressZIP
                        );
                        SaveNativeArrayToDisk(exrData, filePath, fileName, bufferWidth, bufferHeight);
                        exrData.Dispose();
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(tempTexture);
                        if (targetVolume != null && capturePass != null)
                            targetVolume.customPasses.Remove(capturePass);
                    }
                });
            };

            capturePass.RequestFrame();
        }

        private void SaveNativeArrayToDisk(NativeArray<byte> exrBytes, string path, string fileName, int width, int height)
        {
            try 
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) 
                    Directory.CreateDirectory(directory);
                
                File.WriteAllBytes(path, exrBytes.ToArray());
                Mod.LOG.Info($"EXR Screenshot Success: {width} x {height} | File: {fileName} | Folder: {directory}");
            }
            catch (System.Exception e) 
            { 
                Mod.LOG.Error($"{e.Message}"); 
            }
        }
    }
}