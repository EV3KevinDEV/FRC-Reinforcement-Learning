﻿using System;
using System.Collections;
using MoSimCore;
using MoSimCore.Enums;
using TMPro;
using UnityEngine;

namespace MoSimCore.BaseClasses.GameManagement.TimerManagement
{
    /// <summary>
    /// Abstract base class for managing match timing and game state transitions.
    /// Provides events for state changes, timer updates, and automatic transitions between game periods.
    /// Inherit from this class to create game-specific timer implementations.
    /// </summary>
    public abstract class BaseTimerManager : MonoBehaviour
    {
        /// <summary>Invoked when the game state changes (Auto, Teleop, Endgame, End).</summary>
        public event Action<GameState> OnGameStateChanged;
        
        /// <summary>Invoked each frame with the current timer value.</summary>
        public event Action<float> OnTimerUpdated;
        
        /// <summary>Invoked when the match starts.</summary>
        public event Action OnMatchStart;
        
        /// <summary>Invoked when autonomous period ends.</summary>
        public event Action OnAutoEnd;
        
        /// <summary>Invoked when teleoperated period starts.</summary>
        public event Action OnTeleopStart;
        
        /// <summary>Invoked when endgame period starts.</summary>
        public event Action OnEndgameStart;
        
        /// <summary>Invoked when the match ends.</summary>
        public event Action OnMatchEnd;
        
        /// <summary>Gets the current match timer value in seconds.</summary>
        public float Timer { get; protected set; }
        
        [SerializeField]
        [Tooltip("Text displaying the match timer.")]
        private TextMeshProUGUI timerText;
        
        /// <summary>Gets the current game state (Auto, Teleop, Endgame, End).</summary>
        public GameState CurrentGameState { get; protected set; }
        
        /// <summary>Gets the current robot state (Enabled or Disabled).</summary>
        [field: SerializeField]
        public RobotState CurrentRobotState { get; protected set; }
        
        /// <summary>Gets whether the timer is actively counting down.</summary>
        public bool IsCountingDown { get; protected set; } = true;
        
        /// <summary>Gets the total duration of the match in seconds.</summary>
        protected abstract float MatchDuration { get; }
        
        /// <summary>Gets the timer value when teleop period begins.</summary>
        protected abstract float TeleopStartTime { get; }
        
        /// <summary>Gets the timer value when endgame period begins.</summary>
        protected abstract float EndgameStartTime { get; }
        
        /// <summary>The DateTime timestamp for when the match was started, used to prevent cheating</summary>
        public DateTime GameStartTimeStamp { get; protected set; }

        /// <summary>Maximum discrepancy between the DateTime and the game timer, in seconds</summary>
        [SerializeField] private float maximumTimestampDiscrepancy = 1f;
        
        /// <summary>
        /// Subscribes to match end event for endgame handling.
        /// </summary>
        private void Start()
        {
            OnMatchEnd += () => StartCoroutine(WaitEndgame());
        }

        /// <summary>
        /// Initializes and starts a new match.
        /// Resets timer, sets initial game state to Auto, and enables robots.
        /// </summary>
        public virtual void StartMatch()
        {
            Timer = MatchDuration;
            GameStartTimeStamp = DateTime.Now;
            BaseGameManager.Instance.wasCheated = false;
            CurrentGameState = GameState.Auto;
            CurrentRobotState = RobotState.Enabled;
            IsCountingDown = true;
            OnMatchStart?.Invoke();
            OnGameStateChanged?.Invoke(CurrentGameState);
        }
        
        /// <summary>
        /// Updates the timer and UI every frame.
        /// </summary>
        protected virtual void Update()
        {
            if (!RlRuntimeSettings.Enabled && IsCountingDown && CurrentGameState != GameState.End)
            {
                UpdateTimer();
            }
            
            UpdateTimerText();
        }

        /// <summary>
        /// Decrements the timer and checks for state transitions.
        /// Invokes OnTimerUpdated event with the current timer value.
        /// Checks for time-altering cheating
        /// </summary>
        public virtual void UpdateTimer()
        {
            UpdateTimerInternal(Time.deltaTime, true);
        }

        /// <summary>
        /// Advances match time from the deterministic RL fixed-step loop.
        /// </summary>
        public virtual void AdvanceRlTime(float deltaTime)
        {
            if (!RlRuntimeSettings.Enabled || !IsCountingDown || CurrentGameState == GameState.End)
            {
                return;
            }

            UpdateTimerInternal(deltaTime, false);
        }

        private void UpdateTimerInternal(float deltaTime, bool validateWallClock)
        {
            Timer -= deltaTime;
            CheckStateTransitions();
            OnTimerUpdated?.Invoke(Timer);

            if (!validateWallClock || RlRuntimeSettings.Enabled)
            {
                return;
            }
            
            // Check for & flag discrepancies between elapsed DateTime and the Timer
            if (CurrentGameState == GameState.Auto) {
                if (Math.Abs(Math.Abs(MatchDuration - Timer) - Math.Abs((DateTime.Now - GameStartTimeStamp).TotalSeconds)) > maximumTimestampDiscrepancy)
                {
                    BaseGameManager.Instance.wasCheated = true;
                }
            } else {
                // TODO: The `-3` listed here comes from the transition time between auto and TeleOp. It is hardcoded to be 3 seconds in ReefscapeTimerManager, so it's not possible to have it adapt or generalized between games
                if (Math.Abs(Math.Abs(MatchDuration - Timer) - Math.Abs((DateTime.Now - GameStartTimeStamp).TotalSeconds - 3)) > maximumTimestampDiscrepancy)
                {
                    BaseGameManager.Instance.wasCheated = true;
                }
            }
        }

        /// <summary>
        /// Updates the timer UI text in MM:SS format.
        /// </summary>
        protected virtual void UpdateTimerText()
        {
            var minutes = Mathf.FloorToInt(Timer / 60);
            var seconds = Mathf.FloorToInt(Timer % 60);
            if (timerText)
            {
                timerText.text = $"{minutes:0}:{seconds:00}";
            }
        }
        
        /// <summary>
        /// Checks if the timer has reached state transition thresholds and updates game state accordingly.
        /// Transitions occur at TeleopStartTime, EndgameStartTime, and when the timer reaches zero.
        /// </summary>
        protected virtual void CheckStateTransitions()
        {
            var previousState = CurrentGameState;

            if (Timer <= EndgameStartTime && CurrentGameState == GameState.Teleop)
            {
                CurrentGameState = GameState.Endgame;
                OnEndgameStart?.Invoke();
                OnGameStateChanged?.Invoke(CurrentGameState);
            }
            else if (Timer <= TeleopStartTime && CurrentGameState == GameState.Auto)
            {
                StartTeleopTransition();
            }
            else if (Timer <= 0f && CurrentGameState != GameState.End)
            {
                Timer = 0f;
                CurrentGameState = GameState.End;
                CurrentRobotState = RobotState.Disabled;
                OnMatchEnd?.Invoke();
                OnGameStateChanged?.Invoke(CurrentGameState);
            }
        }

        /// <summary>
        /// Initiates the transition from autonomous to teleoperated period.
        /// Implement this to handle game-specific transition behavior (e.g., robot delays).
        /// </summary>
        protected abstract void StartTeleopTransition();

        /// <summary>
        /// Waits briefly after match end before re-enabling robots.
        /// This provides time for final scoring or animations.
        /// </summary>
        /// <returns>
        /// A coroutine that waits for a short duration before setting robots to Enabled state.
        /// </returns>
        protected virtual IEnumerator WaitEndgame()
        {
            yield return new WaitForSeconds(3f);
            CurrentRobotState = RobotState.Enabled;
        }

        /// <summary>
        /// Pauses the countdown timer.
        /// </summary>
        public virtual void PauseTimer()
        {
            IsCountingDown = false;
        }
        
        /// <summary>
        /// Resumes the countdown timer.
        /// </summary>
        public virtual void ResumeTimer()
        {
            IsCountingDown = true;
        }
        
        /// <summary>
        /// Invokes the OnAutoEnd event. Used by derived classes to trigger autonomous end.
        /// </summary>
        protected void InvokeAutoEnd()
        {
            OnAutoEnd?.Invoke();
        }

        /// <summary>
        /// Invokes the OnTeleopStart event. Used by derived classes to trigger teleop start.
        /// </summary>
        protected void InvokeTeleopStart()
        {
            OnTeleopStart?.Invoke();
        }

        /// <summary>
        /// Invokes the OnGameStateChanged event with the current state.
        /// </summary>
        protected void InvokeGameStateChange()
        {
            OnGameStateChanged?.Invoke(CurrentGameState);
        }

        /// <summary>
        /// Resets the timer to its initial value and disables robots.
        /// Updates the timer UI to show the reset time.
        /// </summary>
        public void ResetTimer()
        {
            CurrentRobotState = RobotState.Disabled;
            Timer = MatchDuration;
            BaseGameManager.Instance.wasCheated = false;
            UpdateTimerText();
        }

        /// <summary>
        /// Stops all running coroutines and pauses the timer.
        /// Called during match reset to clean up any ongoing timer operations.
        /// </summary>
        public virtual void StopAllTimerCoroutines()
        {
            StopAllCoroutines();
            IsCountingDown = false;
        }
    }
}
