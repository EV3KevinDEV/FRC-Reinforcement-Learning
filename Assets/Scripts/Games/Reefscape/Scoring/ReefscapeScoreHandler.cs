using GameSystems.Scoring;
using MoSimCore.Enums;
using UnityEngine;
using Games.Reefscape.Robots;
using Games.Reefscape.Scoring.Scorers;

namespace Games.Reefscape.Scoring
{
    public class ReefscapeScoreHandler : ScoreHandler<ReefscapeScoreData, ReefscapeScoreUI>
    {
        // Separate start line colliders for each alliance
        private BoxCollider _blueStartLineCollider;
        private BoxCollider _redStartLineCollider;

        protected override void UpdateUI()
        {
            base.UpdateUI();
        }

        protected override void Start()
        {
            EnsureScorersOnRobots();
            base.Start();
        }

        public ReefscapeScoreSnapshot GetSnapshot(Alliance alliance, bool refresh = true)
        {
            if (refresh)
            {
                CalculateScore(alliance);
            }

            return new ReefscapeScoreSnapshot(
                alliance == Alliance.Blue ? BlueScoreData : RedScoreData);
        }

        public void RefreshScores()
        {
            CalculateScore(Alliance.Blue);
            CalculateScore(Alliance.Red);
        }

        public override void Reset()
        {
            base.Reset();
            
            // Clear cached colliders so they get re-discovered from the new field
            _blueStartLineCollider = null;
            _redStartLineCollider = null;
            
            UpdateUI();
        }

        private void DiscoverStartLineColliders()
        {
            if (_blueStartLineCollider != null && _redStartLineCollider != null)
            {
                return;
            }
            
            // Find start line colliders by tag
            _blueStartLineCollider = FindStartLineByTag("BlueStartLine");
            _redStartLineCollider = FindStartLineByTag("RedStartLine");
            
            // Fallback: search by name if tags not found
            if (_blueStartLineCollider == null || _redStartLineCollider == null)
            {
                var allColliders = FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var bc in allColliders)
                {
                    var goName = bc.gameObject.name.ToLowerInvariant();
                    if (_blueStartLineCollider == null && goName.Contains("blue") && goName.Contains("start"))
                    {
                        _blueStartLineCollider = bc;
                    }
                    else if (_redStartLineCollider == null && goName.Contains("red") && goName.Contains("start"))
                    {
                        _redStartLineCollider = bc;
                    }
                }
            }
        }

        private BoxCollider FindStartLineByTag(string tag)
        {
            try
            {
                var go = GameObject.FindGameObjectWithTag(tag);
                if (go != null)
                {
                    var collider = go.GetComponent<BoxCollider>();
                    if (collider != null)
                    {
                        return collider;
                    }
                }
            }
            catch (UnityException)
            {
                // Tag doesn't exist
            }
            return null;
        }

        public void EnsureScorersOnRobots()
        {
            DiscoverStartLineColliders();

            var robots = FindObjectsByType<ReefscapeRobotBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var robot in robots)
            {
                if (robot == null) continue;
                var mb = robot as MonoBehaviour;
                if (mb == null) continue;

                var scorer = mb.GetComponent<AutoLineScorer>();
                if (scorer == null)
                {
                    scorer = mb.gameObject.AddComponent<AutoLineScorer>();
                }

                // Assign the correct start line collider based on the robot's alliance
                var startLineForRobot = robot.Alliance == Alliance.Blue ? _blueStartLineCollider : _redStartLineCollider;
                
                if (startLineForRobot != null)
                {
                    scorer.SetStartLineTrigger(startLineForRobot);
                }
                
                // Reset scorer state to allow scoring again after game reset
                scorer.ResetScorerState();
            }
        }
    }
}
