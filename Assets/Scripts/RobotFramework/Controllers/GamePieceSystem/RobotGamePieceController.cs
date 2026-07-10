using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public class RobotGamePieceController<TPiece, TData> : RobotGamePieceControllerBase
        where TPiece : GamePiece<TData> where TData : IGamePieceData
    {
        [Header("Game Piece Nodes")] [SerializeField]
        protected List<GamePieceControllerNode> gamePieceNodes;
        
        private GamePieceState _preloadState;

        private RobotBase _robotBase;
        
        private Dictionary<string, GamePieceControllerNode> _nodeQuickLookup;

        protected void OnEnable()
        {
            _nodeQuickLookup = new Dictionary<string, GamePieceControllerNode>();
        }

        public void SetPreload(GamePieceState state)
        {
            _preloadState = state;
        }

        private void Start()
        {
            InitializeNodes();

            if (_preloadState != null)
            {
                AddPreload();
            }
        }

        private void InitializeNodes()
        {
            foreach (var node in gamePieceNodes)
            {
                node.StateLookup.Clear();

                if (node.gamePieceStates != null)
                {
                    foreach (var state in node.gamePieceStates)
                    {
                        if (state == null || string.IsNullOrWhiteSpace(state.name)) continue;
                        var key = state.name.Trim().ToLower();
                        if (!node.StateLookup.ContainsKey(key))
                        {
                            node.StateLookup[key] = new GamePieceData(state.stateNum, state.stateTarget,
                                state.stateMoveSpeed,
                                state.angularSpeed, state.smoothHandoff);
                        }
                    }
                }

                node.currentStateNum = 0;
                node.atTarget = false;
                node.movingTo = string.Empty;
                node.wasMovingTo = string.Empty;
                node.requestIntake.Clear();
                node.gamePieceController = this;
            }
        }

        public bool AddPreload()
        {
            if (_preloadState == null) return false;
            if (!TryGetComponent(out _robotBase)) return false;
            if (_robotBase.PreloadGamePiece == null) return false;

            var preload = Instantiate(_robotBase.PreloadGamePiece, _preloadState.stateTarget.position,
                _preloadState.stateTarget.rotation, _preloadState.stateTarget);

            var controller = preload.GetComponent<GamePieceController<TPiece, TData>>();
            if (!controller)
            {
                Destroy(preload);
                return false;
            }

            var node = gamePieceNodes.FirstOrDefault(node =>
                string.Equals(node.pieceName, "Coral", StringComparison.OrdinalIgnoreCase) && !node.controller);
            if (node == null)
            {
                Destroy(preload);
                return false;
            }

            node.controller = controller;
            node.currentStateNum = _preloadState.stateNum;
            node.wasMovingTo = _preloadState.name?.Trim().ToLower() ?? string.Empty;
            node.atTarget = true;
            return true;
        }

        private void FixedUpdate()
        {
            foreach (var node in gamePieceNodes)
            {
                ProcessNode(node);
            }
        }

        private void ProcessNode(GamePieceControllerNode controllerNode)
        {
            if (controllerNode?.intakes == null || controllerNode.intakes.Count == 0) return;

            foreach (var intake in controllerNode.requestIntake)
            {
                if (controllerNode.currentStateNum == 0 && intake.Item2)
                {
                    var val = intake;
                    val.Item2.requestIntake = val.Item1;
                    val.Item2.pieceName = controllerNode.pieceName;
                }
            }
            
            controllerNode.requestIntake.Clear();
            

            var securingIntake = controllerNode.intakes.FirstOrDefault(intake => intake && intake.securedGamePiece);

            if (securingIntake && controllerNode.currentStateNum == 0)
            {
                if (string.IsNullOrWhiteSpace(controllerNode.movingTo))
                {
                    if (controllerNode.gamePieceStates is { Length: > 0 } &&
                        !string.IsNullOrWhiteSpace(controllerNode.gamePieceStates[0].name))
                    {
                        controllerNode.movingTo = controllerNode.gamePieceStates[0].name.Trim().ToLower();
                    }
                }

                var state = GetNodeState(controllerNode, controllerNode.movingTo);

                controllerNode.controller = securingIntake.GamePieceController;
                securingIntake.RemovePiece();

                controllerNode.requestIntake.Clear();
                controllerNode.wasMovingTo = controllerNode.movingTo;
                controllerNode.atTarget = !securingIntake.smoothHandoff;
                controllerNode.currentStateNum = state.stateNum;
            }

            if (controllerNode.controller)
            {
                UpdateNodeMovement(controllerNode);
            }
        }

        private void UpdateNodeMovement(GamePieceControllerNode controllerNode)
        {
            controllerNode.requestIntake.Clear();

            if (string.IsNullOrWhiteSpace(controllerNode.movingTo))
            {
                return;
            }

            if (!controllerNode.atTarget && controllerNode.currentStateNum != 0)
            {
                MoveNodeTowards(controllerNode, controllerNode.wasMovingTo);
            }
            else if (!string.Equals(controllerNode.wasMovingTo, controllerNode.movingTo, StringComparison.OrdinalIgnoreCase) &&
                     controllerNode.atTarget)
            {
                MoveNodeTowards(controllerNode, controllerNode.movingTo);
            }
        }

        private void MoveNodeTowards(GamePieceControllerNode controllerNode, string targetStateName)
        {
            var state = GetNodeState(controllerNode, targetStateName);

            if (!state.stateTarget)
            {
                controllerNode.atTarget = true;
                return;
            }

            controllerNode.currentStateNum = state.stateNum;

            var result = controllerNode.controller.Move(
                state.stateTarget,
                state.stateMoveSpeed,
                state.stateAngularSpeed,
                state.smoothHandoff);

            controllerNode.atTarget = result == 1;
            controllerNode.wasMovingTo = targetStateName;
        }

        private GamePieceData GetNodeState(GamePieceControllerNode controllerNode, string targetStateName)
        {
            if (string.IsNullOrWhiteSpace(targetStateName))
            {
                return new GamePieceData(0, null, 0, 0, false);
            }

            targetStateName = targetStateName.Trim().ToLower();
            if (!controllerNode.StateLookup.TryGetValue(targetStateName, out var gamePieceData))
            {
                Debug.LogError($"State '{targetStateName}' not found in node '{controllerNode.pieceName}'.");
                return new GamePieceData(0, null, 0, 0, false);
            }

            return gamePieceData;
        }

        public override void RunCoroutine(IEnumerator coroutine)
        {
            StartCoroutine(coroutine);
        }
        
        /// <summary>
        /// Returns the Node to interact with a specific Piece
        /// </summary>
        /// <param name="nodeName"></param>
        /// <returns></returns>
        public GamePieceControllerNode GetPieceByName(string nodeName)
        {
            
            if (string.IsNullOrWhiteSpace(nodeName))
            {
                return null;
            }
            
            var found = _nodeQuickLookup.TryGetValue(nodeName, out var node);
            
            if (found) return node;
            
            node = gamePieceNodes.FirstOrDefault(checkNode => string.Equals(checkNode.pieceName?.Trim().ToLower(), nodeName.Trim().ToLower(), StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
            _nodeQuickLookup.Add(nodeName, node);

            return node;
        }

        [Serializable]
        public class GamePieceControllerNode
        {
            [Tooltip("Name of the game piece node (e.g. Coral, Algae, etc.)")]
            public string pieceName;
            
            [Tooltip("List of intakes associated with this node")]
            public List<GamePieceIntake<TPiece, TData>> intakes = new();
            
            [Tooltip("Ordered list of states for this game piece node")]
            public GamePieceState[] gamePieceStates;
            
            [Header("Runtime Variables (Read-Only)")]
            public GamePieceController<TPiece, TData> controller;
            public int currentStateNum = 0;
            public bool atTarget = false;
            public string movingTo = "";
            public string wasMovingTo = "";

            [NonSerialized] public Dictionary<string, GamePieceData> StateLookup = new();
            
            [HideInInspector] public RobotGamePieceController<TPiece, TData> gamePieceController;

            public List<(bool, GamePieceIntake<TPiece, TData>)> requestIntake = new List<(bool, GamePieceIntake<TPiece, TData>)>();
            
            public bool ReleaseGamePieceWithForce(Vector3 force,
                ForceMode forceMode = ForceMode.Impulse,
                bool whenAtTarget = true)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (whenAtTarget && !node.atTarget)
                {
                    return false;
                }

                if (node.controller)
                {
                    node.controller.Release(force, forceMode);
                    node.currentStateNum = 0;
                    node.controller = null;
                    node.atTarget = false;
                    return true;
                }

                return false;
            }
            
            public bool ReleaseGamePieceWithContinuedForce(Vector3 force, float time, float maxSpeed,
                bool whenAtTarget = true)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (whenAtTarget && !node.atTarget)
                {
                    return false;
                }

                if (node.controller != null)
                {
                    gamePieceController.RunCoroutine(node.controller.ContinuedRelease(force, time, maxSpeed));
                    node.currentStateNum = 0;
                    node.controller = null;
                    node.atTarget = false;
                    return true;
                }

                return false;
            }

            public bool HasPiece()
            {
                return currentStateNum > 0;
            }
            
            public GamePieceState GetCurrentState()
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                return node.gamePieceStates.FirstOrDefault(state =>
                    state.name.Equals(node.wasMovingTo, StringComparison.OrdinalIgnoreCase));
            }
            
            public void SetTargetState(string stateName)
            {
                var node = this;
                if (node == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (string.IsNullOrWhiteSpace(stateName))
                {
                    Debug.LogError($"SetNodeTargetState called with null or empty stateName for node '{stateName}'.");
                    return;
                }

                var key = stateName.Trim().ToLower();
                if (!node.StateLookup.ContainsKey(key))
                {
                    Debug.LogError($"State '{stateName}' not found in node '{pieceName}'.");
                    return;
                }

                node.movingTo = key;
            }

            public void SetTargetState(GamePieceState state)
            {
                if (state == null)
                {
                    Debug.LogError($"SetNodeTargetState called with null state for node '{pieceName}'.");
                    return;
                }
                SetTargetState(state.name);
            }
            
            public void RequestIntake(GamePieceIntake<TPiece, TData> intakePoint, bool requestIntake = true)
            {
                var node = this;
                if (!intakePoint)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                if (!node.requestIntake.Contains((requestIntake, intakePoint)) || !node.requestIntake.Contains((!requestIntake, intakePoint)))
                {
                    node.requestIntake.Add((requestIntake, intakePoint));
                }
            }
            
            public void MoveIntake(GamePieceIntake<TPiece, TData> intake, Transform newTarget)
            {
                if (!intake) return;
                
                intake.ChangeTarget(newTarget);
            }
            
            public bool IntakeHasPieces(GamePieceIntake<TPiece, TData> intake)
            {
                var pieceNode = this;
                if (pieceNode == null)
                {
                    throw new NullReferenceException($"GamePieceNode '{pieceName}' not found.");
                }

                return intake && intake.hasGamePiece;
            }
        }
    }
}
