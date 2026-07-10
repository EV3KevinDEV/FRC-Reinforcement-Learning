using System.Collections.Generic;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class BargeScorer : MonoBehaviour, IScorer
    {
        [field: SerializeField] public Alliance Alliance { get; private set; }

        private BoxCollider _scoringCollider;
        private Vector3 boxSize;
        private Vector3 boxPosition;
        private Quaternion boxRotation;
        private HashSet<Collider> _scoredAlgae = new HashSet<Collider>();
        private int _algaeCount = 0;
        private bool _gameEnded = false;

        private void Start()
        {
            _scoringCollider = GetComponent<BoxCollider>();
            if (_scoringCollider == null)
            {
                Debug.LogError("BargeScorer could not find a BoxCollider component.");
                enabled = false;
                return;
            }
            
            boxSize = Utils.MultiplyVectors(_scoringCollider.size, _scoringCollider.transform.lossyScale) / 2;
            boxPosition = _scoringCollider.bounds.center;
            boxRotation = _scoringCollider.transform.rotation;
        }

        private void Update()
        {
            // Stop accepting new algae once the game has ended
            if (_gameEnded)
            {
                return;
            }

            var results = Physics.OverlapBox(boxPosition, boxSize, boxRotation);

            // Build current set of algae colliders in the zone
            var currentAlgae = new HashSet<Collider>();
            foreach (var result in results)
            {
                if (result == null) continue;
                if (!result.CompareTag("Algae")) continue;
                currentAlgae.Add(result);
                if (!_scoredAlgae.Contains(result))
                {
                    // New algae entered the zone, mark it as scored
                    _scoredAlgae.Add(result);
                    _algaeCount++;
                }
            }

            // Remove algae that have left the zone and adjust count
            var toRemove = new List<Collider>();
            foreach (var scored in _scoredAlgae)
            {
                if (scored == null)
                {
                    toRemove.Add(scored);
                    continue;
                }

                if (!currentAlgae.Contains(scored))
                {
                    toRemove.Add(scored);
                }
            }

            foreach (var rem in toRemove)
            {
                if (_scoredAlgae.Remove(rem))
                {
                    _algaeCount = Mathf.Max(0, _algaeCount - 1);
                }
            }
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            // Mark game as ended when End state is reached
            if (gameState == GameState.End)
            {
                _gameEnded = true;
            }

            if (scoreData is ReefscapeScoreData reefscapeScoreData)
            {
                // Add the total points based on counted algae
                reefscapeScoreData.NetPoints += _algaeCount * ReefscapeOfficialScoring.Net;
                reefscapeScoreData.AlgaeScored += _algaeCount;
            }
            else
            {
                Debug.LogError("Invalid score data type passed to BargeScorer.");
            }
        }
    }
}
