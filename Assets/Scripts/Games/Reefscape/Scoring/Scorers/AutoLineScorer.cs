using Games.Reefscape.Robots;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class AutoLineScorer : MonoBehaviour, IScorer
    {
        private ReefscapeRobotBase _robotBase;
        [SerializeField] private BoxCollider startLineTrigger;
        
        // State tracking for scoring
        private bool _hasBeenInStartZone;
        private bool _hasAlreadyScored;
        private bool _initialized;
        
        public Alliance Alliance { get; private set; }
        
        private void Awake()
        {
            InitializeRobotBase();
        }
        
        private void Start()
        {
            InitializeRobotBase();
            
            if (_robotBase == null)
            {
                enabled = false;
                return;
            }
            
            _hasBeenInStartZone = false;
            _hasAlreadyScored = false;

            // Register with score handler if not already registered
            var scoreHandler = FindFirstObjectByType<ReefscapeScoreHandler>();
            if (scoreHandler != null && !scoreHandler.BlueScorers.Contains(this) && !scoreHandler.RedScorers.Contains(this))
            {
                scoreHandler.RegisterScorer(this);
            }
        }
        
        private void InitializeRobotBase()
        {
            if (_initialized) return;
            
            _robotBase = GetComponent<ReefscapeRobotBase>();
            if (_robotBase != null)
            {
                Alliance = _robotBase.Alliance;
                _initialized = true;
            }
        }

        public void SetStartLineTrigger(BoxCollider collider)
        {
            if (collider == null) return;
            
            InitializeRobotBase();
            
            startLineTrigger = collider;
            enabled = true;
        }

        public void ResetScorerState()
        {
            InitializeRobotBase();
            
            _hasBeenInStartZone = false;
            _hasAlreadyScored = false;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is not ReefscapeScoreData reefscapeData)
                return;

            // Only process scoring during Auto period
            if (gameState != GameState.Auto)
            {
                // During non-Auto periods, only persist if already scored
                if (_hasAlreadyScored)
                {
                    reefscapeData.LeavePoints = ReefscapeOfficialScoring.Leave;
                }
                return;
            }

            if (!_initialized)
            {
                InitializeRobotBase();
            }

            if (startLineTrigger == null || _robotBase == null)
            {
                return;
            }

            bool robotInZone = IsRobotInZone();
            
            if (robotInZone)
            {
                _hasBeenInStartZone = true;
            }
            
            // Award points on transition: robot was in zone, now it's out
            if (!_hasAlreadyScored && _hasBeenInStartZone && !robotInZone)
            {
                _hasAlreadyScored = true;
                reefscapeData.LeavePoints = ReefscapeOfficialScoring.Leave;
            }
            
            // Persist points during Auto once awarded
            if (_hasAlreadyScored)
            {
                reefscapeData.LeavePoints = ReefscapeOfficialScoring.Leave;
            }
        }

        private bool IsRobotInZone()
        {
            // Use the robot's position and check distance to closest point on the collider
            // If the distance is very small, the robot is inside the collider bounds
            Vector3 closestPoint = startLineTrigger.ClosestPoint(_robotBase.transform.position);
            float distance = Vector3.Distance(closestPoint, _robotBase.transform.position);
            
            // If we're within a small threshold, consider the robot in the zone
            return distance < 0.5f;
        }
    }
}
