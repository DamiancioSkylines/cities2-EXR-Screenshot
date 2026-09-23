using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.IO;
using Game.SceneFlow;
using Object = UnityEngine.Object;

namespace EXRScreenshot.Systems
{
    public class EXRScreenshotSystem
    {
        private bool _isCapturing;

        private RenderTexture _originalTarget;
        private RenderTexture _cameraRT;

        private RenderTexture _captureRT;
        private RTHandle _captureRTHandle;

        private readonly GameObject _captureVolumeHolder;
        private readonly CustomPassVolume _captureVolume;
        private readonly EXRCapturePass _capturePass;
        
        private Camera _mainCam;
        private HDAdditionalCameraData _hdData;
        private bool _originalAllowDynRes;
        private int _originalRTWidth;
        private int _originalRTHeight;

        public EXRScreenshotSystem()
        {
            if (Mod.Setting.DebugLogging) Mod.LOG.Info("[EXRScreenshotSystem] EXRScreenshotSystem initialized.");
            
            _captureVolumeHolder = new GameObject("EXRScreenshot_CaptureVolume");
            Object.DontDestroyOnLoad(_captureVolumeHolder); // Prevents it from being deleted during scene shenanigans
            
            _captureVolume = _captureVolumeHolder.AddComponent<CustomPassVolume>();
            _captureVolume.isGlobal = true;
            _captureVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            
            _capturePass = new EXRCapturePass();
            _captureVolume.customPasses.Add(_capturePass);
            _captureVolume.enabled = false;
        }
        
        public void CaptureEXR()
        {
            if (_isCapturing)
            {
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] Capture already in progress, ignoring.");}
                return;
            }
            GameManager.instance.StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            
            _mainCam = Camera.main;
            if (!_mainCam) yield break;
            string currentMetadata = null;
                
            if (Mod.Setting.MetadataLogging)
            {
                try{currentMetadata = VolumeInspection.GetActiveMetadata();}
                catch (Exception e){Mod.LOG.Error($"[EXRScreenshotSystem] Metadata failed: {e.Message}");}
            }

            _captureRTHandle = null;
            _cameraRT = null;
            
            _originalAllowDynRes = false;
            _hdData = null;
            _originalTarget = null;
            
            _originalRTWidth = RTHandles.rtHandleProperties.currentViewportSize.x;
            _originalRTHeight = RTHandles.rtHandleProperties.currentViewportSize.y;

            try
            {
                _isCapturing = true;
                // Prepare target resolution
                var scale = Mod.Setting.TakeSuperResolution ? Mod.Setting.SupersampleScale : 1.0f;
                var targetWidth = Mathf.RoundToInt(_mainCam.pixelWidth * scale);
                var targetHeight = Mathf.RoundToInt(_mainCam.pixelHeight * scale);
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info($"[EXRScreenshotSystem] EXR capture coroutine started: {targetWidth}x{targetHeight} (Scale: {scale}x)"); }

                // Setup Capture Target
                _captureRT = new RenderTexture(targetWidth, targetHeight, 0, GraphicsFormat.R16G16B16A16_SFloat);
                _captureRT.name = "EXRScreenshot_Capture_Target";
                _captureRT.Create();
                _captureRTHandle = RTHandles.Alloc(_captureRT);

                // Force Camera to recognize the high-res target
                _hdData = _mainCam.GetComponent<HDAdditionalCameraData>();
                _originalAllowDynRes = _hdData.allowDynamicResolution;
                _hdData.allowDynamicResolution = false; // "Disable" DLSS/FSR for capture frame

                // cameraRT acts as the temporary target for camera to render over time, because resolution change is initially empty.
                // 24-bit depth and DefaultHDR is default game setup.
                _cameraRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.DefaultHDR);
                _originalTarget = _mainCam.targetTexture;
                _mainCam.targetTexture = _cameraRT;

                RTHandles.SetReferenceSize(targetWidth, targetHeight);
                
                _captureVolume.enabled = true;

                // Wait for several frames while game renders on cameraRT, when done copy colour buffer using EXRCapturePass and blit to captureRT

                // Warmup because SSR, AO, SSGI need more frames to resolve.
                // No warmup frames break glass not sure why, while more frames suffer diminishing returns, denoising is post process I think.
                var warmupFrames = (int)Mod.Setting.AccumulationFramesDropdown;
                if (Mod.Setting.DebugLogging && warmupFrames > 0) { Mod.LOG.Info($"[EXRScreenshotSystem] Warming up for {warmupFrames} accumulation frames..."); }
                for (var i = 0; i < warmupFrames; i++) yield return new WaitForEndOfFrame();
                
                var exportFinished = false;
                var frameCaptured = false;

                _capturePass.OnBufferReady = (ctx, colorBuffer) =>
                {
                    HDUtils.BlitCameraTexture(ctx.cmd, colorBuffer, _captureRTHandle);
                    frameCaptured = true;

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    var exrPath = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Screenshots", "EXR", $"Screenshot_{timestamp}.exr"));
                    var textPath = Path.ChangeExtension(exrPath, ".txt");
                    var exrDir = Path.GetDirectoryName(exrPath);
                    
                    ctx.cmd.RequestAsyncReadback(_captureRT, request =>
                    {
                        try
                        {
                            if (request.hasError)
                            {
                                Mod.LOG.Error("[EXRScreenshotSystem] GPU Readback error.");
                                exportFinished = true;
                                return;
                            }
                            
                            var compressionFlag = (Texture2D.EXRFlags)Mod.Setting.CompressionDropdown;

                            // EncodeNativeArrayToEXR is a Unity API — must run on main thread.
                            var exrBytes = ImageConversion.EncodeNativeArrayToEXR(
                                request.GetData<byte>(),
                                _captureRT.graphicsFormat,
                                (uint)targetWidth,
                                (uint)targetHeight,
                                0,
                                compressionFlag
                            );

                            // Copy encoded bytes to managed array before handing off.
                            var encodedBytes = exrBytes.ToArray();
                            exrBytes.Dispose();
                            
                            System.Threading.Tasks.Task.Run(() =>
                            {
                                try
                                {

                                    if (!string.IsNullOrEmpty(exrDir) && !Directory.Exists(exrDir)) Directory.CreateDirectory(exrDir);

                                    // Save EXR
                                    File.WriteAllBytes(exrPath, encodedBytes);
                                    if (Mod.Setting.DebugLogging) { Mod.LOG.Info($"[EXRScreenshotSystem] Saved EXR: {exrPath}"); }

                                    // Save Metadata
                                    if (Mod.Setting.MetadataLogging && currentMetadata != null)
                                    {
                                        File.WriteAllText(textPath, currentMetadata);
                                    }
                                }
                                catch (Exception e) { Mod.LOG.Error($"[EXRScreenshotSystem] IO Error: {e.Message}"); }
                                finally { exportFinished = true; }
                            });
                        }
                        catch (Exception e)
                        {
                            Mod.LOG.Error($"[EXRScreenshotSystem] Readback processing failed: {e.Message}");
                            // Just in case smth shitfaced
                            exportFinished = true;
                        }
                    });
                };

                _capturePass.RequestFrame();
                // Wait for the frame to be captured
                yield return new WaitUntil(() => frameCaptured);
                
                RestoreCamera();
                // Wait for readback/disk — game should already be running normally
                yield return new WaitUntil(() => exportFinished);

                // Clean-up captureRT stays alive until readback is done, aka consumed and no longer needed by the camera. THEN release
                ReleaseCaptureTarget();
                
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] EXR capture coroutine complete."); }
            }
            finally
            {
                //RestoreCamera();
                _isCapturing = false;
            }
        }
        
        private void RestoreCamera()
        {
            // Restore Camera stuff after frame has been captured
            _captureVolume.enabled = false;
            _mainCam.targetTexture = _originalTarget;
            RenderTexture.ReleaseTemporary(_cameraRT);
            // Most Important: Shrink the RTHandle back to original size to free VRAM
            // Only way to reset the current maximum resolution is using ResetReferenceSize instead of SetReferenceSize that can only increase but not decrease size.
            // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@13.1/manual/rthandle-system-using.html
            RTHandles.ResetReferenceSize(_originalRTWidth, _originalRTHeight);
                
            // Restore DLSS/FSR ability to reduce internal resolution
            _hdData.allowDynamicResolution = _originalAllowDynRes;
            
        }
        private void ReleaseCaptureTarget()
        {
            _captureRTHandle.Release();
            _captureRT.Release();
            Object.Destroy(_captureRT);
        }
    }
}