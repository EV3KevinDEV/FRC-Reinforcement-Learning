using System;
using System.Collections;
using System.Linq;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimCore.SceneTransitions;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.Drivetrain;
using RobotFramework.Controllers.GamePieceSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RobotFramework
{
    /// <summary>
    /// Abstract base class for all robots in the game.
    /// Handles alliance assignment, camera switching, input mapping, and controller feedback.
    /// Inherit from this class to create game-specific robot implementations.
    /// </summary>
    public abstract class RobotBase : MonoBehaviour
    {
        [Header("Alliance & Appearance")]
        [SerializeField]
        [Tooltip("The alliance this robot belongs to (blue or red).")]
        private Alliance alliance;
        
        /// <summary>Gets the current alliance of this robot.</summary>
        public Alliance Alliance => alliance;
        
        /// <summary>
        /// Changes the robot's alliance and updates visual materials accordingly.
        /// </summary>
        /// <param name="newAlliance">The alliance to switch to.</param>
        public void SetAlliance(Alliance newAlliance)
        {
            alliance = newAlliance;
            ApplyAllianceVisuals();
        }
        
        public int TeamNumber { get; set; }
        
        public string PlayerPrefix { get; set; } = "";

        /// <summary>Whether commands are supplied by an external simulation client.</summary>
        public bool ExternalControlEnabled { get; private set; }

        /// <summary>The most recent external command, held until replaced.</summary>
        public ExternalRobotCommand ExternalCommand { get; private set; } = ExternalRobotCommand.Idle;

        /// <summary>Gets or sets whether the robot uses field-centric or robot-centric movement.</summary>
        public bool IsFieldCentric { get; set; } = true;
        
        /// <summary>Gets or sets the third-person view camera.</summary>
        public IRobotCamera ThirdPersonCam { get; set; }
        
        /// <summary>Gets or sets the first-person view camera.</summary>
        public IRobotCamera FirstPersonCam { get; set; }
        
        /// <summary>Gets or sets the driver station view camera.</summary>
        public IRobotCamera DriverStationCam { get; set; }

        /// <summary>Gets the currently active camera GameObject.</summary>
        public GameObject GetActiveCamera() =>
            ThirdPersonCam != null ? ThirdPersonCam.CameraObject :
            FirstPersonCam != null ? FirstPersonCam.CameraObject :
            DriverStationCam?.CameraObject;
        
        protected RobotGamePieceControllerBase RobotGamePieceControllerBase { get; private set; }
        protected DriveController DriveController { get; private set; }
        
        /// <summary>Gets or sets the game piece preloaded on this robot.</summary>
        public GameObject PreloadGamePiece { get; set; }
        
        private PlayerInput _playerInput;
        protected InputActionMap InputActionMap;

        /// <summary>Gets the translate/movement input action.</summary>
        public InputAction TranslateAction { get; private set; }
        
        /// <summary>Gets the rotate/turn input action.</summary>
        public InputAction RotateAction { get; private set; }
        
        /// <summary>Gets the stow/home input action.</summary>
        public InputAction StowAction { get; private set; }
        
        /// <summary>Gets the intake input action.</summary>
        public InputAction IntakeAction { get; private set; }
        
        /// <summary>Gets the outtake/place input action.</summary>
        public InputAction OuttakeAction { get; private set; }
        
        /// <summary>Gets the swap camera input action.</summary>
        public InputAction SwapCameraAction { get; private set; }
        
        /// <summary>Gets the driver station movement input action.</summary>
        public InputAction DriverStationMovementAction { get; private set; }
        
        /// <summary>Gets the right stick modifier input action.</summary>
        public InputAction RightStickModifierAction { get; private set; }
        
        /// <summary>Gets the restart/reset input action.</summary>
        public InputAction RestartAction { get; private set; }
        
        /// <summary>Gets the pause input action.</summary>
        public InputAction PauseAction { get; private set; }

        public void EnableExternalControl(bool enabled)
        {
            ExternalControlEnabled = enabled;
            ExternalCommand = ExternalRobotCommand.Idle;
            if (enabled)
            {
                IsFieldCentric = false;
            }
        }

        public void SetExternalCommand(ExternalRobotCommand command)
        {
            command.Translation = Vector2.ClampMagnitude(command.Translation, 1f);
            command.Rotation = Mathf.Clamp(command.Rotation, -1f, 1f);
            command.TargetSetpoint = Mathf.Clamp(command.TargetSetpoint, 0, 5);
            command.ManipulatorIntent = Mathf.Clamp(command.ManipulatorIntent, -1f, 1f);
            ExternalCommand = command;
        }

        public void ClearExternalPlacePulse()
        {
            if (!ExternalControlEnabled || !ExternalCommand.PlacePulse)
            {
                return;
            }

            var command = ExternalCommand;
            command.PlacePulse = false;
            ExternalCommand = command;
        }

        public Vector2 ReadTranslationInput() => ExternalControlEnabled
            ? ExternalCommand.Translation
            : TranslateAction.ReadValue<Vector2>();

        public float ReadRotationInput() => ExternalControlEnabled
            ? ExternalCommand.Rotation
            : RotateAction.ReadValue<float>();

        public bool IsIntakePressed() => ExternalControlEnabled
            ? ExternalCommand.IntakePressed
            : IntakeAction.IsPressed();

        public bool IsPlacePressed() => ExternalControlEnabled
            ? ExternalCommand.PlacePressed
            : OuttakeAction.IsPressed();

        [Header("Bumper Materials")]
        [SerializeField]
        [Tooltip("Array of MeshRenderer components for bumper coloring.")]
        private MeshRenderer[] bumperMeshes;
        
        [SerializeField]
        [Tooltip("Material to apply to bumpers when robot is on the blue alliance.")]
        private Material blueBumperMaterial;
        
        [SerializeField]
        [Tooltip("Material to apply to bumpers when robot is on the red alliance.")]
        private Material redBumperMaterial;

        /// <summary>Gets whether to enable controller rumble feedback.</summary>
        protected bool DoControllerRumble { get; private set; }
        
        private float _controllerRumbleStrength = 0.5f;

        private Gamepad _gamepad;
        private Coroutine _controllerRumbleRoutine;
        
        /// <summary>Event triggered when rumble feedback should be applied.</summary>
        protected Action OnRumbleTrigger { get; private set; }

        /// <summary>
        /// Initializes components and input actions at startup.
        /// </summary>
        protected virtual void Awake()
        {
            InitializeComponents();
            SetupInputActions();
        }

        /// <summary>
        /// Applies alliance visual colors and sets up controller rumble.
        /// </summary>
        protected virtual void Start()
        {
            ApplyAllianceVisuals();
            SetupControllerRumble();
        }
        
        /// <summary>
        /// Handles input polling for pause, restart, and camera swap actions.
        /// </summary>
        protected virtual void Update()
        {
            if (ExternalControlEnabled)
            {
                return;
            }

            if (PauseAction.triggered)
            {
                SceneManager.Instance.LoadScene("MainMenu", "CrossFade");
            }

            if (RestartAction.triggered && !BaseGameManager.Instance.IsResetting)
            {
                BaseGameManager.Instance.StartCoroutine(BaseGameManager.Instance.ResetMatch());
            }

            if (SwapCameraAction.triggered)
            {
                // Only flip for third person and first person cameras, not driver station
                var activeCamera = GetActiveCamera();
                if (activeCamera != null && activeCamera != DriverStationCam?.CameraObject)
                {
                    var baseVCam = activeCamera.GetComponent<BaseVCamScript>();
                    if (baseVCam != null)
                    {
                        baseVCam.FlipCamera();
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves and wires up core robot components (controllers, input, interpolation).
        /// </summary>
        private void InitializeComponents()
        {
            RobotGamePieceControllerBase = GetComponent<RobotGamePieceControllerBase>();
            if (RobotGamePieceControllerBase == null)
            {
                Debug.LogError("RobotGamePieceController component not found on Robot.");
            }
            
            DriveController = GetComponent<DriveController>();
            if (DriveController == null)
            {
                Debug.LogError("DriveController component not found on Robot.");
            }
            
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null)
            {
                Debug.LogError("PlayerInput component not found on Robot.");
            }
            else
            {
                _playerInput.actions.Enable();
                InputActionMap = _playerInput.currentActionMap;
                if (InputActionMap == null)
                    Debug.LogError("No current Action Map found in PlayerInput.");
            }
            
            if (RobotGamePieceControllerBase == null)
                Debug.LogError("RobotGamePieceController component not found on Robot.");
            if (DriveController == null)
                Debug.LogError("DriveController component not found on Robot.");

            Utils.TryGetAddComponent<CustomRigidbodyInterpolation>(gameObject);
            Utils.TryGetAddComponent<JointStackStabilizer>(gameObject);
        }
        
        /// <summary>
        /// Maps input actions from the PlayerInput component to public properties.
        /// </summary>
        private void SetupInputActions()
        {
            InputActionMap.Enable();
            
            TranslateAction = InputActionMap.FindAction("Translate");
            RotateAction = InputActionMap.FindAction("Rotate");
            StowAction = InputActionMap.FindAction("Stow");
            OuttakeAction = InputActionMap.FindAction("Place");
            IntakeAction = InputActionMap.FindAction("Intake");
            SwapCameraAction = InputActionMap.FindAction("SwapCamera");
            DriverStationMovementAction = InputActionMap.FindAction("DriverStationMovement");
            RightStickModifierAction = InputActionMap.FindAction("RightStickModifier");
            RestartAction = InputActionMap.FindAction("Restart");
            PauseAction = InputActionMap.FindAction("Pause");

            InputActionMap.Enable();
        }

        /// <summary>
        /// Initializes gamepad rumble based on player preferences.
        /// </summary>
        private void SetupControllerRumble()
        {
            _gamepad = _playerInput.devices.FirstOrDefault(device => device is Gamepad) as Gamepad;
            if (_gamepad == null)
            {
                Debug.LogWarning("No gamepad found for controller rumble.");
            }

            DoControllerRumble = !Mathf.Approximately(PlayerPrefs.GetFloat("ControllerRumble", 0.5f), 0f);
            _controllerRumbleStrength = PlayerPrefs.GetFloat("ControllerRumbleStrength", 0.5f);
            
            OnRumbleTrigger += () =>
            {
                if (_controllerRumbleRoutine != null)
                {
                    StopCoroutine(_controllerRumbleRoutine);
                }
                _controllerRumbleRoutine = StartCoroutine(ControllerRumbleRoutine(0.5f, _controllerRumbleStrength));
            };
        }

        /// <summary>
        /// Provides haptic feedback by vibrating the gamepad for a specified duration.
        /// </summary>
        /// <param name="duration">How long to vibrate in seconds.</param>
        /// <param name="strength">The intensity of vibration (0-1).</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        private IEnumerator ControllerRumbleRoutine(float duration, float strength)
        {
            if (_gamepad == null || !DoControllerRumble)
                yield break;

            _gamepad.SetMotorSpeeds(strength, strength);
            yield return new WaitForSeconds(duration);
            _gamepad.SetMotorSpeeds(0f, 0f);
        }

        /// <summary>
        /// Applies alliance-specific materials to the robot's bumpers.
        /// </summary>
        private void ApplyAllianceVisuals()
        {
            foreach (var mesh in bumperMeshes)
            {
                switch (alliance)
                {
                    case Alliance.Blue:
                        mesh.material = blueBumperMaterial;
                        break;
                    case Alliance.Red:
                        mesh.material = redBumperMaterial;
                        break;
                    default:
                        Debug.LogError("Unknown alliance type.");
                        break;
                }
            }
        }
        
        /// <summary>
        /// Cleans up controller rumble on robot destruction.
        /// </summary>
        private void OnDestroy()
        {
            if (_controllerRumbleRoutine != null)
            {
                StopCoroutine(_controllerRumbleRoutine);
                _controllerRumbleRoutine = null;
            }

            var gamepad = _gamepad ?? Gamepad.current;
            gamepad?.SetMotorSpeeds(0f, 0f);
        }
    }
}
