using System.Collections;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Games.Reefscape.GameManagement
{
    public class ReefscapeTimerManager : BaseTimerManager
    {
        protected override float MatchDuration => 150f;
        protected override float TeleopStartTime => 135f;
        protected override float EndgameStartTime => 20f;

        private float _rlTeleopPauseRemaining;

        public override void StartMatch()
        {
            _rlTeleopPauseRemaining = 0f;
            base.StartMatch();
        }
        
        protected override void StartTeleopTransition()
        {
            BeginTeleopTransition();
            if (MoSimCore.RlRuntimeSettings.Enabled)
            {
                _rlTeleopPauseRemaining = 3f;
            }
            else
            {
                StartCoroutine(HandleTeleopTransition());
            }
        }

        private void BeginTeleopTransition()
        {
            PauseTimer();
            Timer = TeleopStartTime;
            UpdateTimerText();
            CurrentRobotState = RobotState.Disabled;
            InvokeAutoEnd();
        }

        public override void AdvanceRlTime(float deltaTime)
        {
            if (MoSimCore.RlRuntimeSettings.Enabled && _rlTeleopPauseRemaining > 0f)
            {
                _rlTeleopPauseRemaining -= deltaTime;
                if (_rlTeleopPauseRemaining <= 0.0001f)
                {
                    _rlTeleopPauseRemaining = 0f;
                    CompleteTeleopTransition();
                }
                return;
            }

            base.AdvanceRlTime(deltaTime);
        }

        private IEnumerator HandleTeleopTransition()
        {

            yield return new WaitForSeconds(3f);

            CompleteTeleopTransition();
        }

        private void CompleteTeleopTransition()
        {
            CurrentGameState = GameState.Teleop;
            CurrentRobotState = RobotState.Enabled;
            ResumeTimer();
            InvokeTeleopStart();
            InvokeGameStateChange();
        }
    }
}
