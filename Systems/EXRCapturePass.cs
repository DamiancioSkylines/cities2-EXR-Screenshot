using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using JetBrains.Annotations;

namespace EXRScreenshot.Systems
{
    /// <summary>
    /// Passive custom pass used to grab colour buffer
    /// </summary>
    [UsedImplicitly]
    public class EXRCapturePass : CustomPass
    {
        public System.Action<CustomPassContext, RTHandle> OnBufferReady;
        private bool _requestCapture;
        
        public void RequestFrame() => _requestCapture = true;

        protected override void Execute(CustomPassContext ctx)
        {
            // Only for actual game camera
            if (!_requestCapture || ctx.hdCamera.camera.cameraType != UnityEngine.CameraType.Game)
                return;

            // Direct access to colour buffer
            var colorBuffer = ctx.cameraColorBuffer;
            
            if (colorBuffer != null && colorBuffer.rt != null)
            {
                // Check the actual format
                if (Mod.Setting.DebugLogging)
                {
                    // Expected:  "[EXRCapturePass] Capture Triggered on: Main Camera, Buffer Format: B10G11R11_UFloatPack32, RenderTarget Format: RGB111110Float"
                    Mod.LOG.Info($"[EXRCapturePass] Capture Triggered on: {ctx.hdCamera.camera.name} , Buffer Format: {colorBuffer.rt.graphicsFormat} , RenderTarget Format: {colorBuffer.rt.format}");
                }

                // Send the buffer handle to the EXRScreenshotSystem.
                OnBufferReady?.Invoke(ctx, colorBuffer);
            }
            
            // Reset the flag immediately to ensure only one capture frame per request.
            _requestCapture = false;
        }

        /// <summary>
        /// Cleans up references when the Custom Pass is removed.
        /// </summary>
        protected override void Cleanup()
        {
            OnBufferReady = null;
        }
    }
}