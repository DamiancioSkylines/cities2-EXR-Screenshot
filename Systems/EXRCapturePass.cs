using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using JetBrains.Annotations;

namespace EXRScreenshot.Systems
{
    [UsedImplicitly]
    public class EXRCapturePass : CustomPass
    {
        // Full context passed so we can use ctx.cmd (pipeline command buffer)
        // and ctx.hdCamera.actualWidth/actualHeight to verify rendered size.
        public System.Action<CustomPassContext, RTHandle> OnBufferReady;
        private bool _requestCapture;

        public void RequestFrame() => _requestCapture = true;

        protected override void Execute(CustomPassContext ctx)
        {
            if (!_requestCapture || ctx.hdCamera.camera.cameraType != UnityEngine.CameraType.Game)
                return;

            var rawBuffer = ctx.cameraColorBuffer;
            if (rawBuffer == null) return;

            OnBufferReady?.Invoke(ctx, rawBuffer);
            _requestCapture = false;
        }

        protected override void Cleanup()
        {
            OnBufferReady = null;
        }
    }
}