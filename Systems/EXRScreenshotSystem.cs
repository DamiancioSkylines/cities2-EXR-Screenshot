using System;
using Colossal.Logging;
using UnityEngine;
using System.IO;
using Object = UnityEngine.Object;
using Game.SceneFlow;

namespace EXRScreenshot.Systems

{
    public static class MakingScreenshot
    {
        private static int _screenshotCount;
        private static bool _wasUIon;

        private static void CaptureScreenshot(Camera camera, RenderTexture destination)
        {
            _wasUIon = GameManager.instance.userInterface.view.enabled;
            GameManager.instance.userInterface.view.enabled = false;

            if (destination == null || camera == null)
            {
                Mod.LOG.Error("EXR Screenshot: Camera or destination RenderTexture is null.");
                return;
            }

            //Mod.LOG.Info("EXR Screenshot: Rendering camera to RenderTexture.");
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = destination;
            camera.Render();
            RenderTexture.active = previousRT;
            //Mod.LOG.Info("EXR Screenshot: Rendering complete.");

            Material material = new Material(Shader.Find("Hidden/ScreenCaptureCompose"));
            if (material == null)
            {
                Mod.LOG.Error("EXR Screenshot: Failed to find shader 'Hidden/ScreenCaptureCompose'.");
                return;
            }
            
            // I don't remember what this was about, but it's breaking the mod.
            //Mod.LOG.Info("EXR Screenshot: Blitting RenderTexture.");
            //Graphics.Blit(destination, destination, material, 0);

            Object.Destroy(material);
            destination.IncrementUpdateCount();
            //Mod.LOG.Info("EXR Screenshot: Capture Screenshot Finished.");
            GameManager.instance.userInterface.view.enabled = _wasUIon;
        }

        public static void TakeScreenshot(bool mSettingTakeSuperResolution)
        {
            //Mod.LOG.Info("EXR Screenshot: Attempting to capture linear EXR screenshot using built-in method!");
            Camera gameCamera = Camera.main;
            if (gameCamera == null)
            {
                Mod.LOG.Error("EXR Screenshot: Failed to get game camera.");
                return;
            }

            int width = Screen.width;
            int height = Screen.height;
            int scaleFactor = 1; // Default scale factor.

            // Get the m_TakeSuperResolution setting.
            if (mSettingTakeSuperResolution)
            {
                // Take a supersize screenshot that is *at least* 2160 pixels (4K).
                // Height is better than width because of widescreen monitors.
                // the resulting image is bigger. credit Toverux
                 scaleFactor = (int)Math.Ceiling(2160d / height);
                 //Mod.LOG.Info($"EXR Screenshot: Super Resolution enabled, scaleFactor = {scaleFactor}");
            }


            //Mod.LOG.Info($"EXR Screenshot: Super Resolution disabled, scaleFactor = {scaleFactor}");
            // Create a RenderTexture with the supersized resolution
            width *= scaleFactor;
            height *= scaleFactor;

            // Initialise the descriptor
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBFloat, 32);

            // Force Linear
            descriptor.sRGB = false; 

            // Set remaining properties
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            
            
            RenderTexture renderTexture = new RenderTexture(descriptor)
            {
                antiAliasing = QualitySettings.antiAliasing,
                useDynamicScale = false,
                enableRandomWrite = false
            };
            if (renderTexture == null)
            {
                Mod.LOG.Error("EXR Screenshot: Failed to create HDR RenderTexture.");
                return;
            }
            gameCamera.targetTexture = renderTexture;
            Mod.LOG.Info($"EXR Screenshot: HDR RenderTexture created: {width}x{height} format={renderTexture.format} {descriptor.depthBufferBits} Bits");

            CaptureScreenshot(gameCamera, renderTexture);

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            RenderTexture.active = renderTexture;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            //Mod.LOG.Info("EXR Screenshot: Pixels read from HDR RenderTexture to HDR Texture2D.");

            byte[] exrBytes = texture.EncodeToEXR();
            string exrFilename = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{_screenshotCount}_{renderTexture.descriptor.colorFormat}_{renderTexture.descriptor.depthBufferBits}_Bits.exr";
            string exrPath = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", exrFilename);
            Directory.CreateDirectory(Path.GetDirectoryName(exrPath) ?? string.Empty);
            File.WriteAllBytes(exrPath, exrBytes);
            Mod.LOG.Info($"EXR Screenshot: Screenshot saved to: {exrPath}");

            /* Taking Debug PNG files will look very dark as trying to save Linear to RGB without gamma transform or something like that
            // linear data is being forced into a gamma container
            byte[] pngBytes = texture.EncodeToPNG();
            string pngFilename = $"Screenshot_DEBUG_{DateTime.Now:yyyyMMdd_HHmmss}_{_screenshotCount}.png";
            string pngPath = Path.Combine(Application.persistentDataPath, "Screenshots", "EXR Debug", pngFilename);
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
            File.WriteAllBytes(pngPath, pngBytes);
            Mod.LOG.Info($"EXR Screenshot: Linear EXR screenshot (DEBUG PNG) saved to: {pngPath}");
            */
            
            _screenshotCount++;

            gameCamera.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(texture);
            Object.Destroy(renderTexture);
            // Mod.LOG.Info("EXR Screenshot: Cleanup complete.");
        }
    }
}
