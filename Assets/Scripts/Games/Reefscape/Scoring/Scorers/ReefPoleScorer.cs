using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ReefPoleScorer : MonoBehaviour, IScorer
    {
        public Alliance Alliance { get; private set; }

        private Reef _reef;
        private GameObject _triggersCache;
        private Branch[] _branches;
        private bool _initialized;

        [SerializeField] private AudioClip scoreClip;
        [SerializeField] private AudioSource poleAudioSource;

        private void Awake()
        {
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                enabled = false;
                return;
            }
            
            _reef = GetComponentInParent<Reef>();
            if (_reef == null)
            {
                Debug.LogError("ReefPoleScorer could not find a Reef component in parent objects.");
                enabled = false;
                return;
            }

            Alliance = _reef.Alliance;

            _triggersCache = Utils.FindChild("Triggers", gameObject);
            if (_triggersCache == null)
            {
                Debug.LogError("ReefPoleScorer could not find 'Triggers' child object.");
                enabled = false;
                return;
            }
            
            var l4 = Utils.FindChild("L4", _triggersCache)?.GetComponent<BoxCollider>();
            var l3 = Utils.FindChild("L3", _triggersCache)?.GetComponent<BoxCollider>();
            var l2 = Utils.FindChild("L2", _triggersCache)?.GetComponent<BoxCollider>();
            if (l4 == null || l3 == null || l2 == null)
            {
                Debug.LogError("ReefPoleScorer could not find one or more branch colliders.");
                enabled = false;
                return;
            }

            _branches = new[]
            {
                new Branch(ReefscapeBranchHeight.L4, l4),
                new Branch(ReefscapeBranchHeight.L3, l3),
                new Branch(ReefscapeBranchHeight.L2, l2)
            };
            
            _initialized = true;

            foreach (var branch in _branches)
            {
                branch.bounds = Utils.MultiplyVectors(branch.ScoringTrigger.size, branch.ScoringTrigger.transform.lossyScale) / 2;
                branch.center = branch.ScoringTrigger.bounds.center;
                branch.rotation = branch.ScoringTrigger.transform.rotation;
            }
        }

        private void Update()
        {
            if (!_initialized || !isActiveAndEnabled)
            {
                Debug.LogError("ReefPoleScorer is initialized: " + _initialized + " and active/enabled: " + isActiveAndEnabled);
                return;
            }
            
            if (BaseGameManager.Instance.GameState == GameState.End)
            {
                return;
            }
            
            foreach (var branch in _branches)
            {
                if (branch?.ScoringTrigger == null)
                {
                    Debug.LogError("Branch or ScoringTrigger is null in ReefPoleScorer on " + gameObject.name);
                    continue;
                }
                CheckCoral(branch);

                if (branch.ScoredInAuto && !branch.Scored)
                {
                    branch.ScoredInAuto = false;
                }
            }
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (!_initialized || !isActiveAndEnabled)
            {
                Debug.LogError("ReefPoleScorer is initialized: " + _initialized + " and active/enabled: " + isActiveAndEnabled);
                return;
            }
            if (scoreData is not ReefscapeScoreData reefscapeData) return;

            foreach (var branch in _branches)
            {
                if (branch == null)
                {
                    Debug.LogError("Branch is null in ReefPoleScorer on " + gameObject.name);
                    continue;
                }

                var val = branch.TargetHeight switch
                {
                    ReefscapeBranchHeight.L4 => branch.ScoredInAuto
                        ? ReefscapeOfficialScoring.AutoL4
                        : ReefscapeOfficialScoring.TeleopL4,
                    ReefscapeBranchHeight.L3 => branch.ScoredInAuto
                        ? ReefscapeOfficialScoring.AutoL3
                        : ReefscapeOfficialScoring.TeleopL3,
                    ReefscapeBranchHeight.L2 => branch.ScoredInAuto
                        ? ReefscapeOfficialScoring.AutoL2
                        : ReefscapeOfficialScoring.TeleopL2,
                    _ => 0
                };

                if (branch.Scored)
                {
                    reefscapeData.CoralPoints += val;
                    reefscapeData.CoralScored += 1;
                }
            }
        }

        private void CheckCoral(Branch branch)
        {
            var hasCoral = false;
            Collider coralCollider = null;
            foreach (var result in Physics.OverlapBox(branch.center,
                         branch.bounds,
                         branch.rotation))
            {
                if (!result.CompareTag("Coral")) continue;
                hasCoral = true;
                coralCollider = result;
            }

            switch (hasCoral)
            {
                case true when !branch.Scored && coralCollider != null &&
                               coralCollider.transform.parent.gameObject.layer != LayerMask.NameToLayer("Robot"):
                    branch.Scored = true;
                    if (!branch.ScoredInAuto && BaseGameManager.Instance.GameState == GameState.Auto)
                    {
                        branch.ScoredInAuto = true;
                    }

                    if (poleAudioSource != null && scoreClip != null)
                    {
                        poleAudioSource.PlayOneShot(scoreClip);
                    }
                    break;
                case false when branch.Scored:
                    branch.Scored = false;
                    break;
            }
        }

        private class Branch
        {
            public ReefscapeBranchHeight TargetHeight { get; }
            public BoxCollider ScoringTrigger { get; }
            public bool Scored { get; set; }
            public bool ScoredInAuto { get; set; }

            public Vector3 bounds;

            public Vector3 center;
            
            public Quaternion rotation;

            public Branch(ReefscapeBranchHeight targetHeight, BoxCollider scoringTrigger)
            {
                TargetHeight = targetHeight;
                ScoringTrigger = scoringTrigger;
                Scored = false;
            }
        }
    }
}
