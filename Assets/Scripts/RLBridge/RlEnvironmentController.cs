using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Games.Reefscape.Enums;
using Games.Reefscape.Robots;
using Games.Reefscape.Scoring;
using MoSimCore;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoSimRL
{
    [DefaultExecutionOrder(10000)]
    public sealed class RlEnvironmentController : MonoBehaviour
    {
        private const double FinalScoringGraceSeconds = 3.0;
        private const int MaxCameraJpegBytes = 700_000;
        private readonly RlScenarioManager _scenario = new();
        private RlTcpServer _server;
        private RlRequest _activeRequest;
        private Robonauts _robot;
        private bool _operationActive;
        private bool _advanceMatch;
        private bool _captureStep;
        private double _stepSecondsRemaining;
        private double _stepTimingCarry;
        private double _endGraceSeconds;
        private int _stepId;
        private double _simTime;
        private int _previousTotalScore;
        private int _previousCoralScore;
        private float _previousManipulatorIntent;
        private int _lastRequestId;
        private bool _environmentReady;

        private void Awake()
        {
            _server = new RlTcpServer(RlRuntimeSettings.Host, RlRuntimeSettings.Port);
            _server.Start();
            AudioListener.volume = 0f;
        }

        private void Update()
        {
            if (_operationActive || !_server.TryDequeue(out var request))
            {
                return;
            }

            if (request.id <= _lastRequestId)
            {
                SendError(request, "stale_request_id");
                return;
            }
            _lastRequestId = request.id;

            if (request.v != RlRuntimeSettings.ProtocolVersion)
            {
                SendError(request, "protocol_version_mismatch");
                return;
            }

            switch (request.cmd)
            {
                case "hello":
                    SendSuccess(request, new RlResponsePayload
                    {
                        simulator = "MoSimulator Reefscape RL",
                        simulator_version = "v26.2.0",
                        worker_id = RlRuntimeSettings.WorkerId,
                        team_number = RlRuntimeSettings.TeamNumber,
                        action_dim = RlRuntimeSettings.ActionDimension,
                        observation_dim = RlRuntimeSettings.ObservationDimension,
                        fixed_dt = RlRuntimeSettings.FixedDeltaTime,
                        control_dt = RlRuntimeSettings.ControlDeltaTime,
                        decision_dt = RlRuntimeSettings.FrameSkip * RlRuntimeSettings.ControlDeltaTime,
                        frame_skip = RlRuntimeSettings.FrameSkip,
                        virtual_camera_api = true,
                        camera_rendering_available = CameraRenderingAvailable
                    });
                    break;
                case "ping":
                    SendSuccess(request, new RlResponsePayload
                    {
                        worker_id = RlRuntimeSettings.WorkerId,
                        fixed_dt = RlRuntimeSettings.FixedDeltaTime,
                        control_dt = RlRuntimeSettings.ControlDeltaTime,
                        decision_dt = RlRuntimeSettings.FrameSkip * RlRuntimeSettings.ControlDeltaTime,
                        frame_skip = RlRuntimeSettings.FrameSkip
                    });
                    break;
                case "reset":
                    StartCoroutine(ResetEnvironment(request));
                    break;
                case "step":
                    BeginStep(request);
                    break;
                case "list_cameras":
                    ListVirtualCameras(request);
                    break;
                case "get_camera_frame":
                    GetVirtualCameraFrame(request);
                    break;
                case "close":
                    SendSuccess(request, new RlResponsePayload
                    {
                        worker_id = RlRuntimeSettings.WorkerId
                    });
                    StartCoroutine(QuitAfterResponse());
                    break;
                default:
                    SendError(request, "unknown_command");
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (RlRuntimeSettings.Realtime && _environmentReady)
            {
                AdvanceMatchClock();
            }

            if (!_operationActive || _stepSecondsRemaining <= 0d)
            {
                return;
            }

            if (!RlRuntimeSettings.Realtime && _advanceMatch)
            {
                AdvanceMatchClock();
            }

            _stepSecondsRemaining -= RlRuntimeSettings.FixedDeltaTime;
            if (_stepSecondsRemaining <= 0d)
            {
                // Carry sub-tick overshoot into the next decision. At MoSimulator's
                // native ~4.5 ms timestep this alternates the required physics-step
                // count while preserving an exact long-run 100 ms policy interval.
                _stepTimingCarry = Math.Max(
                    -RlRuntimeSettings.FixedDeltaTime,
                    _stepSecondsRemaining);
                _captureStep = true;
                if (!RlRuntimeSettings.Realtime)
                {
                    Time.timeScale = 0f;
                }
            }
        }

        private void AdvanceMatchClock()
        {
            if (BaseGameManager.Instance == null)
            {
                return;
            }
            BaseGameManager.Instance.AdvanceRlTimer(RlRuntimeSettings.FixedDeltaTime);
            _simTime += RlRuntimeSettings.FixedDeltaTime;
            if (BaseGameManager.Instance.GameState == GameState.End)
            {
                _endGraceSeconds += RlRuntimeSettings.FixedDeltaTime;
            }
        }

        private void LateUpdate()
        {
            if (!_captureStep || _activeRequest == null)
            {
                return;
            }

            _captureStep = false;
            var request = _activeRequest;
            _activeRequest = null;
            var payload = BuildStatePayload(false);
            // BeginStep runs late in Update. Keep a place edge alive through the
            // full decision so ReefscapeRobotBase.Update and Robonauts.FixedUpdate
            // are guaranteed to observe it before clearing it here.
            _robot?.ClearExternalPlacePulse();
            SendSuccess(request, payload);
            _advanceMatch = false;
            _operationActive = false;
        }

        private IEnumerator ResetEnvironment(RlRequest request)
        {
            _environmentReady = false;
            _operationActive = true;
            _advanceMatch = false;
            _activeRequest = request;
            // A rendered client must rebuild and settle the scene at the same
            // rate as normal MoSimulator. Accelerating reset was visibly injecting
            // energy into articulated robot joints and the suspended deep cages.
            Time.timeScale = RlRuntimeSettings.Realtime
                ? 1f
                : RlRuntimeSettings.ActiveTimeScale;

            var seed = request.payload.seed;
            UnityEngine.Random.InitState(seed);
            RlRuntimeSettings.FrameSkip = Mathf.Clamp(request.payload.frame_skip, 1, 50);

            yield return new WaitUntil(() => BaseGameManager.Instance != null && FindRobot() != null &&
                                             !BaseGameManager.Instance.IsResetting);
            yield return BaseGameManager.Instance.StartCoroutine(BaseGameManager.Instance.ResetMatch());
            yield return new WaitUntil(() => FindRobot() != null && !BaseGameManager.Instance.IsResetting);

            _robot.EnableExternalControl(true);
            _robot.SetExternalCommand(ExternalRobotCommand.Idle);
            if (!RlRuntimeSettings.Graphical)
            {
                DisablePresentationObjects();
            }

            // Scene reconstruction consumes Unity's global random stream. Reset it
            // again so curriculum target selection depends only on the episode seed.
            UnityEngine.Random.InitState(seed);
            _scenario.Configure(
                Mathf.Clamp(request.payload.curriculum_stage, 0, 4),
                request.payload.scenario,
                _robot);

            _stepId = 0;
            _simTime = 0d;
            _stepTimingCarry = 0d;
            _endGraceSeconds = 0d;
            _previousManipulatorIntent = 0f;
            var score = CurrentScore();
            _previousTotalScore = score.TotalPoints;
            _previousCoralScore = score.CoralPoints + score.TroughPoints + score.LeavePoints;
            _environmentReady = true;
            Time.timeScale = RlRuntimeSettings.Realtime ? 1f : 0f;

            var payload = BuildStatePayload(true);
            SendSuccess(request, payload);
            _activeRequest = null;
            _operationActive = false;
        }

        private void BeginStep(RlRequest request)
        {
            if (request.payload.action == null || request.payload.action.Length != 6)
            {
                SendError(request, "invalid_action_shape");
                return;
            }
            if (FindRobot() == null)
            {
                SendError(request, "robot_not_ready");
                return;
            }

            var action = request.payload.action;
            var manipulatorIntent = Mathf.Clamp(action[4], -1f, 1f);
            var command = new ExternalRobotCommand
            {
                Translation = new Vector2(
                    Mathf.Clamp(action[0], -1f, 1f),
                    Mathf.Clamp(action[1], -1f, 1f)),
                Rotation = Mathf.Clamp(action[2], -1f, 1f),
                TargetSetpoint = Mathf.Clamp(
                    Mathf.RoundToInt((Mathf.Clamp(action[3], -1f, 1f) + 1f) * 2.5f),
                    0,
                    5),
                ManipulatorIntent = manipulatorIntent,
                StationMode = action[5] > 0f,
                PlacePulse = ExternalRobotCommand.IsPlaceRisingEdge(
                    manipulatorIntent,
                    _previousManipulatorIntent)
            };
            _previousManipulatorIntent = manipulatorIntent;
            _robot.SetExternalCommand(command);

            _activeRequest = request;
            var controlTicks = Mathf.Clamp(request.payload.frame_skip, 1, 50);
            RlRuntimeSettings.FrameSkip = controlTicks;
            _stepSecondsRemaining = controlTicks * RlRuntimeSettings.ControlDeltaTime +
                                    _stepTimingCarry;
            _operationActive = true;
            _advanceMatch = true;
            _stepId++;
            Time.timeScale = RlRuntimeSettings.Realtime
                ? 1f
                : RlRuntimeSettings.ActiveTimeScale;
        }

        private RlResponsePayload BuildStatePayload(bool reset)
        {
            FindRobot();
            var snapshot = CurrentScore();
            var totalDelta = reset ? 0 : snapshot.TotalPoints - _previousTotalScore;
            var coralScore = snapshot.CoralPoints + snapshot.TroughPoints + snapshot.LeavePoints;
            var cycleSuccess = !reset && coralScore > _previousCoralScore;
            if (cycleSuccess)
            {
                _scenario.OnCycleSucceeded(_robot);
            }

            var state = new RlStateDto();
            PopulateRobotState(state);
            PopulatePhysicsDiagnostics(state);
            state.task = _scenario.Capture(_robot);
            state.match = new RlMatchDto
            {
                time_remaining = BaseGameManager.Instance != null ? BaseGameManager.Instance.Timer : 150f,
                game_state = BaseGameManager.Instance != null
                    ? BaseGameManager.Instance.GameState.ToString()
                    : GameState.Auto.ToString(),
                sim_time = (float)_simTime,
                score = ToDto(snapshot),
                score_delta = totalDelta,
                match_complete = _endGraceSeconds >= FinalScoringGraceSeconds
            };

            _previousTotalScore = snapshot.TotalPoints;
            _previousCoralScore = coralScore;
            return new RlResponsePayload
            {
                worker_id = RlRuntimeSettings.WorkerId,
                state = state,
                events = new RlEventsDto
                {
                    cycle_success = cycleSuccess,
                    cycle_failed = false,
                    match_complete = state.match.match_complete
                },
                info = new RlInfoDto
                {
                    worker_id = RlRuntimeSettings.WorkerId,
                    step_id = _stepId,
                    curriculum_stage = _scenario.Stage,
                    scenario = _scenario.Scenario
                }
            };
        }

        private void ListVirtualCameras(RlRequest request)
        {
            if (FindRobot() == null)
            {
                SendError(request, "robot_not_ready");
                return;
            }

            var cameras = FindVirtualCameras();
            SendSuccess(request, new RlResponsePayload
            {
                worker_id = RlRuntimeSettings.WorkerId,
                virtual_camera_api = true,
                camera_rendering_available = CameraRenderingAvailable,
                cameras = cameras.Select(camera => camera.BuildInfo(_robot.transform)).ToArray()
            });
        }

        private void GetVirtualCameraFrame(RlRequest request)
        {
            if (FindRobot() == null)
            {
                SendError(request, "robot_not_ready");
                return;
            }
            if (!CameraRenderingAvailable)
            {
                SendError(request, "camera_rendering_unavailable");
                return;
            }
            if (string.IsNullOrWhiteSpace(request.payload.camera_name))
            {
                SendError(request, "camera_name_required");
                return;
            }
            if (request.payload.jpeg_quality is < 1 or > 95)
            {
                SendError(request, "invalid_jpeg_quality");
                return;
            }

            var matches = FindVirtualCameras()
                .Where(camera => string.Equals(
                    camera.CameraId,
                    request.payload.camera_name,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                SendError(request, "camera_not_found");
                return;
            }
            if (matches.Length > 1)
            {
                SendError(request, "camera_name_ambiguous");
                return;
            }

            try
            {
                var camera = matches[0];
                var jpeg = camera.CaptureJpeg(request.payload.jpeg_quality);
                if (jpeg == null || jpeg.Length == 0)
                {
                    SendError(request, "camera_capture_failed");
                    return;
                }
                if (jpeg.Length > MaxCameraJpegBytes)
                {
                    SendError(request, "camera_frame_too_large");
                    return;
                }

                SendSuccess(request, new RlResponsePayload
                {
                    worker_id = RlRuntimeSettings.WorkerId,
                    virtual_camera_api = true,
                    camera_rendering_available = true,
                    camera_frame = new RlCameraFrameDto
                    {
                        name = camera.CameraId,
                        width = camera.ImageWidth,
                        height = camera.ImageHeight,
                        image_base64 = Convert.ToBase64String(jpeg),
                        sequence = camera.CaptureSequence,
                        sim_time = (float)_simTime
                    }
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Virtual camera '{request.payload.camera_name}' capture failed: {exception.Message}",
                    this);
                SendError(request, "camera_capture_failed");
            }
        }

        private RobotVirtualCamera[] FindVirtualCameras()
        {
            return _robot.GetComponentsInChildren<RobotVirtualCamera>(true)
                .OrderBy(camera => camera.CameraId, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool CameraRenderingAvailable =>
            RlRuntimeSettings.Graphical &&
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        private void PopulateRobotState(RlStateDto state)
        {
            if (_robot == null)
            {
                return;
            }
            var body = _robot.GetComponent<Rigidbody>();
            var localVelocity = body != null
                ? _robot.transform.InverseTransformDirection(body.velocity)
                : Vector3.zero;
            var drive = _robot.GetComponent<DriveController>();
            state.robot = new RlRobotDto
            {
                position = Vector(_robot.transform.position),
                yaw_degrees = _robot.transform.eulerAngles.y,
                local_velocity = Vector(localVelocity),
                yaw_rate = body != null ? body.angularVelocity.y : 0f,
                up = Vector(_robot.transform.up),
                grounded = drive != null && drive.IsTouchingGround,
                enabled = BaseGameManager.Instance != null &&
                          BaseGameManager.Instance.RobotState == RobotState.Enabled
            };
            state.mechanism = new RlMechanismDto
            {
                setpoint = SetpointIndex(_robot.CurrentSetpoint),
                arm_angle = _robot.RlArmAngle,
                elevator_height = _robot.RlElevatorHeight,
                intake_angle = _robot.RlIntakeAngle,
                algae_arms_angle = _robot.RlAlgaeArmsAngle,
                has_coral = _robot.HasCoral,
                coral_state = _robot.CoralState,
                station_mode = _robot.RlStationMode
            };
        }

        private static void PopulatePhysicsDiagnostics(RlStateDto state)
        {
            var seenBodies = new HashSet<int>();
            GameObject[] cageObjects;
            try
            {
                cageObjects = GameObject.FindGameObjectsWithTag("Cage");
            }
            catch (UnityException)
            {
                return;
            }

            foreach (var cageObject in cageObjects)
            {
                if (cageObject == null)
                {
                    continue;
                }
                var body = cageObject.GetComponent<Rigidbody>() ??
                           cageObject.GetComponentInParent<Rigidbody>();
                if (body == null || !seenBodies.Add(body.GetInstanceID()))
                {
                    continue;
                }
                state.physics.max_cage_linear_speed = Mathf.Max(
                    state.physics.max_cage_linear_speed,
                    body.velocity.magnitude);
                state.physics.max_cage_angular_speed = Mathf.Max(
                    state.physics.max_cage_angular_speed,
                    body.angularVelocity.magnitude);
            }
        }

        private ReefscapeScoreSnapshot CurrentScore()
        {
            var handler = FindFirstObjectByType<ReefscapeScoreHandler>();
            return handler != null
                ? handler.GetSnapshot(Alliance.Blue, false)
                : new ReefscapeScoreSnapshot(new ReefscapeScoreData());
        }

        private Robonauts FindRobot()
        {
            if (_robot != null)
            {
                return _robot;
            }
            var robots = FindObjectsByType<Robonauts>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (var robot in robots)
            {
                if (robot.TeamNumber == RlRuntimeSettings.TeamNumber && robot.Alliance == Alliance.Blue)
                {
                    _robot = robot;
                    break;
                }
            }
            return _robot;
        }

        private static int SetpointIndex(ReefscapeSetpoints setpoint) => setpoint switch
        {
            ReefscapeSetpoints.Stow => 0,
            ReefscapeSetpoints.Intake => 1,
            ReefscapeSetpoints.L1 => 2,
            ReefscapeSetpoints.L2 => 3,
            ReefscapeSetpoints.L3 => 4,
            ReefscapeSetpoints.L4 => 5,
            _ => 0
        };

        private static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };

        private static RlScoreDto ToDto(ReefscapeScoreSnapshot snapshot) => new()
        {
            coral_points = snapshot.CoralPoints,
            trough_points = snapshot.TroughPoints,
            net_points = snapshot.NetPoints,
            processor_points = snapshot.ProcessorPoints,
            climb_points = snapshot.ClimbPoints,
            park_points = snapshot.ParkPoints,
            leave_points = snapshot.LeavePoints,
            coral_scored = snapshot.CoralScored,
            algae_scored = snapshot.AlgaeScored,
            total_points = snapshot.TotalPoints
        };

        private static void DisablePresentationObjects()
        {
            foreach (var camera in FindObjectsByType<Camera>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                camera.enabled = false;
            }
            foreach (var listener in FindObjectsByType<AudioListener>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                listener.enabled = false;
            }
        }

        private void SendSuccess(RlRequest request, RlResponsePayload payload)
        {
            _server.Send(new RlResponse
            {
                id = request.id,
                ok = true,
                error = null,
                payload = payload
            });
        }

        private void SendError(RlRequest request, string error)
        {
            _server.Send(new RlResponse
            {
                id = request.id,
                ok = false,
                error = error,
                payload = new RlResponsePayload { worker_id = RlRuntimeSettings.WorkerId }
            });
        }

        private static IEnumerator QuitAfterResponse()
        {
            yield return null;
            Application.Quit(0);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            _server?.Dispose();
        }
    }
}
