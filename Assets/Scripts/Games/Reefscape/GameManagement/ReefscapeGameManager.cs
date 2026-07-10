using System.Collections;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.Scoring;
using GameSystems.Management;
using MoSimCore;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.SceneTransitions;
using UnityEngine;

namespace Games.Reefscape.GameManagement
{
    public class ReefscapeGameManager : BaseGameManager
    {
        private ReefscapeScoreHandler _scoreHandler;
        
        private RobotSpawnController _robotSpawnController;

        [Header("Reefscape Specific Components")]
        [SerializeField] private GameObject reefscapeField;
        [SerializeField] private GameObject gamePieceWorld;
        
        private BargeLightController _bargeLightController;

        protected override void InitializeComponents()
        {
            base.InitializeComponents();
            
            _scoreHandler = FindAnyObjectByType<ReefscapeScoreHandler>();
            if (_scoreHandler == null)
            {
                Debug.LogError("ReefscapeScoreHandler component is missing.");
            }
            
            _robotSpawnController = FindAnyObjectByType<RobotSpawnController>();
            if (_robotSpawnController == null)
            {
                Debug.LogError("RobotSpawnController component is missing.");
            }

            _bargeLightController = FindAnyObjectByType<BargeLightController>();
            if (_bargeLightController == null)
            {
                Debug.LogError("BargeLightController component is missing.");
            }
        }

        protected override void OnGameStateChanged(GameState gameState)
        {
            if (gameState == GameState.Endgame)
            {
                if (RlRuntimeSettings.Enabled)
                {
                    return;
                }

                if (_bargeLightController == null)
                {
                    Debug.LogError("BargeLightController component is missing.");
                    return;
                }
                _bargeLightController.StartCoroutine(_bargeLightController.StartEndgameSequence());
            }
        }

        protected override IEnumerator PerformGameSpecificReset()
        {
            if (!RlRuntimeSettings.Enabled)
            {
                SceneManager.Instance.PlayTransition("CrossFade");
            }
            
            _scoreHandler.Reset();

            var newGamePieceWorld = Instantiate(gamePieceWorld);
            
            var newField = Instantiate(reefscapeField);

            yield return null;

            // var coralStations = newField.GetComponentsInChildren<CoralStation>();
            
            _bargeLightController = newField.GetComponentInChildren<BargeLightController>();

            yield return new WaitForSeconds(0.2f);
            
            _scoreHandler.ReRegisterScorers();
            
            yield return _robotSpawnController.StartMatch();
            
            // Wait a frame for robots to fully initialize
            yield return null;
            
            // Set up scorers on the new robots with the new start line colliders
            _scoreHandler.EnsureScorersOnRobots();
            _scoreHandler.ReRegisterScorers();
        }
    }
}
