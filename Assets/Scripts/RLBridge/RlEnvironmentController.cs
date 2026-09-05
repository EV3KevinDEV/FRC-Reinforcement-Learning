using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Games.Reefscape.Enums;
using Games.Reefscape.Robots;
using Games.Reefscape.Scoring;
using GameSystems.Cameras;
using GameSystems.Management;
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
        private const double RealtimeControlWatchdogSeconds = 0.25;
        private const int MaxCameraJpegBytes = 700_000;
        private const int GamepadActionDimension = 25;
        private const int GamepadDpadDown = 5;
        private const int GamepadDpadLeft = 6;
        private const int GamepadDpadUp = 8;
        private const int GamepadEast = 9;
        private const int GamepadLeftShoulder = 11;
        private const int GamepadLeftThumb = 12;
        private const int GamepadNorth = 14;
        private const int GamepadRightShoulder = 18;
        private const int GamepadRightThumb = 19;
        private const int GamepadSouth = 22;
        private const int GamepadWest = 24;
        private readonly RlScenarioManager _scenario = new();
        private readonly float[] _previousGamepadAction = new float[GamepadActionDimension];
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
        private int _previousTargetSetpoint;
        private int _lastRequestId;
        private bool _environmentReady;
        private bool _activeRequestAppliedAction;
        private bool _realtimeControlActive;
        private string _realtimeControlSession;
        private long _realtimeControlSequence;
        private long _appliedRealtimeControlSequence;
        private double _lastRealtimeControlAt;
        private float[] _appliedRealtimeAction;
        private float[] _appliedRealtimeGamepadAction;
        private RlRequest _pendingSampleRequest;
        private RobotVirtualCamera[] _pendingSampleCameras;

        private void Awake()
        {
            _server = new RlTcpServer(RlRuntimeSettings.Host, RlRuntimeSettings.Port);
            _server.Start();
            gameObject.AddComponent<RlRealtimeInputPump>().Bridge = this;
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
                        camera_rendering_available = CameraRenderingAvailable,
                        realtime_control_api = RlRuntimeSettings.Realtime,
                        realtime_control_port = RlRuntimeSettings.Port
                    });
                    break;
                case "ping":
                    SendSuccess(request, new RlResponsePayload
                    {
                        worker_id = RlRuntimeSettings.WorkerId,
                        fixed_dt = RlRuntimeSettings.FixedDeltaTime,
                        control_dt = RlRuntimeSettings.ControlDeltaTime,
                        decision_dt = RlRuntimeSettings.FrameSkip * RlRuntimeSettings.ControlDeltaTime,
                        frame_skip = RlRuntimeSettings.FrameSkip,
                        realtime_control_api = RlRuntimeSettings.Realtime,
                        realtime_control_port = RlRuntimeSettings.Port
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
            if (_pendingSampleRequest != null)
            {
                var sampleRequest = _pendingSampleRequest;
                var sampleCameras = _pendingSampleCameras;
                _pendingSampleRequest = null;
                _pendingSampleCameras = null;
                _stepId++;
                var samplePayload = BuildStatePayload(false);
                if (sampleCameras.Length == 0)
                {
                    samplePayload.camera_frames = Array.Empty<RlCameraFrameDto>();
                    SendSuccess(sampleRequest, samplePayload);
                    _operationActive = false;
                }
                else
                {
                    BeginSynchronizedSample(sampleRequest, samplePayload, sampleCameras,
                        sampleRequest.payload.jpeg_quality, samplePayload.state.match.sim_time);
                }
            }

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
            if (_activeRequestAppliedAction)
            {
                _robot?.ClearExternalPlacePulse();
            }
            _activeRequestAppliedAction = false;
            SendSuccess(request, payload);
            _advanceMatch = false;
            _operationActive = false;
        }

        private IEnumerator ResetEnvironment(RlRequest request)
        {
            if (!TryParseCameraMode(request.payload.camera_mode, out var cameraMode))
            {
                SendError(request, "invalid_camera_mode");
                yield break;
            }

            if (!TryParseDriveMode(request.payload.drive_mode, out var fieldCentric))
            {
                SendError(request, "invalid_drive_mode");
                yield break;
            }

            _environmentReady = false;
            ResetRealtimeControlState();
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
            if (cameraMode.HasValue)
            {
                FindFirstObjectByType<RobotSpawnController>()?.ConfigureRlCamera(cameraMode.Value);
            }
            if (fieldCentric.HasValue)
            {
                _robot.IsFieldCentric = fieldCentric.Value;
            }
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
            ResetControlEdges();
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
            if (request.payload.gamepad_action != null &&
                request.payload.gamepad_action.Length != GamepadActionDimension)
            {
                SendError(request, "invalid_gamepad_action_shape");
                return;
            }
            if (FindRobot() == null)
            {
                SendError(request, "robot_not_ready");
                return;
            }

            if (request.payload.observe_only)
            {
                if (!RlRuntimeSettings.Realtime)
                {
                    SendError(request, "observe_only_requires_realtime");
                    return;
                }
                if (!TrySelectCameras(
                        request.payload.camera_names,
                        request.payload.jpeg_quality,
                        out var selectedCameras,
                        out var cameraError))
                {
                    SendError(request, cameraError);
                    return;
                }

                // Capture after gameplay Update/LateUpdate consumers, with no
                // intervening physics between the state and camera submissions.
                _operationActive = true;
                _pendingSampleRequest = request;
                _pendingSampleCameras = selectedCameras;
                return;
            }

            var command = BuildExternalCommand(
                request.payload.action,
                request.payload.gamepad_action);
            _robot.SetExternalCommand(command);
            _activeRequestAppliedAction = true;

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

        private ExternalRobotCommand BuildExternalCommand(float[] action, float[] gamepad)
        {
            var manipulatorIntent = Mathf.Clamp(action[4], -1f, 1f);
            var targetSetpoint = Mathf.Clamp(
                Mathf.RoundToInt((Mathf.Clamp(action[3], -1f, 1f) + 1f) * 2.5f),
                0,
                5);
            var hasGamepad = gamepad != null;
            var targetSelectionPulse = hasGamepad &&
                (targetSetpoint != _previousTargetSetpoint ||
                 GamepadRising(gamepad, GamepadDpadDown) ||
                 GamepadRising(gamepad, GamepadSouth) ||
                 GamepadRising(gamepad, GamepadEast) ||
                 GamepadRising(gamepad, GamepadWest) ||
                 GamepadRising(gamepad, GamepadNorth));
            var command = new ExternalRobotCommand
            {
                Translation = new Vector2(
                    Mathf.Clamp(action[0], -1f, 1f),
                    Mathf.Clamp(action[1], -1f, 1f)),
                Rotation = Mathf.Clamp(action[2], -1f, 1f),
                TargetSetpoint = targetSetpoint,
                ManipulatorIntent = manipulatorIntent,
                StationMode = action[5] > 0f,
                PlacePulse = ExternalRobotCommand.IsPlaceRisingEdge(
                    manipulatorIntent,
                    _previousManipulatorIntent),
                HasGamepadControls = hasGamepad,
                TargetSelectionPulse = targetSelectionPulse,
                RobotModeTogglePulse = GamepadRising(gamepad, GamepadDpadUp),
                IntakeModeTogglePulse = GamepadRising(gamepad, GamepadDpadLeft),
                ClimbPulse = GamepadRising(gamepad, GamepadLeftThumb),
                CameraFlipPulse = GamepadRising(gamepad, GamepadRightThumb),
                AutoAlignLeft = GamepadPressed(gamepad, GamepadLeftShoulder),
                AutoAlignRight = GamepadPressed(gamepad, GamepadRightShoulder),
                AutoAlignLeftPulse = GamepadRising(gamepad, GamepadLeftShoulder),
                AutoAlignRightPulse = GamepadRising(gamepad, GamepadRightShoulder)
            };
            _previousManipulatorIntent = manipulatorIntent;
            _previousTargetSetpoint = targetSetpoint;
            if (hasGamepad)
            {
                Array.Copy(gamepad, _previousGamepadAction, GamepadActionDimension);
            }
            else
            {
                Array.Clear(_previousGamepadAction, 0, _previousGamepadAction.Length);
            }
            return command;
        }

        private static bool GamepadPressed(float[] gamepad, int index) =>
            gamepad != null && index >= 0 && index < gamepad.Length && gamepad[index] > 0.5f;

        private bool GamepadRising(float[] gamepad, int index) =>
            GamepadPressed(gamepad, index) && _previousGamepadAction[index] <= 0.5f;

        public void ProcessRealtimeControls()
        {
            if (!RlRuntimeSettings.Realtime || _server == null)
            {
                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;

            while (_server.TryDequeueRealtimeControl(out var control))
            {
                if (control.sequence <= 0 || string.IsNullOrWhiteSpace(control.session))
                {
                    continue;
                }
                if (control.active &&
                    (control.action == null || control.action.Length != 6 ||
                     control.gamepad_action == null ||
                     control.gamepad_action.Length != GamepadActionDimension))
                {
                    continue;
                }

                if (!string.Equals(
                        control.session,
                        _realtimeControlSession,
                        StringComparison.Ordinal))
                {
                    _realtimeControlSession = control.session;
                    _realtimeControlSequence = 0;
                    ResetControlEdges();
                    _robot?.SetExternalCommand(ExternalRobotCommand.Idle);
                }
                if (control.sequence <= _realtimeControlSequence)
                {
                    continue;
                }

                _realtimeControlSequence = control.sequence;
                _lastRealtimeControlAt = now;
                if (!control.active)
                {
                    StopRealtimeControl();
                    continue;
                }

                _realtimeControlActive = true;
                if (!_environmentReady || FindRobot() == null)
                {
                    continue;
                }

                var nextCommand = BuildExternalCommand(
                    control.action,
                    control.gamepad_action);
                // Consume discrete events in packet order instead of OR-merging
                // taps (which loses toggle parity and target-selection history).
                // RT survives a following neutral packet until physics consumes it.
                nextCommand.PlacePulse |= _robot.ExternalCommand.PlacePulse;
                _robot.SetExternalCommand(nextCommand);
                _robot.ApplyExternalInputsNow();
                _appliedRealtimeAction = (float[])control.action.Clone();
                _appliedRealtimeGamepadAction = (float[])control.gamepad_action.Clone();
                _appliedRealtimeControlSequence = control.sequence;
            }

            if (_realtimeControlActive &&
                now - _lastRealtimeControlAt > RealtimeControlWatchdogSeconds)
            {
                StopRealtimeControl();
            }
        }

        private void StopRealtimeControl()
        {
            _realtimeControlActive = false;
            _appliedRealtimeAction = null;
            _appliedRealtimeGamepadAction = null;
            _appliedRealtimeControlSequence = 0;
            ResetControlEdges();
            _robot?.SetExternalCommand(ExternalRobotCommand.Idle);
        }

        private void ResetRealtimeControlState()
        {
            _realtimeControlActive = false;
            _realtimeControlSession = null;
            _realtimeControlSequence = 0;
            _appliedRealtimeControlSequence = 0;
            _lastRealtimeControlAt = 0d;
            _appliedRealtimeAction = null;
            _appliedRealtimeGamepadAction = null;
            ResetControlEdges();
        }

        private void ResetControlEdges()
        {
            _previousManipulatorIntent = 0f;
            _previousTargetSetpoint = 0;
            Array.Clear(_previousGamepadAction, 0, _previousGamepadAction.Length);
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
            var controlActive = _realtimeControlActive &&
                                _appliedRealtimeAction != null &&
                                _appliedRealtimeGamepadAction != null;
            return new RlResponsePayload
            {
                worker_id = RlRuntimeSettings.WorkerId,
                control = controlActive
                    ? new RlControlSnapshotDto
                    {
                        session = _realtimeControlSession,
                        sequence = _appliedRealtimeControlSequence,
                        sample_id = _stepId,
                        unity_frame = Time.frameCount,
                        sim_time = state.match.sim_time,
                        action = (float[])_appliedRealtimeAction.Clone(),
                        gamepad_action = (float[])_appliedRealtimeGamepadAction.Clone()
                    }
                    : null,
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
                    scenario = _scenario.Scenario,
                    realtime_control_active = controlActive,
                    realtime_control_sequence = controlActive
                        ? _appliedRealtimeControlSequence
                        : 0,
                    realtime_control_age_ms = controlActive
                        ? (float)Math.Max(
                            0d,
                            (Time.realtimeSinceStartupAsDouble - _lastRealtimeControlAt) * 1000d)
                        : -1f
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
                SendSuccess(request, new RlResponsePayload
                {
                    worker_id = RlRuntimeSettings.WorkerId,
                    virtual_camera_api = true,
                    camera_rendering_available = true,
                    camera_frame = CaptureCameraFrame(camera, request.payload.jpeg_quality)
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

        private static bool TryParseCameraMode(string value, out CameraMode? mode)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                mode = null;
                return true;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "robot":
                case "first-person":
                case "first_person":
                    mode = CameraMode.FirstPerson;
                    return true;
                case "third-person":
                case "third_person":
                case "field":
                    mode = CameraMode.ThirdPerson;
                    return true;
                case "driver-station":
                case "driver_station":
                    mode = CameraMode.DriverStation;
                    return true;
                default:
                    mode = null;
                    return false;
            }
        }

        private bool TrySelectCameras(
            string[] cameraNames,
            int jpegQuality,
            out RobotVirtualCamera[] cameras,
            out string error)
        {
            cameras = Array.Empty<RobotVirtualCamera>();
            error = null;
            if (cameraNames == null || cameraNames.Length == 0)
            {
                return true;
            }
            if (!CameraRenderingAvailable)
            {
                error = "camera_rendering_unavailable";
                return false;
            }
            if (jpegQuality is < 1 or > 95)
            {
                error = "invalid_jpeg_quality";
                return false;
            }
            if (cameraNames.Length > 8 ||
                cameraNames.Any(string.IsNullOrWhiteSpace) ||
                cameraNames.Distinct(StringComparer.Ordinal).Count() != cameraNames.Length)
            {
                error = "invalid_camera_names";
                return false;
            }

            var available = FindVirtualCameras();
            var selected = new List<RobotVirtualCamera>(cameraNames.Length);
            foreach (var cameraName in cameraNames)
            {
                var matches = available.Where(camera => string.Equals(
                    camera.CameraId,
                    cameraName,
                    StringComparison.Ordinal)).ToArray();
                if (matches.Length == 0)
                {
                    error = "camera_not_found";
                    return false;
                }
                if (matches.Length > 1)
                {
                    error = "camera_name_ambiguous";
                    return false;
                }
                selected.Add(matches[0]);
            }

            cameras = selected.ToArray();
            return true;
        }

        private void BeginSynchronizedSample(
            RlRequest request,
            RlResponsePayload payload,
            RobotVirtualCamera[] cameras,
            int jpegQuality,
            float sampleSimTime)
        {
            var frames = new RlCameraFrameDto[cameras.Length];
            var remaining = cameras.Length;
            string captureError = null;

            void CompleteOne()
            {
                remaining--;
                if (remaining > 0)
                {
                    return;
                }

                try
                {
                    if (captureError != null)
                    {
                        SendError(request, captureError);
                        return;
                    }
                    payload.camera_frames = frames;
                    try
                    {
                        SendSuccess(request, payload);
                    }
                    catch (InvalidDataException)
                    {
                        SendError(request, "camera_batch_too_large");
                    }
                }
                finally
                {
                    _operationActive = false;
                }
            }

            for (var index = 0; index < cameras.Length; index++)
            {
                var resultIndex = index;
                var camera = cameras[index];
                // Freeze metadata when rendering is submitted, not after the
                // asynchronous readback/encoder completes on a later frame.
                var frame = new RlCameraFrameDto
                {
                    name = camera.CameraId,
                    width = camera.ImageWidth,
                    height = camera.ImageHeight,
                    sim_time = sampleSimTime,
                    sample_id = payload.control?.sample_id ?? _stepId,
                    unity_frame = Time.frameCount,
                    control_sequence = payload.control?.sequence ?? 0
                };
                StartCoroutine(camera.CaptureJpegAsync(
                    jpegQuality,
                    jpeg =>
                    {
                        if (jpeg.Length > MaxCameraJpegBytes)
                        {
                            captureError ??= "camera_frame_too_large";
                        }
                        else
                        {
                            frame.image_base64 = Convert.ToBase64String(jpeg);
                            frame.sequence = camera.CaptureSequence;
                            frames[resultIndex] = frame;
                        }
                        CompleteOne();
                    },
                    exception =>
                    {
                        Debug.LogWarning(
                            $"Synchronized camera '{camera.CameraId}' capture failed: " +
                            exception.Message,
                            this);
                        captureError ??= "camera_capture_failed";
                        CompleteOne();
                    }));
            }
        }

        private RlCameraFrameDto CaptureCameraFrame(
            RobotVirtualCamera camera,
            int jpegQuality)
        {
            var jpeg = camera.CaptureJpeg(jpegQuality);
            if (jpeg == null || jpeg.Length == 0)
            {
                throw new InvalidDataException("camera_capture_failed");
            }
            if (jpeg.Length > MaxCameraJpegBytes)
            {
                throw new InvalidDataException("camera_frame_too_large");
            }

            return new RlCameraFrameDto
            {
                name = camera.CameraId,
                width = camera.ImageWidth,
                height = camera.ImageHeight,
                image_base64 = Convert.ToBase64String(jpeg),
                sequence = camera.CaptureSequence,
                sim_time = (float)_simTime
            };
        }

        private static bool TryParseDriveMode(string value, out bool? fieldCentric)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                fieldCentric = null;
                return true;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "field":
                case "field-oriented":
                case "field_oriented":
                    fieldCentric = true;
                    return true;
                case "robot":
                case "robot-oriented":
                case "robot_oriented":
                    fieldCentric = false;
                    return true;
                default:
                    fieldCentric = null;
                    return false;
            }
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
