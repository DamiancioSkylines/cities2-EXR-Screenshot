using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Unity.Collections;
using System.IO;

namespace EXRScreenshot.Systems
{
    public class EXRRecorder
    {
        public void CaptureProEXR(string filePath)
        {
            // 1. Find the Custom Pass Volume
            CustomPassVolume volume = GameObject.FindObjectOfType<CustomPassVolume>();

            if (volume == null)
            {
                Mod.LOG.Error("EXR Mod: Could not find a CustomPassVolume.");
                return;
            }

            // 2. Add our specialized Hijacker pass and cast it
            var capturePass = volume.AddPassOfType<EXRCapturePass>() as EXRCapturePass;
            if (capturePass == null) return;

            capturePass.name = "EXR_Raw_Capture_Pass";
            
            capturePass.OnBufferReady = (hdrBuffer) => 
            {
                // 3. Request raw floating-point data directly from GPU
                AsyncGPUReadback.Request(hdrBuffer, 0, (request) => 
                {
                    if (request.hasError) return;

                    // Get the bytes and the dimensions
                    NativeArray<byte> rawData = request.GetData<byte>();
                    int width = hdrBuffer.rt.width;
                    int height = hdrBuffer.rt.height;
                    
                    SaveToDisk(rawData.ToArray(), width, height, filePath);

                    // 4. Cleanup the pass immediately so we don't lag the game
                    volume.customPasses.Remove(capturePass);
                });
            };

            // 5. Fire for the next frame
            capturePass.RequestFrame();
        }

        private void SaveToDisk(byte[] data, int width, int height, string path)
        {
            // CRITICAL: We use RGBAHalf (16-bit float) to match the HDRP buffer.
            // This is what gives you 65,536 steps of color instead of 256.
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);

            texture.LoadRawTextureData(data);
            texture.Apply();

            // Use the Unity native encoder (no flags needed for high bit depth)
            byte[] exrBytes = texture.EncodeToEXR(Texture2D.EXRFlags.None);

            // Ensure directory exists
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) 
                Directory.CreateDirectory(directory);
            
            File.WriteAllBytes(path, exrBytes);

            // Cleanup memory
            Object.Destroy(texture);
            Mod.LOG.Info($"High-Fidelity EXR Saved to: {path}");
        }
    }
}