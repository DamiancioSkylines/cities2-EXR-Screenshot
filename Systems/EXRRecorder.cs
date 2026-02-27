using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Unity.Collections;
using System.IO;

namespace EXRScreenshot.Systems
{
    public class EXRRecorder
    {
        public void CaptureProEXR(string filePath, float userScale)
        {
            CustomPassVolume volume = GameObject.FindObjectOfType<CustomPassVolume>();

            if (volume == null)
            {
                Mod.LOG.Error("EXR Screenshot:  Could not find a CustomPassVolume.");
                return;
            }

            var capturePass = volume.AddPassOfType<EXRCapturePass>() as EXRCapturePass;
            if (capturePass == null) return;

            capturePass.name = "EXR_Raw_Capture_Pass";
            
            capturePass.OnBufferReady = (hdrBuffer) => 
            {
                Vector2 renderScale = hdrBuffer.rtHandleProperties.rtHandleScale;
                
                Camera mainCam = Camera.main;
                int screenWidth = mainCam != null ? mainCam.pixelWidth : hdrBuffer.rt.width;
                int screenHeight = mainCam != null ? mainCam.pixelHeight : hdrBuffer.rt.height;

                int targetWidth = Mathf.RoundToInt(screenWidth * userScale);
                int targetHeight = Mathf.RoundToInt(screenHeight * userScale);

                // Use ARGBHalf (16-bit) to match the game's internal HDR precision
                RenderTexture tempRGBA = RenderTexture.GetTemporary(
                    targetWidth, 
                    targetHeight, 
                    0, 
                    RenderTextureFormat.ARGBHalf, 
                    RenderTextureReadWrite.Linear
                );

                hdrBuffer.rt.filterMode = FilterMode.Trilinear;
                tempRGBA.filterMode = FilterMode.Trilinear;

                RenderTexture previousActive = RenderTexture.active;
                Graphics.SetRenderTarget(tempRGBA);
                GL.Clear(true, true, Color.clear);
                
                Graphics.Blit(hdrBuffer, tempRGBA, new Vector2(renderScale.x, renderScale.y), Vector2.zero);
                
                RenderTexture.active = previousActive;

                // Request data from GPU
                AsyncGPUReadback.Request(tempRGBA, 0, (request) => 
                {
                    try
                    {
                        if (request.hasError)
                        {
                            Mod.LOG.Error("EXR Screenshot: GPU Readback error.");
                            return;
                        }

                        // OPTIMIZATION: Use the NativeArray directly from the request
                        // This avoids the 'LoadRawTextureData' and Texture2D creation overhead entirely.
                        var rawData = request.GetData<byte>();
                        
                        // CompressZIP: Significantly reduces file size for high-res images
                        Texture2D.EXRFlags flags = Texture2D.EXRFlags.CompressZIP;
                        
                        // I think due to final exr will be missing metadata it will report is 32 bit but everything should be intact so 16bit.
                        NativeArray<byte> exrData = ImageConversion.EncodeNativeArrayToEXR(
                            rawData, 
                            tempRGBA.graphicsFormat, 
                            (uint)targetWidth, 
                            (uint)targetHeight, 
                            0, 
                            flags
                        );

                        SaveNativeArrayToDisk(exrData, filePath, targetWidth, targetHeight, renderScale, userScale);
                        exrData.Dispose();
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(tempRGBA);
                        if (volume != null && capturePass != null)
                            volume.customPasses.Remove(capturePass);
                    }
                });
            };

            capturePass.RequestFrame();
        }

        private void SaveNativeArrayToDisk(NativeArray<byte> exrBytes, string path, int width, int height, Vector2 renderScale, float userScale)
        {
            try 
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) 
                    Directory.CreateDirectory(directory);
                
                File.WriteAllBytes(path, exrBytes.ToArray());
                
               
                Mod.LOG.Info($"EXR Screenshot: Saved {width}x{height} | 16-bit HDR | ZIP Compressed | SSAA: {userScale}x");
            }
            catch (System.Exception e)
            {
                Mod.LOG.Error($"EXR Screenshot: Save Error: {e.Message}");
            }
        }
    }
}