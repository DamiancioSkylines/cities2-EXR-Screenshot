using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EXRScreenshot.Systems
{
    public class EXRCapturePass : CustomPass
    {
        public System.Action<RTHandle> OnBufferReady;
        private bool m_RequestCapture = false;

        public void RequestFrame() => m_RequestCapture = true;

        protected override void Execute(CustomPassContext ctx)
        {
            // We only want to capture the Game Camera, not the UI or Scene cameras
            if (!m_RequestCapture || ctx.hdCamera.camera.cameraType != CameraType.Game)
                return;

            // This is the RAW HDR light data (Linear 16/32-bit Float)
            // It has NO UI, NO Banding, and NO Tonemapping yet.
            RTHandle rawBuffer = ctx.cameraColorBuffer;

            if (rawBuffer != null)
            {
                OnBufferReady?.Invoke(rawBuffer);
                m_RequestCapture = false; // Reset after one frame
            }
        }
        // Clean-up to prevent memory leaks
        protected override void Cleanup()
        {
            OnBufferReady = null;
        }
    }
}