using System.Linq;
using Games.Reefscape.Robots;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;
using UnityEngine.Serialization;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ClimbScorer : MonoBehaviour, IScorer
    {
        public Alliance Alliance { get; private set; }
        private ReefscapeRobotBase _robotBase;
        private DriveController _driveController;

        [Header("Cage Detection Triggers")] 
        private BoxCollider _bargeZoneTrigger;

        [Tooltip("Trigger collider used to detect when the climber is in the cage for scoring.")] 
        [SerializeField]
        private BoxCollider cageScoringTrigger;

        [FormerlySerializedAs("autoClimbTrigger")]
        [Tooltip("Trigger colliders used to detect when the climber is in position to auto-climb.")]
        [SerializeField]
        private BoxCollider[] autoClimbTriggers;

        private OverlapBoxBounds _bargeZoneBounds;
        private OverlapBoxBounds _cageScoringBounds;
        private OverlapBoxBounds[] _autoClimbBounds;

        private bool _inBargeZone;
        public bool ScoringTriggered => CheckCage(_cageScoringBounds);
        public bool AutoClimbTriggered => CheckCage(_autoClimbBounds);

        private float _autoClimbTimer = 0f;
        private const float AutoClimbThreshold = 0.1f;

        private float finalScoreAdded;

        private void Start()
        {
            _bargeZoneTrigger = GetComponent<BoxCollider>();
            if (_bargeZoneTrigger == null)
            {
                Debug.LogWarning($"ClimbScorer: BoxCollider for barge zone detection is not assigned on {gameObject.name}.");
                enabled = false;
            }

            if (cageScoringTrigger == null)
            {
                Debug.LogWarning($"ClimbScorer: Cage scoring trigger is not assigned on {gameObject.name}.");
                enabled = false;
            }

            if (autoClimbTriggers == null)
            {
                Debug.LogWarning($"ClimbScorer: Auto-climb trigger(s) is/are not assigned on {gameObject.name}.");
                enabled = false;
            }

            _robotBase = GetComponentInParent<ReefscapeRobotBase>();
            if (_robotBase == null)
            {
                Debug.LogError("ClimbScorer: ReefscapeRobotBase component not found in parent.");
                enabled = false;
            }

            Alliance = _robotBase.Alliance;

            _driveController = GetComponentInParent<DriveController>();
            if (_driveController == null)
            {
                Debug.LogError("ClimbScorer: DriveController component not found in parent.");
                enabled = false;
            }

            var scoreHandler = FindFirstObjectByType<ReefscapeScoreHandler>();
            if (scoreHandler != null && (Alliance == Alliance.Blue
                    ? !scoreHandler.BlueScorers.Contains(this)
                    : !scoreHandler.RedScorers.Contains(this)))
            {
                scoreHandler.RegisterScorer(this);
            }

            _bargeZoneBounds = new OverlapBoxBounds(_bargeZoneTrigger);
            _cageScoringBounds = new OverlapBoxBounds(cageScoringTrigger);

            if (autoClimbTriggers != null)
            {
                _autoClimbBounds = new OverlapBoxBounds[autoClimbTriggers.Length];
                for (var i = 0; i < autoClimbTriggers.Length; i++)
                {
                    _autoClimbBounds[i] = new OverlapBoxBounds(autoClimbTriggers[i]);
                }
            }
        }

        private bool CheckCage(OverlapBoxBounds overlapBoxBounds)
        {
            if (overlapBoxBounds == _cageScoringBounds || _autoClimbBounds.Contains(overlapBoxBounds))
            {
                return overlapBoxBounds.OverlapBox()
                    .Any(overlappingCollider => overlappingCollider.gameObject.CompareTag("Cage"));
            }

            return false;
        }

        private bool CheckCage(OverlapBoxBounds[] overlapBoxBounds)
        {
            var result = false;
            foreach (var overlappingCollider in overlapBoxBounds)
            {
                if (!CheckCage(overlappingCollider))
                {
                    result = false;
                    break;
                }
                result = true;
            }

            if (result)
            {
                if (_autoClimbTimer >= AutoClimbThreshold)
                {
                    return true;
                }
                _autoClimbTimer += Time.deltaTime;
            }
            else
            {
                _autoClimbTimer = 0;
            }

            return false;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is ReefscapeScoreData reefscapeData)
            {
                if (gameState == GameState.Endgame)
                {
                    _inBargeZone = _bargeZoneBounds.OverlapBox()
                        .Any(overlappingCollider =>
                            overlappingCollider.gameObject.CompareTag(Alliance == Alliance.Blue
                                ? "BlueBargeZone"
                                : "RedBargeZone"));

                    if (!_inBargeZone)
                    {
                        finalScoreAdded = 0;
                        return;
                    }

                    finalScoreAdded = 0;
                    if (ScoringTriggered && !_driveController.IsTouchingGround)
                    {
                        reefscapeData.ClimbPoints += ReefscapeOfficialScoring.DeepCage;
                        finalScoreAdded = ReefscapeOfficialScoring.DeepCage;
                    }
                    else
                    {
                        reefscapeData.ParkPoints += ReefscapeOfficialScoring.Park;
                        finalScoreAdded = ReefscapeOfficialScoring.Park;
                    }
                } 
                else if (gameState == GameState.End)
                {
                    // Carry over the logic from the final endgame state to the final scoreboard
                    if (Mathf.Approximately(finalScoreAdded, ReefscapeOfficialScoring.Park))
                    {
                        reefscapeData.ParkPoints += ReefscapeOfficialScoring.Park;
                    } 
                    else if (Mathf.Approximately(finalScoreAdded, ReefscapeOfficialScoring.DeepCage))
                    {
                        reefscapeData.ClimbPoints += ReefscapeOfficialScoring.DeepCage;
                    }
                }
            }
        }
    }
}
