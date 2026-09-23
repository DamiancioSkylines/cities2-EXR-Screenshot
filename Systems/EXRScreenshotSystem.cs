using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.IO;
using Game.SceneFlow;
using Game.Simulation;
using Unity.Entities;
using Object = UnityEngine.Object;

namespace EXRScreenshot.Systems
{
    public class EXRScreenshotSystem
    {
        private bool _isCapturing;
        private Coroutine _captureCoroutine;
        
        private RTHandle _captureRTHandle;
        private RenderTexture _captureRT;
        private RenderTexture _cameraRT;
        private RenderTexture _originalTarget;

        private readonly GameObject _captureVolumeHolder;
        private readonly CustomPassVolume _captureVolume;
        private readonly EXRCapturePass _capturePass;
        
        private Camera _mainCam;
        private HDAdditionalCameraData _hdData;
        
        private bool _originalAllowDynRes;
        private int _originalRTWidth;
        private int _originalRTHeight;

        private SimulationSystem _simulationSystem;
        private float _originalSpeed;

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
            _captureCoroutine = GameManager.instance.StartCoroutine(CaptureRoutine());
        }

        // <summary>
        // Overall core logic is:
        // Pause simulation, change resolution
        // Wait some frames, while game renders into cameraRT so SSR/SSAO/SSGI accumulates
        // EXRCapturePass grabs framebuffer and blit copy it to captureRTHandle its captureRT texture
        // Asynchronous read GPU texture data and encode and save to EXR
        // Disable Volume and Pass, Restore original camera stuff and release memory.
        // </summary>
        private IEnumerator CaptureRoutine()
        {
            
            _mainCam = Camera.main;
            if (!_mainCam) yield break;
            string currentMetadata = null;
                
            if (Mod.Setting.MetadataLogging)
            {
                try {currentMetadata = VolumeInspection.GetActiveMetadata();}
                catch (Exception e){Mod.LOG.Error($"[EXRScreenshotSystem] Metadata failed: {e.Message}");}
            }

            _captureRTHandle = null;
            _cameraRT = null;
            
            _originalAllowDynRes = false;
            _hdData = null;
            _originalTarget = null;
            
            _originalRTWidth = RTHandles.rtHandleProperties.currentViewportSize.x;
            _originalRTHeight = RTHandles.rtHandleProperties.currentViewportSize.y;

            _simulationSystem = World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<SimulationSystem>();
            _originalSpeed = 1f;

            if (_simulationSystem is not null)
            {
                _originalSpeed = _simulationSystem.selectedSpeed;
                _simulationSystem.selectedSpeed = 0; // Pause simulation
            }
            
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

                // Force Camera screen resolution or super resolution
                if (_mainCam.TryGetComponent(out _hdData))
                {
                    _originalAllowDynRes = _hdData.allowDynamicResolution;
                    _hdData.allowDynamicResolution = false; // "Disable" DLSS/FSR for capture frame
                }

                // cameraRT is temporary target for camera to render over time, because of resolution change is initially empty.
                // 24-bit depth buffer and DefaultHDR is default game setup
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
                    var txtPath = Path.ChangeExtension(exrPath, ".txt");
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
                                        File.WriteAllText(txtPath, currentMetadata);
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
                
                // Game should already be running normally, but hold on before releasing captureRTHandle
                yield return new WaitUntil(() => exportFinished);
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] EXR capture coroutine complete."); }
            }
            finally
            {
                // Restore and release even if coroutine would fall apart
                RestoreCamera();
                ReleaseCaptureTarget();
                
                _isCapturing = false;
                _captureCoroutine = null;
            }
        }
        
        private void RestoreCamera()
        {
            if (_captureVolume) { _captureVolume.enabled = false; }
            if (_capturePass is not null) { _capturePass.OnBufferReady = null; }
            if (_mainCam) { _mainCam.targetTexture = _originalTarget; }
            if (_hdData) { _hdData.allowDynamicResolution = _originalAllowDynRes; }
            if (_cameraRT) { RenderTexture.ReleaseTemporary(_cameraRT); _cameraRT = null; }
            // Most Important: Shrink the RTHandle back to original size to free VRAM
            // Only way to reset the current maximum resolution is using ResetReferenceSize instead of SetReferenceSize that can only increase but not decrease size.
            // https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@13.1/manual/rthandle-system-using.html
            if (_originalRTWidth > 0 && _originalRTHeight > 0)
            {
                RTHandles.ResetReferenceSize(_originalRTWidth, _originalRTHeight);
            }
            if (_simulationSystem is not null) { _simulationSystem.selectedSpeed = _originalSpeed; }
        }

        private void ReleaseCaptureTarget()
        {
            // _captureRTHandle.Release removes captureRT automatically, but not before exportFinished
            if (_captureRTHandle is not null) { _captureRTHandle.Release(); _captureRTHandle = null; _captureRT = null; }
        }

        public void Cleanup()
        {
            // Called from Mod's onDispose
            if (_captureCoroutine is not null && GameManager.instance)
            {
                GameManager.instance.StopCoroutine(_captureCoroutine); 
                if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] EXR capture coroutine stopped"); }
            }

            RestoreCamera();
            ReleaseCaptureTarget();

            if (_captureVolumeHolder != null)
            {
                Object.Destroy(_captureVolumeHolder);
                // if (Mod.Setting.DebugLogging) { Mod.LOG.Info("[EXRScreenshotSystem] CaptureVolumeHolder destroyed."); }
            }
            
        }
    }
}