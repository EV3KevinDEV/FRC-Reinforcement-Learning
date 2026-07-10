using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using UnityEngine;
using UnityEngine.Audio;

namespace RobotFramework.Controllers.Drivetrain
{
    public class DriveController : MonoBehaviour
    {
        // Core references
        private RobotBase _robotBase;
        private Rigidbody _rb;
        private Transform _wheelChild;
        
        // Configuration
        [SerializeField] private GameObject drivetrainParent;
        [SerializeField] private float wheelDiameter = 4f;
        [SerializeField] private SecureFloat maxSpeed = 17f;
        [SerializeField] private SecureFloat accelerationForce = 8f;
        [SerializeField] private float falloffPercent = 0.075f;
        [SerializeField] private int falloffExponent = 10;
        [SerializeField] private float steerMultiplier = 1f;
        [SerializeField] private float speedDebug;
        private float[] _falloffLookup; // Velocity falloff lookup table
        private const int LookupTableSize = 2048; // Higher = more precision
        private float _maxSpeedMeters; // Cache this conversion
        
        // Audio
        [SerializeField] private AudioMixerGroup swerveAudioMixerGroup;
        [SerializeField] private AudioResource gearAudioResource;
        [SerializeField] private AudioResource treadAudioResource;
        private AudioSource _gearSource;
        private AudioSource _treadSource;
        
        // State
        public bool IsTouchingGround { get; private set; }
        [HideInInspector] public float moveSpeedMultiplier;
        [HideInInspector] public float rotationSpeedMultiplier;
        
        private bool _drivetrainAssigned;
        private bool _drivetrainError;
        private bool _setupError;
        
        // Swerve data
        private readonly SwerveWheel[] _swerveWheels = new SwerveWheel[4];
        private readonly SwerveSetpoint[] _swerveSetpoints = new SwerveSetpoint[4];
        
        // Module indices
        private const int FL_MODULE = 0;
        private const int FR_MODULE = 1;
        private const int BL_MODULE = 2;
        private const int BR_MODULE = 3;
        
        // Constants
        private const float METERS_TO_FEET = 3.28084f;
        private const float FEET_TO_METERS = 0.3048f;
        private const float INCHES_TO_METERS = 0.0254f;
        private const float DEG_TO_RAD = Mathf.PI / 180f;
        private const float RAD_TO_DEG = 180f / Mathf.PI;
        private int FIELD_LAYER_MASK; // Assumes Field layer is 8

        private float length;
        private float width;
        private float radius;

        private bool overideActive;

        private float str;
        private float fwd;
        private float rotation;
        private float softSteer;
        private float driveMP;

        private void Start()
        {
            InitializeComponents();
            InitializeAudio();
            LoadPlayerPreferences();
            BuildFalloffLookupTable();
            
            FIELD_LAYER_MASK = 1 << LayerMask.NameToLayer("Robot");
            overideActive = false;

            driveMP = 1;
        }

        private void Update()
        {
            UpdateAudio();
        }

        private void FixedUpdate()
        {
            if (_drivetrainError) return;
            if (!_drivetrainAssigned && !RuntimeCheck()) return;
            
            RunSwerve();
        }

        private void InitializeComponents()
        {
            _robotBase = GetComponent<RobotBase>() ?? gameObject.AddComponent<RobotBase>();
            _rb = GetComponent<Rigidbody>();
            
            ValidateSetup();
        }

        private void InitializeAudio()
        {
            _treadSource = CreateAudioSource(treadAudioResource);
            _gearSource = CreateAudioSource(gearAudioResource);
        }

        private AudioSource CreateAudioSource(AudioResource resource)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = swerveAudioMixerGroup;
            source.spatialBlend = 0.4f;
            source.playOnAwake = false;
            source.resource = resource;
            source.loop = true;
            return source;
        }

        private void LoadPlayerPreferences()
        {
            moveSpeedMultiplier = Mathf.Clamp01(PlayerPrefs.GetFloat("MoveSpeed", 1f));
            rotationSpeedMultiplier = Mathf.Clamp01(PlayerPrefs.GetFloat("RotationSpeed", 1f));
        }

        private void ValidateSetup()
        {
            _setupError = false;

            if (drivetrainParent == null)
            {
                Debug.LogError("Please add a Drivetrain Parent");
                _setupError = true;
            }

            if (drivetrainParent != null && drivetrainParent.transform.Find("Wheels") != null)
            {
                _wheelChild = drivetrainParent.transform.Find("Wheels");
            }
            else
            {
                Debug.LogError("Please add a Wheels Child to the Drivetrain Parent");
                _setupError = true;
            }

            if (_rb == null)
            {
                Debug.LogError($"Rigidbody not found on {gameObject.name}. Adding temporary Rigidbody");
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.mass = 20f;
                _rb.drag = 3f;
                _rb.angularDrag = 3f;
                _setupError = true;
            }
        }

        private bool RuntimeCheck()
        {
            if (_setupError || BaseGameManager.Instance.RobotState != RobotState.Enabled)
                return false;

            if (!_drivetrainAssigned)
            {
                _drivetrainError = !AssignSwerveWheels();
                _drivetrainAssigned = true;
            }

            return !_drivetrainError;
        }

        private bool AssignSwerveWheels()
        {
            var wheelNames = new[] { "FL", "FR", "BL", "BR" };
            var indices = new[] { FL_MODULE, FR_MODULE, BL_MODULE, BR_MODULE };

            for (int i = 0; i < wheelNames.Length; i++)
            {
                var wheelTransform = _wheelChild.Find(wheelNames[i]);
                if (wheelTransform == null)
                {
                    Debug.LogError($"No {wheelNames[i]} Wheel Object Found");
                    return false;
                }
                _swerveWheels[indices[i]] = wheelTransform.GetComponent<SwerveWheel>();
            }
            
            length = Mathf.Abs(_swerveWheels[FL_MODULE].transform.localPosition.z - 
                                   _swerveWheels[BL_MODULE].transform.localPosition.z);
            width = Mathf.Abs(_swerveWheels[FL_MODULE].transform.localPosition.x - 
                                  _swerveWheels[FR_MODULE].transform.localPosition.x);
            radius = Mathf.Sqrt(length * length + width * width);

            return true;
        }

        private void RunSwerve()
        {
            _rb.maxLinearVelocity = maxSpeed * FEET_TO_METERS;

            if (BaseGameManager.Instance.RobotState != RobotState.Enabled)
            {
                fwd = 0;
                str = 0;
                rotation = 0;
            }
            else if (_robotBase.ExternalControlEnabled)
            {
                var translation = _robotBase.ReadTranslationInput();
                fwd = translation.x;
                str = translation.y;
                rotation = _robotBase.ReadRotationInput() * steerMultiplier * rotationSpeedMultiplier;
            }
            else if (overideActive)
            {
                overideActive = false;
            }
            else 
            {
                if (_robotBase.IsFieldCentric)
                {
                    var translationValue = _robotBase.TranslateAction.ReadValue<Vector2>();
                    GetFieldRelativeInput(translationValue);
                }
                else
                {
                    GetRobotRelativeInput();
                }

                rotation = _robotBase.RotateAction.ReadValue<float>() * steerMultiplier * rotationSpeedMultiplier;
            }

            var mag = new Vector2(fwd * driveMP, str * driveMP);
            if (mag.magnitude > 1)
            {
                mag = mag.normalized;
            }
            GenerateSwerveSetpoints(Mathf.Clamp(mag.x, -1, 1), Mathf.Clamp(mag.y, -1, 1), Mathf.Clamp(-rotation + softSteer, -1, 1));
            softSteer = 0;
            
            IsTouchingGround = false;
            RunSwerveModuleSphere(FL_MODULE);
            RunSwerveModuleSphere(FR_MODULE);
            RunSwerveModuleSphere(BL_MODULE);
            RunSwerveModuleSphere(BR_MODULE);
            
            speedDebug = _rb.velocity.magnitude * METERS_TO_FEET;
        }

        public void overideInput(Vector2 input, float rotation, DriveMode mode)
        {
            overideActive = true;

            if (mode == DriveMode.RobotRelative)
            {
                fwd = input.x;
                str = input.y;
            }
            else
            {
                Vector3 driveInput = new Vector3(input.x, 0, input.y);
                var fieldRelative = Quaternion.AngleAxis(Mathf.Repeat(-transform.localRotation.eulerAngles.y, 360), Vector3.up) * driveInput;
            
                fwd = fieldRelative.z;
                str = fieldRelative.x;
            }

            this.rotation = rotation;
        }

        public void SetDriveMp(float value)
        {
            driveMP = value;
        }

        public void SoftSteer(float input)
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Enabled)
            {
                softSteer = input;
            }
        }

        private void GetFieldRelativeInput(Vector2 translationValue)
        {
            var driveInput = new Vector3(translationValue.y, 0f, translationValue.x);

            // Calculate field-relative angle based on camera
            var angle = transform.localRotation.eulerAngles.y;
            if (_robotBase.ThirdPersonCam != null)
            {
                angle -= _robotBase.ThirdPersonCam.CameraObject.transform.eulerAngles.y;
            }
            else if (_robotBase.DriverStationCam != null)
            {
                angle += _robotBase.Alliance == Alliance.Blue ? 90f : -90f;
            }

            if (_robotBase.ThirdPersonCam is not null)
            {
                angle = transform.localRotation.eulerAngles.y +
                       -_robotBase.ThirdPersonCam.CameraObject.transform.eulerAngles.y;
            } else if (_robotBase.DriverStationCam is not null)
            {
                angle = transform.localRotation.eulerAngles.y +
                        (_robotBase.Alliance == Alliance.Blue ? 90 : -90);
            }

            var fieldRelative = Quaternion.AngleAxis(angle, Vector3.up) * driveInput;
            
            fwd = fieldRelative.x;
            str = fieldRelative.z;
        }

        private void GetRobotRelativeInput()
        {
            var translationValue = _robotBase.TranslateAction.ReadValue<Vector2>();
            var driveInput = new Vector3(translationValue.y, 0f, translationValue.x) * moveSpeedMultiplier;

            fwd = driveInput.x; 
            str = driveInput.z;
        }

        private void RunSwerveModuleSphere(int moduleIndex)
        {
            var module = _swerveWheels[moduleIndex];
            var wheelRadius = (wheelDiameter / 2f) * INCHES_TO_METERS;

            // Single spherecast can replace your 3 raycasts
            if (Physics.SphereCast(
                    module.transform.position,
                    wheelRadius * 0.3f, // Small radius for some tolerance
                    -module.transform.up,
                    out RaycastHit hit,
                    wheelRadius * 1.1f,
                    ~FIELD_LAYER_MASK))
            {
                applyWheelForceAtContact(moduleIndex, hit);
                IsTouchingGround = true;
            }
        }
        
        private void BuildFalloffLookupTable()
        {
            _maxSpeedMeters = maxSpeed * FEET_TO_METERS;
            _falloffLookup = new float[LookupTableSize];
    
            for (int i = 0; i < LookupTableSize; i++)
            {
                float speedRatio = (float)i / (LookupTableSize - 1);
                _falloffLookup[i] = Mathf.Pow(1f - speedRatio * falloffPercent, falloffExponent);
            }
        } 
        
        private float GetFalloffFromLookup(float speedRatio)
        {
            // Clamp and convert to lookup index
            int index = Mathf.Clamp(
                Mathf.RoundToInt(speedRatio * (LookupTableSize - 1)), 
                0, 
                LookupTableSize - 1
            );
            return _falloffLookup[index];
        }

        private void applyWheelForceAtContact(int moduleIndex, RaycastHit hit)
        {
            var module = _swerveWheels[moduleIndex];
            var setpoint = _swerveSetpoints[moduleIndex];
    
            // Get ground speed
            var realGroundSpeed = module.transform.InverseTransformVector(
                _rb.GetPointVelocity(module.transform.position)).z;
    
            // Use lookup table instead of calculating falloff
            var speedRatio = Mathf.Clamp01(Mathf.Abs(realGroundSpeed) / _maxSpeedMeters);
            var falloff = GetFalloffFromLookup(speedRatio);
    
            // Calculate force (no Pow call needed!)
            var forceMag = accelerationForce * FEET_TO_METERS * setpoint.Velocity * falloff;

            module.transform.localEulerAngles = new Vector3(0f, setpoint.Angle, 0f);
    
            Vector3 propulsionForce = module.transform.forward * forceMag;
            _rb.AddForceAtPosition(propulsionForce, hit.point, ForceMode.Impulse); 

            module.wheelAngle = module.transform.localRotation.eulerAngles.y;
            IsTouchingGround = true;
        }

        private void GenerateSwerveSetpoints(float fwd, float str, float rotation)
        {
            // Calculate wheelbase dimensions
            

            // Calculate wheel vectors
            var a = str - rotation * (length / radius);
            var b = str + rotation * (length / radius);
            var c = fwd - rotation * (width / radius);
            var d = fwd + rotation * (width / radius);

            // Calculate speeds and angles for each module
            CalculateModuleSetpoint(FR_MODULE, b, c);
            CalculateModuleSetpoint(FL_MODULE, b, d);
            CalculateModuleSetpoint(BL_MODULE, a, d);
            CalculateModuleSetpoint(BR_MODULE, a, c);
        }

        private void CalculateModuleSetpoint(int moduleIndex, float x, float y)
        {
            var speed = Mathf.Sqrt(x * x + y * y);
            _swerveSetpoints[moduleIndex].Velocity = speed;
            
            // Only update angle if there's movement
            if (speed > 0f)
            {
                _swerveSetpoints[moduleIndex].Angle = Mathf.Atan2(x, y) * RAD_TO_DEG;
            }
        }

        private void UpdateAudio()
        {
            moveSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
            rotationSpeedMultiplier = Mathf.Clamp01(rotationSpeedMultiplier);

            if (_rb.velocity.magnitude > 0f || Mathf.Abs(_rb.angularVelocity.magnitude) > 0f)
            {
                PlaySwerveSounds();
            }
            else
            {
                StopSwerveSounds();
            }
        }

        private void PlaySwerveSounds()
        {
            var velocityFactor = Mathf.Clamp01(_rb.velocity.magnitude / _rb.maxLinearVelocity);
            var rotationFactor = Mathf.Clamp01(Mathf.Abs(_rb.angularVelocity.magnitude) / 6);
            var accelerationFactor = Mathf.Clamp(1f + velocityFactor, 1f, 2f);

            var volume = velocityFactor + rotationFactor * 0.25f;
            var pitch = Mathf.Max(accelerationFactor, rotationFactor);

            _treadSource.volume = volume * 0.5f;
            _treadSource.pitch = pitch * 0.7f;
            _gearSource.volume = volume * 0.2f;

            if (!_treadSource.isPlaying)
            {
                _treadSource.Play();
                _gearSource.Play();
            }
        }

        private void StopSwerveSounds()
        {
            if (_treadSource.isPlaying)
            {
                _treadSource.Stop();
                _gearSource.Stop();
            }
        }

        private struct SwerveSetpoint
        {
            public float Angle;
            public float Velocity;
        }

        public enum DriveMode
        {
            FieldOriented,
            RobotRelative,
        }
    }
}
