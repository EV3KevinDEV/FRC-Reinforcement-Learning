using System;
using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Games.Reefscape.Robots
{
    public class ReefscapeRobotBase : RobotBase
    {
        [field: NonSerialized]
        protected new ReefscapeRobotGamePieceController RobotGamePieceController { get; private set; }

        public InputAction L4Action { get; private set; }
        public InputAction L3Action { get; private set; }
        public InputAction L2Action { get; private set; }
        public InputAction L1Action { get; private set; }
        public InputAction ClimbAction { get; private set; }

        public InputAction AutoAlignLeftAction { get; private set; }
        public InputAction AutoAlignRightAction { get; private set; }

        public InputAction RobotModeToggleAction { get; private set; }
        public InputAction IntakeModeToggleAction { get; private set; }
        public InputAction RobotSpecialAction { get; private set; }

        [field: SerializeField] public ReefscapeSetpoints CurrentSetpoint { get; protected set; }
        protected ReefscapeSetpoints LastSetpoint { get; private set; }
        public bool IsIntaking => IsIntakePressed();

        public bool HasCoral => RobotGamePieceController != null &&
                                RobotGamePieceController.GetPieceByName("Coral")?.currentStateNum > 0;

        public int CoralState => RobotGamePieceController != null
            ? RobotGamePieceController.GetPieceByName("Coral")?.currentStateNum ?? 0
            : 0;

        public bool AddRlCoralPreload() => RobotGamePieceController != null &&
                                           RobotGamePieceController.AddPreload();

        /// <summary>
        /// Optional deterministic fixture used by the RL integration test. Robot
        /// implementations place a real released coral in their normal ground
        /// intake; acquisition still goes through the production intake system.
        /// </summary>
        public virtual bool PrepareRlGroundPickupTest() => false;

        /// <summary>Remove the normal preload for manual empty-robot testing.</summary>
        public virtual bool PrepareRlEmptyStart() => false;

        public bool ExternalStationMode => ExternalControlEnabled && ExternalCommand.StationMode;

        public bool IsAutoAlignLeftPressed() => ExternalControlEnabled
            ? ExternalCommand.AutoAlignLeft
            : AutoAlignLeftAction.IsPressed();

        public bool IsAutoAlignRightPressed() => ExternalControlEnabled
            ? ExternalCommand.AutoAlignRight
            : AutoAlignRightAction.IsPressed();

        public bool WasAutoAlignLeftTriggered() => ExternalControlEnabled
            ? ExternalCommand.AutoAlignLeftPulse
            : AutoAlignLeftAction.triggered;

        public bool WasAutoAlignRightTriggered() => ExternalControlEnabled
            ? ExternalCommand.AutoAlignRightPulse
            : AutoAlignRightAction.triggered;

        public ReefscapeRobotMode CurrentRobotMode { get; private set; }
        public ReefscapeIntakeMode CurrentIntakeMode { get; private set; }
        [field: SerializeField] public CoralStationMode CurrentCoralStationMode { get; private set; }

        [SerializeField] protected bool superCycler = false;

        private GameObject _targetReef;
        protected bool FacingReef;

        private bool _hasCoral = true;
        private bool _hasAlgae;
        protected int HasCoralTrigger { get; set; }

        protected override void Awake()
        {
            base.Awake();

            RobotGamePieceController = GetComponent<ReefscapeRobotGamePieceController>();
            if (RobotGamePieceController == null)
            {
                Debug.LogError("ReefscapeRobotGamePieceController component not found on the robot!");
            }

            SetupInputActions();
        }

        protected override void Start()
        {
            base.Start();

            var coralStations = FindObjectsByType(typeof(CoralStation), FindObjectsSortMode.None);
            if (coralStations.Length == 0)
            {
                Debug.LogError("No CoralStation found in the scene!");
            }

            foreach (var coralStation in coralStations)
            {
                var station = (CoralStation)coralStation;
                if (Alliance == Alliance.Blue && station.Alliance == Alliance.Blue ||
                    Alliance == Alliance.Red && station.Alliance == Alliance.Red)
                {
                    station.Robots.Add(this);
                }
            }

            _targetReef = Alliance == Alliance.Blue ? GameObject.Find("BlueReef") : GameObject.Find("RedReef");
        }

        private void SetupInputActions()
        {
            L4Action = InputActionMap.FindAction("L4");
            L3Action = InputActionMap.FindAction("L3");
            L2Action = InputActionMap.FindAction("L2");
            L1Action = InputActionMap.FindAction("L1");
            ClimbAction = InputActionMap.FindAction("Climb");

            AutoAlignLeftAction = InputActionMap.FindAction("AutoAlignLeft");
            AutoAlignRightAction = InputActionMap.FindAction("AutoAlignRight");

            RobotModeToggleAction = InputActionMap.FindAction("RobotModeToggle");
            IntakeModeToggleAction = InputActionMap.FindAction("IntakeModeToggle");
            RobotSpecialAction = InputActionMap.FindAction("RobotSpecial");
        }

        protected override void Update()
        {
            base.Update();

            if (ExternalControlEnabled)
            {
                ApplyExternalControl();
                return;
            }

            if (RobotModeToggleAction.triggered && !RightStickModifierAction.IsPressed())
            {
                CurrentRobotMode = CurrentRobotMode switch
                {
                    ReefscapeRobotMode.Coral => ReefscapeRobotMode.Algae,
                    ReefscapeRobotMode.Algae => ReefscapeRobotMode.Coral,
                    _ => ReefscapeRobotMode.Coral
                };
            }

            if (IntakeModeToggleAction.triggered &&
                RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0 &&
                !RightStickModifierAction.IsPressed())
            {
                CurrentIntakeMode = CurrentIntakeMode switch
                {
                    ReefscapeIntakeMode.L1 => ReefscapeIntakeMode.Normal,
                    ReefscapeIntakeMode.Normal => ReefscapeIntakeMode.L1,
                    _ => ReefscapeIntakeMode.Normal
                };
            }

            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                return;
            }

            if (AutoAlignLeftAction.triggered || AutoAlignRightAction.triggered)
            {
                CheckFacingReef();
            }

            if (IntakeAction.IsPressed() && CurrentSetpoint != ReefscapeSetpoints.HighAlgae &&
                CurrentSetpoint != ReefscapeSetpoints.LowAlgae && CurrentSetpoint != ReefscapeSetpoints.Stack)
            {
                CurrentSetpoint = ReefscapeSetpoints.Intake;
            }
            else if (OuttakeAction.triggered)
            {
                CurrentSetpoint = ReefscapeSetpoints.Place;
            }
            else if (L1Action.triggered)
            {
                if (RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0 &&
                    RobotGamePieceController.GetPieceByName("Algae").currentStateNum == 0)
                {
                    if (CurrentSetpoint == ReefscapeSetpoints.Stow && CurrentRobotMode == ReefscapeRobotMode.Algae)
                    {
                        CurrentSetpoint = ReefscapeSetpoints.Stack;
                    }
                    else
                    {
                        CurrentSetpoint = ReefscapeSetpoints.Stow;
                    }
                }
                else if (RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0 ||
                         RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0)
                {
                    CurrentSetpoint = RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0
                        ? ReefscapeSetpoints.Processor
                        : ReefscapeSetpoints.L1;
                }
            }
            else if (L2Action.triggered)
            {
                CheckFacingReef();
                if (CurrentSetpoint is ReefscapeSetpoints.L2 or ReefscapeSetpoints.LowAlgae)
                {
                    CurrentSetpoint = ReefscapeSetpoints.Stow;
                }
                else
                {
                    var isTrue = RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0 ||
                                 (CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler);
                    CurrentSetpoint = isTrue ? ReefscapeSetpoints.LowAlgae : ReefscapeSetpoints.L2;
                }
            }
            else if (L3Action.triggered)
            {
                CheckFacingReef();
                if (CurrentSetpoint is ReefscapeSetpoints.L3 or ReefscapeSetpoints.HighAlgae)
                {
                    CurrentSetpoint = ReefscapeSetpoints.Stow;
                }
                else
                {
                    var isTrue = RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0 ||
                                 (CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler);
                    CurrentSetpoint = isTrue ? ReefscapeSetpoints.HighAlgae : ReefscapeSetpoints.L3;
                }
            }
            else if (L4Action.triggered)
            {
                CheckFacingReef();
                if (CurrentSetpoint is ReefscapeSetpoints.L4 or ReefscapeSetpoints.Barge)
                {
                    CurrentSetpoint = ReefscapeSetpoints.Stow;
                }
                else if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0 ||
                         RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0)
                {
                    var isTrue = RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0 ||
                                 (CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler);
                    var algaeSetpoint = RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0
                        ? ReefscapeSetpoints.Barge
                        : ReefscapeSetpoints.Stow;
                    CurrentSetpoint = isTrue ? algaeSetpoint : ReefscapeSetpoints.L4;
                }
            }
            else if (RobotSpecialAction.triggered)
            {
                CurrentSetpoint = CurrentSetpoint is ReefscapeSetpoints.RobotSpecial
                    ? ReefscapeSetpoints.Stow
                    : ReefscapeSetpoints.RobotSpecial;
            }
            else if (ClimbAction.triggered && !RightStickModifierAction.IsPressed())
            {
                CurrentSetpoint = CurrentSetpoint switch
                {
                    ReefscapeSetpoints.Stow => ReefscapeSetpoints.Climb,
                    ReefscapeSetpoints.Climb => ReefscapeSetpoints.Climbed,
                    ReefscapeSetpoints.Climbed => ReefscapeSetpoints.Stow,
                    _ => CurrentSetpoint
                };
            }
            else if (
                (StowAction.IsPressed() &&
                 (CurrentSetpoint != ReefscapeSetpoints.Climb && CurrentSetpoint != ReefscapeSetpoints.Climbed)) ||
                (CurrentSetpoint == ReefscapeSetpoints.Intake && !IntakeAction.IsPressed()) ||
                (CurrentSetpoint is ReefscapeSetpoints.HighAlgae or ReefscapeSetpoints.LowAlgae
                     or ReefscapeSetpoints.Stack &&
                 !IntakeAction.IsPressed() && RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0) &&
                !RightStickModifierAction.IsPressed())
            {
                CurrentSetpoint = ReefscapeSetpoints.Stow;
            }


            if (CurrentSetpoint != LastSetpoint && CurrentSetpoint != ReefscapeSetpoints.Place)
            {
                LastSetpoint = CurrentSetpoint;
                SetState(CurrentSetpoint);
            }
            
            HandleRumble();
        }

        private void ApplyExternalControl()
        {
            var targetSelected = ConsumeExternalTargetSelection();
            var climbRequested = false;
            if (ExternalCommand.HasGamepadControls)
            {
                if (ConsumeExternalRobotModeToggle())
                {
                    CurrentRobotMode = CurrentRobotMode == ReefscapeRobotMode.Coral
                        ? ReefscapeRobotMode.Algae
                        : ReefscapeRobotMode.Coral;
                }

                if (ConsumeExternalIntakeModeToggle() &&
                    RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0)
                {
                    CurrentIntakeMode = CurrentIntakeMode == ReefscapeIntakeMode.Normal
                        ? ReefscapeIntakeMode.L1
                        : ReefscapeIntakeMode.Normal;
                }

                climbRequested = ConsumeExternalClimbPulse();
            }
            else
            {
                CurrentRobotMode = ReefscapeRobotMode.Coral;
                CurrentIntakeMode = ReefscapeIntakeMode.Normal;
            }

            if (CurrentCoralStationMode != null)
            {
                CurrentCoralStationMode.DropType =
                    BaseGameManager.Instance.GameState != GameState.Auto && ExternalCommand.StationMode
                        ? DropType.Station
                        : DropType.Ground;
            }

            if (ExternalCommand.HasGamepadControls)
            {
                // Apply independent controls from the same gamepad sample
                // before resolving mutually exclusive final mechanism states.
                // This keeps a face-button selection from being consumed and
                // discarded when RT or the climb button arrives with it.
                if (targetSelected)
                {
                    ApplyExternalGamepadSelection();
                }

                if (climbRequested)
                {
                    SetState(CurrentSetpoint switch
                    {
                        ReefscapeSetpoints.Climb => ReefscapeSetpoints.Climbed,
                        ReefscapeSetpoints.Climbed => ReefscapeSetpoints.Stow,
                        _ => ReefscapeSetpoints.Climb
                    });
                    return;
                }

                if (IsPlacePressed())
                {
                    if (CurrentSetpoint != ReefscapeSetpoints.Place)
                    {
                        SetState(ReefscapeSetpoints.Place);
                    }
                    return;
                }

                if (IsAutoAlignLeftPressed() || IsAutoAlignRightPressed())
                {
                    CheckFacingReef();
                }
                return;
            }

            if (IsPlacePressed())
            {
                if (CurrentSetpoint != ReefscapeSetpoints.Place)
                {
                    SetState(ReefscapeSetpoints.Place);
                }
                return;
            }

            var desiredSetpoint = ExternalCommand.TargetSetpoint switch
            {
                0 => ReefscapeSetpoints.Stow,
                1 => ReefscapeSetpoints.Intake,
                2 => ReefscapeSetpoints.L1,
                3 => ReefscapeSetpoints.L2,
                4 => ReefscapeSetpoints.L3,
                5 => ReefscapeSetpoints.L4,
                _ => ReefscapeSetpoints.Stow
            };

            if (desiredSetpoint is ReefscapeSetpoints.L2 or ReefscapeSetpoints.L3 or ReefscapeSetpoints.L4 ||
                IsAutoAlignLeftPressed() || IsAutoAlignRightPressed())
            {
                CheckFacingReef();
            }

            if (CurrentSetpoint != desiredSetpoint)
            {
                SetState(desiredSetpoint);
            }
        }

        private void ApplyExternalGamepadSelection()
        {
            var coralState = RobotGamePieceController.GetPieceByName("Coral").currentStateNum;
            var algaeState = RobotGamePieceController.GetPieceByName("Algae").currentStateNum;

            switch (ExternalCommand.TargetSetpoint)
            {
                case 0:
                    SetState(ReefscapeSetpoints.Stow);
                    break;
                case 1:
                    SetState(ReefscapeSetpoints.Intake);
                    break;
                case 2:
                    if (coralState == 0 && algaeState == 0)
                    {
                        SetState(CurrentSetpoint == ReefscapeSetpoints.Stow &&
                                 CurrentRobotMode == ReefscapeRobotMode.Algae
                            ? ReefscapeSetpoints.Stack
                            : ReefscapeSetpoints.Stow);
                    }
                    else
                    {
                        SetState(algaeState > 0
                            ? ReefscapeSetpoints.Processor
                            : ReefscapeSetpoints.L1);
                    }
                    break;
                case 3:
                    CheckFacingReef();
                    if (CurrentSetpoint is ReefscapeSetpoints.L2 or ReefscapeSetpoints.LowAlgae)
                    {
                        SetState(ReefscapeSetpoints.Stow);
                    }
                    else
                    {
                        var useAlgaeSetpoint = coralState == 0 ||
                                               CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler;
                        SetState(useAlgaeSetpoint
                            ? ReefscapeSetpoints.LowAlgae
                            : ReefscapeSetpoints.L2);
                    }
                    break;
                case 4:
                    CheckFacingReef();
                    if (CurrentSetpoint is ReefscapeSetpoints.L3 or ReefscapeSetpoints.HighAlgae)
                    {
                        SetState(ReefscapeSetpoints.Stow);
                    }
                    else
                    {
                        var useAlgaeSetpoint = coralState == 0 ||
                                               CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler;
                        SetState(useAlgaeSetpoint
                            ? ReefscapeSetpoints.HighAlgae
                            : ReefscapeSetpoints.L3);
                    }
                    break;
                case 5:
                    CheckFacingReef();
                    if (CurrentSetpoint is ReefscapeSetpoints.L4 or ReefscapeSetpoints.Barge)
                    {
                        SetState(ReefscapeSetpoints.Stow);
                    }
                    else if (algaeState > 0 || coralState > 0)
                    {
                        var useAlgaeSetpoint = coralState == 0 ||
                                               CurrentRobotMode == ReefscapeRobotMode.Algae && superCycler;
                        SetState(useAlgaeSetpoint
                            ? algaeState > 0
                                ? ReefscapeSetpoints.Barge
                                : ReefscapeSetpoints.Stow
                            : ReefscapeSetpoints.L4);
                    }
                    break;
            }
        }

        protected void SetState(ReefscapeSetpoints setpoint)
        {
            LastSetpoint = CurrentSetpoint;
            CurrentSetpoint = setpoint;
        }

        protected void SetRobotMode(ReefscapeRobotMode mode)
        {
            CurrentRobotMode = mode;
        }

        private void CheckFacingReef()
        {
            if (_targetReef == null)
            {
                return;
            }
            var toReefVector = (_targetReef.transform.position - transform.position).normalized;
            var robotForwardVector = transform.forward.normalized;
            var angle = Vector3.Dot(robotForwardVector, toReefVector);
            FacingReef = angle > 0.0f;
        }

        public bool GetFacingReef()
        {
            return FacingReef;
        }

        private void HandleRumble()
        {
            if (!DoControllerRumble)
            {
                return;
            }

            switch (_hasCoral)
            {
                case false when (HasCoralTrigger > 0
                    ? RobotGamePieceController.GetPieceByName("Coral").currentStateNum == HasCoralTrigger
                        : RobotGamePieceController.GetPieceByName("Coral").currentStateNum > 0):
                    _hasCoral = true;
                    OnRumbleTrigger.Invoke();
                    break;
                case true when RobotGamePieceController.GetPieceByName("Coral").currentStateNum == 0:
                    _hasCoral = false;
                    break;
            }

            switch (_hasAlgae)
            {
                case false when RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0:
                    _hasAlgae = true;
                    OnRumbleTrigger.Invoke();
                    break;
                case true when RobotGamePieceController.GetPieceByName("Algae").currentStateNum == 0:
                    _hasAlgae = false;
                    break;
            }
        }
    }
}
