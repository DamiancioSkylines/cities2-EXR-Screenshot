using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using JetBrains.Annotations;

namespace EXRScreenshot.Systems
{
    [UsedImplicitly] // the class is being used by an external framework (Unity)
    public class EXRCapturePass : CustomPass
    {
        public System.Action<RTHandle> OnBufferReady;
        private bool _requestCapture;

        public void RequestFrame() => _requestCapture = true;

        protected override void Execute(CustomPassContext ctx)
        {
            // Capture the Game Camera, not the UI or Scene cameras
            if (!_requestCapture || ctx.hdCamera.camera.cameraType != CameraType.Game)
                return;

            // This is the RAW HDR light data (Linear 16bit Float)
            // This buffer is before post-processing
            // NO tonemapping, LUT, antialiasing, post process colour adjustments
            // NO bloom and other post process stuff like that
            // It is after camera autoexposure but before final post exposure used in photomode or Lumina.
            var rawBuffer = ctx.cameraColorBuffer;

            if (rawBuffer == null) return;
            OnBufferReady?.Invoke(rawBuffer);
            _requestCapture = false; // Reset after
        }
        // Clean-up to prevent memory leaks
        protected override void Cleanup()
        {
            OnBufferReady = null;
        }
    }
}