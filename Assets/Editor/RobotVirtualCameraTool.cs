using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MoSimRL;
using RobotFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoSimulator.EditorTools
{
    public sealed class RobotVirtualCameraTool : EditorWindow
    {
        private static readonly Regex CameraIdPattern = new(
            "^[A-Za-z0-9_-]+$",
            RegexOptions.CultureInvariant);

        private readonly List<RobotMetadataSO> _robots = new();
        private string[] _robotLabels = Array.Empty<string>();
        private string[] _mountPaths = { string.Empty };
        private string[] _mountLabels = { "<Robot Root>" };
        private int _selectedRobotIndex;
        private int _selectedMountIndex;
        private bool _useAlternate;
        private GameObject _cachedPrefab;

        private string _cameraId = "front";
        private Vector3 _localPosition = new(0f, 0.5f, 0.5f);
        private Vector3 _localEulerAngles = Vector3.zero;
        private int _imageWidth = 320;
        private int _imageHeight = 180;
        private float _verticalFieldOfView = 70f;
        private float _nearClip = 0.03f;
        private float _farClip = 50f;
        private bool _showPlacementPreview = true;

        [MenuItem("MoSimulator/RL/Virtual Camera Tool _F8", priority = 150)]
        private static void Open()
        {
            var window = GetWindow<RobotVirtualCameraTool>();
            window.titleContent = new GUIContent("Virtual Cameras");
            window.minSize = new Vector2(480f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= DrawPlacementPreview;
            SceneView.duringSceneGui += DrawPlacementPreview;
            RefreshRobots();
            EditorApplication.delayCall -= OpenSelectedPrefabIfNeeded;
            EditorApplication.delayCall += OpenSelectedPrefabIfNeeded;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawPlacementPreview;
            EditorApplication.delayCall -= OpenSelectedPrefabIfNeeded;
            SceneView.RepaintAll();
        }

        private void OpenSelectedPrefabIfNeeded()
        {
            if (Application.isBatchMode || this == null ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return;
            }

            var prefab = SelectedPrefab;
            if (prefab != null)
            {
                OpenAndFramePrefab(prefab);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Robot virtual cameras", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Attach on-demand sensor cameras to a robot prefab. Camera IDs are " +
                "case-sensitive and become the names used by the RL camera API.",
                MessageType.Info);

            DrawRobotSelection();
            EditorGUILayout.Space(8f);

            var prefab = SelectedPrefab;
            if (prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Select robot metadata with a valid robot prefab.",
                    MessageType.Warning);
                return;
            }

            EnsureMounts(prefab);
            EditorGUI.BeginChangeCheck();
            DrawNewCamera(prefab);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
            EditorGUILayout.Space(12f);
            DrawExistingCameras(prefab);
        }

        private void DrawRobotSelection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Robot", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
            if (_robots.Count == 0)
            {
                EditorGUILayout.LabelField("<No robot metadata found>");
            }
            else
            {
                var nextIndex = EditorGUILayout.Popup(_selectedRobotIndex, _robotLabels);
                if (nextIndex != _selectedRobotIndex)
                {
                    _selectedRobotIndex = nextIndex;
                    _useAlternate = false;
                    InvalidateMounts();
                }
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
            {
                RefreshRobots();
            }
            EditorGUILayout.EndHorizontal();

            var metadata = SelectedMetadata;
            if (metadata == null)
            {
                return;
            }

            if (metadata.HasAlternateRobot && metadata.AlternateRobotPrefab != null)
            {
                var useAlternate = EditorGUILayout.Toggle("Alternate prefab", _useAlternate);
                if (useAlternate != _useAlternate)
                {
                    _useAlternate = useAlternate;
                    InvalidateMounts();
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Prefab", SelectedPrefab, typeof(GameObject), false);
            }

            using (new EditorGUI.DisabledScope(SelectedPrefab == null))
            {
                if (GUILayout.Button("Open and frame robot prefab", GUILayout.Height(26f)))
                {
                    OpenAndFramePrefab(SelectedPrefab);
                }
            }
        }

        private static void OpenAndFramePrefab(GameObject prefab)
        {
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (!IsEditablePrefabPath(prefabPath))
            {
                EditorUtility.DisplayDialog(
                    "Virtual camera",
                    "The selected robot must be an editable prefab asset.",
                    "OK");
                return;
            }

            var prefabStage = PrefabStageUtility.OpenPrefab(prefabPath);
            var prefabRoot = prefabStage != null ? prefabStage.prefabContentsRoot : null;
            if (prefabRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Virtual camera",
                    $"Unity could not open robot prefab '{prefab.name}'.",
                    "OK");
                return;
            }

            Selection.activeGameObject = prefabRoot;
            EditorGUIUtility.PingObject(prefabRoot);
            EditorApplication.delayCall += () =>
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return;
                }

                sceneView.FrameSelected();
                sceneView.Repaint();
            };
        }

        private void DrawNewCamera(GameObject prefab)
        {
            EditorGUILayout.LabelField("Add camera", EditorStyles.boldLabel);
            _cameraId = EditorGUILayout.TextField("Camera ID", _cameraId);
            _selectedMountIndex = EditorGUILayout.Popup(
                "Mount transform",
                Mathf.Clamp(_selectedMountIndex, 0, _mountLabels.Length - 1),
                _mountLabels);
            _localPosition = EditorGUILayout.Vector3Field("Local position", _localPosition);
            _localEulerAngles = EditorGUILayout.Vector3Field(
                "Local rotation",
                _localEulerAngles);

            EditorGUILayout.Space(4f);
            _imageWidth = EditorGUILayout.IntField("Image width", _imageWidth);
            _imageHeight = EditorGUILayout.IntField("Image height", _imageHeight);
            _verticalFieldOfView = EditorGUILayout.Slider(
                "Vertical field of view",
                _verticalFieldOfView,
                1f,
                179f);
            _nearClip = EditorGUILayout.FloatField("Near clip", _nearClip);
            _farClip = EditorGUILayout.FloatField("Far clip", _farClip);
            _showPlacementPreview = EditorGUILayout.Toggle(
                new GUIContent(
                    "Scene placement preview",
                    "Draw the camera position, direction, and field of view in the Scene view."),
                _showPlacementPreview);

            if (_showPlacementPreview)
            {
                EditorGUILayout.HelpBox(
                    "Drag the red, green, and blue arrows to move the camera. Drag the " +
                    "colored rings to rotate it. The cyan pyramid previews its field of view.",
                    MessageType.None);
            }

            var validation = ValidateNewCamera(prefab);
            if (validation != null)
            {
                EditorGUILayout.HelpBox(validation, MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(validation != null))
            {
                if (GUILayout.Button("Add virtual camera", GUILayout.Height(28f)))
                {
                    AddCamera(prefab);
                }
            }
        }

        private void DrawPlacementPreview(SceneView _)
        {
            if (!_showPlacementPreview)
            {
                return;
            }

            var prefab = SelectedPrefab;
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefab == null || prefabStage == null || prefabStage.prefabContentsRoot == null)
            {
                return;
            }

            var selectedPrefabPath = AssetDatabase.GetAssetPath(prefab);
            if (!string.Equals(
                    prefabStage.assetPath,
                    selectedPrefabPath,
                    StringComparison.Ordinal))
            {
                return;
            }

            EnsureMounts(prefab);
            var mountPath = _mountPaths[Mathf.Clamp(
                _selectedMountIndex,
                0,
                _mountPaths.Length - 1)];
            var prefabRoot = prefabStage.prefabContentsRoot.transform;
            var mount = string.IsNullOrEmpty(mountPath)
                ? prefabRoot
                : prefabRoot.Find(mountPath);
            if (mount == null)
            {
                return;
            }

            var cameraPosition = mount.TransformPoint(_localPosition);
            var cameraRotation = mount.rotation * Quaternion.Euler(_localEulerAngles);
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = previousColor;

            EditorGUI.BeginChangeCheck();
            var movedPosition = Handles.PositionHandle(cameraPosition, mount.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                _localPosition = mount.InverseTransformPoint(movedPosition);
                cameraPosition = movedPosition;
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            var rotatedCamera = Handles.RotationHandle(cameraRotation, cameraPosition);
            if (EditorGUI.EndChangeCheck())
            {
                var localRotation = Quaternion.Inverse(mount.rotation) * rotatedCamera;
                _localEulerAngles = NormalizeEulerAngles(localRotation.eulerAngles);
                cameraRotation = rotatedCamera;
                Repaint();
            }

            if (Event.current.type != EventType.Repaint)
            {
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
                return;
            }

            var aspect = Mathf.Max(1, _imageWidth) / (float)Mathf.Max(1, _imageHeight);
            var fieldOfView = Mathf.Clamp(_verticalFieldOfView, 1f, 179f);
            var nearDistance = Mathf.Max(0.01f, _nearClip);
            var previewDistance = Mathf.Clamp(_farClip, 0.1f, 3f);
            var previewNearDistance = Mathf.Clamp(
                nearDistance,
                0.01f,
                previewDistance * 0.95f);
            var nearCorners = CalculateFrustumCorners(
                cameraPosition,
                cameraRotation,
                previewNearDistance,
                fieldOfView,
                aspect);
            var farCorners = CalculateFrustumCorners(
                cameraPosition,
                cameraRotation,
                previewDistance,
                fieldOfView,
                aspect);

            Handles.color = new Color(1f, 0.75f, 0.05f, 1f);
            var markerSize = HandleUtility.GetHandleSize(cameraPosition) * 0.08f;
            Handles.SphereHandleCap(
                0,
                cameraPosition,
                Quaternion.identity,
                markerSize,
                EventType.Repaint);
            Handles.DrawDottedLine(mount.position, cameraPosition, 4f);
            Handles.ArrowHandleCap(
                0,
                cameraPosition,
                cameraRotation,
                markerSize * 4f,
                EventType.Repaint);

            Handles.color = new Color(0f, 0.9f, 1f, 0.95f);
            DrawClosedPolyline(nearCorners);
            DrawClosedPolyline(farCorners);
            for (var cornerIndex = 0; cornerIndex < farCorners.Length; cornerIndex++)
            {
                Handles.DrawLine(cameraPosition, farCorners[cornerIndex]);
            }

            var labelPosition = cameraPosition + cameraRotation *
                new Vector3(0f, markerSize * 2f, markerSize * 2f);
            Handles.Label(
                labelPosition,
                $"Camera preview: {(_cameraId ?? string.Empty).Trim()}",
                EditorStyles.whiteBoldLabel);

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static Vector3 NormalizeEulerAngles(Vector3 eulerAngles) => new(
            Mathf.DeltaAngle(0f, eulerAngles.x),
            Mathf.DeltaAngle(0f, eulerAngles.y),
            Mathf.DeltaAngle(0f, eulerAngles.z));

        private static Vector3[] CalculateFrustumCorners(
            Vector3 position,
            Quaternion rotation,
            float distance,
            float verticalFieldOfView,
            float aspect)
        {
            var halfHeight = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            var halfWidth = halfHeight * aspect;
            var center = position + rotation * Vector3.forward * distance;
            var right = rotation * Vector3.right * halfWidth;
            var up = rotation * Vector3.up * halfHeight;
            return new[]
            {
                center - right - up,
                center + right - up,
                center + right + up,
                center - right + up,
            };
        }

        private static void DrawClosedPolyline(IReadOnlyList<Vector3> points)
        {
            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                Handles.DrawLine(points[pointIndex], points[(pointIndex + 1) % points.Count]);
            }
        }

        private void DrawExistingCameras(GameObject prefab)
        {
            EditorGUILayout.LabelField("Configured cameras", EditorStyles.boldLabel);
            var cameras = prefab.GetComponentsInChildren<RobotVirtualCamera>(true)
                .OrderBy(camera => camera.CameraId, StringComparer.Ordinal)
                .ThenBy(camera => AnimationUtility.CalculateTransformPath(
                    camera.transform,
                    prefab.transform), StringComparer.Ordinal)
                .ToArray();
            if (cameras.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This prefab has no virtual cameras yet.",
                    MessageType.None);
                return;
            }

            foreach (var virtualCamera in cameras)
            {
                var path = AnimationUtility.CalculateTransformPath(
                    virtualCamera.transform,
                    prefab.transform);
                var sensorCamera = virtualCamera.GetComponent<Camera>();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(virtualCamera.CameraId, EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                {
                    RemoveCamera(prefab, path);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Path", path);
                EditorGUILayout.LabelField(
                    "Output",
                    $"{virtualCamera.ImageWidth} x {virtualCamera.ImageHeight} JPEG");
                if (sensorCamera != null)
                {
                    EditorGUILayout.LabelField(
                        "Lens",
                        $"{sensorCamera.fieldOfView:0.#}° vertical FOV, " +
                        $"{sensorCamera.nearClipPlane:0.###}–{sensorCamera.farClipPlane:0.###} m");
                }
                EditorGUILayout.EndVertical();
            }
        }

        private string ValidateNewCamera(GameObject prefab)
        {
            var id = (_cameraId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                return "Camera ID is required.";
            }
            if (!CameraIdPattern.IsMatch(id))
            {
                return "Camera ID may contain only letters, numbers, underscores, and hyphens.";
            }
            if (_imageWidth < RobotVirtualCamera.MinimumDimension ||
                _imageWidth > RobotVirtualCamera.MaximumWidth ||
                _imageHeight < RobotVirtualCamera.MinimumDimension ||
                _imageHeight > RobotVirtualCamera.MaximumHeight)
            {
                return $"Image size must be between {RobotVirtualCamera.MinimumDimension} x " +
                       $"{RobotVirtualCamera.MinimumDimension} and " +
                       $"{RobotVirtualCamera.MaximumWidth} x {RobotVirtualCamera.MaximumHeight}.";
            }
            if (_nearClip <= 0f || _farClip <= _nearClip)
            {
                return "Clip planes must satisfy 0 < near < far.";
            }
            if (prefab.GetComponentsInChildren<RobotVirtualCamera>(true).Any(camera =>
                    string.Equals(camera.CameraId, id, StringComparison.Ordinal)))
            {
                return $"Camera ID '{id}' already exists on this prefab.";
            }
            return null;
        }

        private void AddCamera(GameObject prefab)
        {
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (!IsEditablePrefabPath(prefabPath))
            {
                EditorUtility.DisplayDialog(
                    "Virtual camera",
                    "The selected robot must be an editable prefab asset.",
                    "OK");
                return;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                var mountPath = _mountPaths[Mathf.Clamp(
                    _selectedMountIndex,
                    0,
                    _mountPaths.Length - 1)];
                var mount = string.IsNullOrEmpty(mountPath)
                    ? root.transform
                    : root.transform.Find(mountPath);
                if (mount == null)
                {
                    throw new InvalidOperationException(
                        $"Mount transform '{mountPath}' no longer exists.");
                }

                var id = _cameraId.Trim();
                var cameraObject = new GameObject($"Virtual Camera ({id})");
                cameraObject.transform.SetParent(mount, false);
                cameraObject.transform.localPosition = _localPosition;
                cameraObject.transform.localEulerAngles = _localEulerAngles;

                var sensorCamera = cameraObject.AddComponent<Camera>();
                sensorCamera.enabled = false;
                sensorCamera.fieldOfView = _verticalFieldOfView;
                sensorCamera.nearClipPlane = _nearClip;
                sensorCamera.farClipPlane = _farClip;
                sensorCamera.depth = -100f;
                sensorCamera.allowHDR = false;
                sensorCamera.allowMSAA = false;

                var virtualCamera = cameraObject.AddComponent<RobotVirtualCamera>();
                virtualCamera.Configure(id, _imageWidth, _imageHeight);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"Added virtual camera '{id}' to '{prefab.name}' at " +
                    $"'{(string.IsNullOrEmpty(mountPath) ? "<Robot Root>" : mountPath)}'.",
                    prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Could not add virtual camera", exception.Message, "OK");
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            InvalidateMounts();
            Repaint();
        }

        private void RemoveCamera(GameObject prefab, string cameraPath)
        {
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (!IsEditablePrefabPath(prefabPath))
            {
                return;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                var cameraTransform = string.IsNullOrEmpty(cameraPath)
                    ? root.transform
                    : root.transform.Find(cameraPath);
                var virtualCamera = cameraTransform != null
                    ? cameraTransform.GetComponent<RobotVirtualCamera>()
                    : null;
                if (virtualCamera == null || cameraTransform == root.transform)
                {
                    throw new InvalidOperationException(
                        $"Virtual camera at '{cameraPath}' could not be removed safely.");
                }

                var id = virtualCamera.CameraId;
                DestroyImmediate(cameraTransform.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Removed virtual camera '{id}' from '{prefab.name}'.", prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Could not remove virtual camera", exception.Message, "OK");
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            InvalidateMounts();
            Repaint();
        }

        private void RefreshRobots()
        {
            var selectedGuid = SelectedMetadata != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(SelectedMetadata))
                : string.Empty;
            _robots.Clear();
            _robots.AddRange(AssetDatabase.FindAssets("t:RobotMetadataSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<RobotMetadataSO>)
                .Where(metadata => metadata != null && metadata.RobotPrefab != null)
                .OrderBy(metadata => metadata.TeamNumber)
                .ThenBy(metadata => metadata.TeamName, StringComparer.Ordinal));
            _robotLabels = _robots.Select(metadata =>
                $"{metadata.TeamNumber} - {metadata.TeamName}").ToArray();

            var retainedIndex = _robots.FindIndex(metadata =>
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(metadata)) == selectedGuid);
            _selectedRobotIndex = retainedIndex >= 0
                ? retainedIndex
                : Mathf.Clamp(_selectedRobotIndex, 0, Math.Max(0, _robots.Count - 1));
            InvalidateMounts();
            Repaint();
        }

        private void EnsureMounts(GameObject prefab)
        {
            if (_cachedPrefab == prefab)
            {
                return;
            }

            var paths = new List<string> { string.Empty };
            paths.AddRange(prefab.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform != prefab.transform)
                .Select(transform => AnimationUtility.CalculateTransformPath(
                    transform,
                    prefab.transform)));
            _mountPaths = paths.ToArray();
            _mountLabels = paths.Select(path =>
                string.IsNullOrEmpty(path) ? "<Robot Root>" : path).ToArray();
            _selectedMountIndex = Mathf.Clamp(
                _selectedMountIndex,
                0,
                _mountPaths.Length - 1);
            _cachedPrefab = prefab;
        }

        private void InvalidateMounts()
        {
            _cachedPrefab = null;
            _mountPaths = new[] { string.Empty };
            _mountLabels = new[] { "<Robot Root>" };
            _selectedMountIndex = 0;
        }

        private RobotMetadataSO SelectedMetadata =>
            _robots.Count == 0
                ? null
                : _robots[Mathf.Clamp(_selectedRobotIndex, 0, _robots.Count - 1)];

        private GameObject SelectedPrefab
        {
            get
            {
                var metadata = SelectedMetadata;
                if (metadata == null)
                {
                    return null;
                }
                return _useAlternate && metadata.HasAlternateRobot &&
                       metadata.AlternateRobotPrefab != null
                    ? metadata.AlternateRobotPrefab
                    : metadata.RobotPrefab;
            }
        }

        private static bool IsEditablePrefabPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            string.Equals(Path.GetExtension(path), ".prefab", StringComparison.OrdinalIgnoreCase);
    }
}
