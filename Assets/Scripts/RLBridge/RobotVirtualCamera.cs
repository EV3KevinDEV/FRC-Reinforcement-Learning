using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MoSimRL
{
    /// <summary>
    /// Marks a disabled Unity camera as a robot-mounted sensor. Frames are rendered
    /// on demand so sensor cameras do not add per-frame cost when nobody requests one.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RobotVirtualCamera : MonoBehaviour
    {
        public const int MinimumDimension = 16;
        public const int MaximumWidth = 640;
        public const int MaximumHeight = 480;

        [SerializeField] private string cameraId = "front";
        [SerializeField] private int imageWidth = 640;
        [SerializeField] private int imageHeight = 360;

        private Camera _camera;
        private long _captureSequence;

        public string CameraId => cameraId;
        public int ImageWidth => imageWidth;
        public int ImageHeight => imageHeight;
        public long CaptureSequence => _captureSequence;
        public static bool GraphicsDeviceAvailable =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public void Configure(string id, int width, int height)
        {
            cameraId = (id ?? string.Empty).Trim();
            imageWidth = Mathf.Clamp(width, MinimumDimension, MaximumWidth);
            imageHeight = Mathf.Clamp(height, MinimumDimension, MaximumHeight);
        }

        public RlCameraInfoDto BuildInfo(Transform robotRoot)
        {
            if (robotRoot == null)
            {
                throw new ArgumentNullException(nameof(robotRoot));
            }

            var sensorCamera = SensorCamera;
            var relativePosition = robotRoot.InverseTransformPoint(transform.position);
            var relativeRotation = Quaternion.Inverse(robotRoot.rotation) * transform.rotation;
            return new RlCameraInfoDto
            {
                name = cameraId,
                width = imageWidth,
                height = imageHeight,
                vertical_fov_degrees = sensorCamera.fieldOfView,
                near_clip = sensorCamera.nearClipPlane,
                far_clip = sensorCamera.farClipPlane,
                robot_position = Vector(relativePosition),
                robot_rotation_euler = Vector(relativeRotation.eulerAngles)
            };
        }

        public byte[] CaptureJpeg(int quality)
        {
            if (!GraphicsDeviceAvailable)
            {
                throw new InvalidOperationException("No graphics device is available.");
            }
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                throw new InvalidOperationException("The virtual camera has no ID.");
            }

            var sensorCamera = SensorCamera;
            var previousTarget = sensorCamera.targetTexture;
            var previousAspect = sensorCamera.aspect;
            var previousActive = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(
                imageWidth,
                imageHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var image = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

            try
            {
                sensorCamera.aspect = imageWidth / (float)imageHeight;
                Render(sensorCamera, renderTexture);
                RenderTexture.active = renderTexture;
                image.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0, false);
                image.Apply(false, false);
                var bytes = image.EncodeToJPG(Mathf.Clamp(quality, 1, 95));
                _captureSequence++;
                return bytes;
            }
            finally
            {
                sensorCamera.targetTexture = previousTarget;
                sensorCamera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (Application.isPlaying)
                {
                    Destroy(image);
                }
                else
                {
                    DestroyImmediate(image);
                }
            }
        }

        public IEnumerator CaptureJpegAsync(
            int quality,
            Action<byte[]> onComplete,
            Action<Exception> onError)
        {
            if (!GraphicsDeviceAvailable)
            {
                onError?.Invoke(new InvalidOperationException(
                    "No graphics device is available."));
                yield break;
            }
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                onError?.Invoke(new InvalidOperationException(
                    "The virtual camera has no ID."));
                yield break;
            }

            var sensorCamera = SensorCamera;
            var previousTarget = sensorCamera.targetTexture;
            var previousAspect = sensorCamera.aspect;
            RenderTexture renderTexture = null;
            var captureWidth = (uint)imageWidth;
            var captureHeight = (uint)imageHeight;
            AsyncGPUReadbackRequest readback = default;
            Task<byte[]> encode = null;
            byte[] bytes = null;
            Exception failure = null;
            try
            {
                try
                {
                    renderTexture = RenderTexture.GetTemporary(
                        imageWidth,
                        imageHeight,
                        24,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB);
                    sensorCamera.aspect = imageWidth / (float)imageHeight;
                    Render(sensorCamera, renderTexture);
                    sensorCamera.targetTexture = previousTarget;
                    sensorCamera.aspect = previousAspect;
                    // GPU readback completes over later player frames instead of
                    // blocking the Unity update that applies driver controls.
                    readback = AsyncGPUReadback.Request(
                        renderTexture,
                        0,
                        TextureFormat.RGBA32);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure == null)
                {
                    while (!readback.done)
                    {
                        yield return null;
                    }
                    if (readback.hasError)
                    {
                        failure = new InvalidOperationException(
                            "Asynchronous GPU readback failed.");
                    }
                }

                byte[] pixels = null;
                if (failure == null)
                {
                    try
                    {
                        pixels = readback.GetData<byte>().ToArray();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }

                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                    renderTexture = null;
                }

                if (failure == null)
                {
                    try
                    {
                        // Unity documents EncodeArrayToJPG as thread-safe. Keeping
                        // it off the player loop prevents three JPEG encodes from
                        // delaying the next controller command.
                        var captureQuality = Mathf.Clamp(quality, 1, 95);
                        encode = Task.Run(() => ImageConversion.EncodeArrayToJPG(
                            pixels,
                            GraphicsFormat.R8G8B8A8_SRGB,
                            captureWidth,
                            captureHeight,
                            0,
                            captureQuality));
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }

                if (encode != null)
                {
                    while (!encode.IsCompleted)
                    {
                        yield return null;
                    }
                    if (encode.IsFaulted)
                    {
                        failure = encode.Exception?.GetBaseException() ??
                                  new InvalidOperationException("JPEG encoding failed.");
                    }
                    else
                    {
                        try
                        {
                            bytes = encode.Result;
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                    }
                }
            }
            finally
            {
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
                sensorCamera.targetTexture = previousTarget;
                sensorCamera.aspect = previousAspect;
            }

            if (failure == null && (bytes == null || bytes.Length == 0))
            {
                failure = new InvalidOperationException("JPEG encoding returned no data.");
            }
            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            _captureSequence++;
            onComplete?.Invoke(bytes);
        }

        private Camera SensorCamera
        {
            get
            {
                if (_camera == null)
                {
                    _camera = GetComponent<Camera>();
                }
                return _camera;
            }
        }

        private static void Render(Camera sensorCamera, RenderTexture destination)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                sensorCamera.targetTexture = destination;
                sensorCamera.Render();
                return;
            }

            var request = new RenderPipeline.StandardRequest
            {
                destination = destination
            };
            if (!RenderPipeline.SupportsRenderRequest(sensorCamera, request))
            {
                throw new InvalidOperationException(
                    "The active render pipeline does not support standard camera render requests.");
            }
            RenderPipeline.SubmitRenderRequest(sensorCamera, request);
        }

        private void Reset()
        {
            SensorCamera.enabled = false;
        }

        private void OnValidate()
        {
            cameraId = (cameraId ?? string.Empty).Trim();
            imageWidth = Mathf.Clamp(imageWidth, MinimumDimension, MaximumWidth);
            imageHeight = Mathf.Clamp(imageHeight, MinimumDimension, MaximumHeight);
        }

        private static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };
    }
}
