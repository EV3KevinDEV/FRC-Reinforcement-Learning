using System;
using UnityEngine;
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
        [SerializeField] private int imageWidth = 320;
        [SerializeField] private int imageHeight = 180;

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
