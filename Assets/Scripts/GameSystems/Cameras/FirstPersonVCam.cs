using MoSimCore.BaseClasses;
using Unity.Cinemachine;
using UnityEngine;

namespace GameSystems.Cameras
{
    public class FirstPersonVCam : BaseVCamScript
    {
        public CinemachineThirdPersonFollow Follow { get; private set; }
        private Transform _headingTarget;
        private bool _flipped;

        private void Start()
        {
            Follow = GetComponent<CinemachineThirdPersonFollow>();
            ConfigureHeadingTarget();
        }

        private void Update()
        {
            if (TargetRobot != null && _headingTarget == null)
            {
                ConfigureHeadingTarget();
            }
        }

        public override void SetCameraTarget(Transform target)
        {
            if (target == null)
            {
                Debug.LogError("Target is null");
                return;
            }
            
            TargetRobot = target;
            
            if (Vcam == null)
            {
                Debug.LogError("Virtual Camera is not assigned.");
                
                Vcam = GetComponent<CinemachineCamera>();
                
                if (Vcam == null)
                {
                    Debug.LogError("CinemachineCamera component not found on this GameObject.");
                    return;
                }
            }

            ConfigureHeadingTarget();
        }

        public override void FlipCamera()
        {
            _flipped = !_flipped;
            if (_headingTarget != null)
            {
                _headingTarget.localRotation = HeadingRotation();
            }
        }

        private void ConfigureHeadingTarget()
        {
            if (TargetRobot == null || Vcam == null)
            {
                return;
            }

            if (_headingTarget == null || _headingTarget.parent != TargetRobot)
            {
                if (_headingTarget != null)
                {
                    Destroy(_headingTarget.gameObject);
                }
                var targetObject = new GameObject($"{name} Robot Heading Target")
                {
                    hideFlags = HideFlags.HideInHierarchy
                };
                _headingTarget = targetObject.transform;
                _headingTarget.SetParent(TargetRobot, false);
                _headingTarget.localPosition = Vector3.zero;
            }

            _headingTarget.localRotation = HeadingRotation();
            Vcam.Follow = _headingTarget;
            Vcam.LookAt = _headingTarget;
        }

        private Quaternion HeadingRotation() => Quaternion.Euler(
            0f,
            _flipped ? 180f : 0f,
            0f);

        private void OnDestroy()
        {
            if (_headingTarget != null)
            {
                Destroy(_headingTarget.gameObject);
            }
        }
    }
}
