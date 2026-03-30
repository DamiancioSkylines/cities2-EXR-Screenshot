using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using JetBrains.Annotations;

namespace EXRScreenshot.Systems
{
    /// <summary>
    /// A lightweight Custom Pass designed to "snoop" on the HDRP render pipeline.
    /// Unlike standard passes, this does not draw anything; it simply provides 
    /// a thread-safe hook to access the internal camera buffers at the exact
    /// moment they are finished rendering.
    /// </summary>
    [UsedImplicitly]
    public class EXRCapturePass : CustomPass
    {
        // We pass CustomPassContext to allow the receiver to check hdCamera properties
        // and ctx.cmd (the current CommandBuffer) for pipeline-synced operations.
        public System.Action<CustomPassContext, RTHandle> OnBufferReady;
        private bool _requestCapture;

        /// <summary>
        /// Triggers a capture for the next valid game frame.
        /// </summary>
        public void RequestFrame() => _requestCapture = true;

        protected override void Execute(CustomPassContext ctx)
        {
            // Camera Type Guard
            // CS2 renders many things (UI, Thumbnails, Shadows). This ensures we ONLY
            // trigger on the actual Game camera, preventing accidental captures of 
            // internal utility buffers or black frames.
            if (!_requestCapture || ctx.hdCamera.camera.cameraType != UnityEngine.CameraType.Game)
                return;

            // Direct RTHandle Access
            // By using ctx.cameraColorBuffer directly, we avoid manually searching 
            // for the texture by ID. This is faster and more reliable when 
            // using the RTHandle.ResetReferenceSize scaling method.
            var colorBuffer = ctx.cameraColorBuffer;
            
            if (colorBuffer != null && colorBuffer.rt != null)
            {
                // LOGGING: Check the actual bit-depth and format of the HDRP buffer
                if (Mod.Setting.DebugLogging)
                {
                    Mod.LOG.Info($"[EXRCapturePass] Capture Triggered on: {ctx.hdCamera.camera.name}");
                    Mod.LOG.Info($"[EXRCapturePass] Buffer Format: {colorBuffer.rt.graphicsFormat}");
                    Mod.LOG.Info($"[EXRCapturePass] RenderTarget Format: {colorBuffer.rt.format}");
                }

                // Send the buffer handle to the EXRScreenshotSystem.
                // This is "Zero-Copy"—we are passing a reference, not duplicating pixels on GPU.
                OnBufferReady?.Invoke(ctx, colorBuffer);
            }
            
            // Reset the flag immediately to ensure we only capture one frame per request.
            _requestCapture = false;
        }

        /// <summary>
        /// Cleans up references when the Custom Pass is removed.
        /// </summary>
        protected override void Cleanup()
        {
            // In Unity modding, if the mod is reloaded, old references can cause 
            // 'MissingReferenceException' which can crash the entire HDRP loop 
            // (turning the user's screen black). This ensures the pipeline is "sanitized".
            OnBufferReady = null;
        }
    }
}