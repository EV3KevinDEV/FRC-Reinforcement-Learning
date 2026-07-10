using System;
using UnityEngine;

namespace RobotFramework
{
    /// <summary>
    /// Simulator-independent robot command used by the RL bridge. Values are
    /// normalized and are intentionally separate from Unity's Input System so
    /// normal keyboard and controller input remains unchanged.
    /// </summary>
    [Serializable]
    public struct ExternalRobotCommand
    {
        public Vector2 Translation;
        public float Rotation;
        public int TargetSetpoint;
        public float ManipulatorIntent;
        public bool StationMode;
        public bool PlacePulse;

        public bool IntakePressed => ManipulatorIntent > 0.33f;
        public bool PlacePressed => PlacePulse;

        public static bool IsPlaceRisingEdge(float currentIntent, float previousIntent) =>
            currentIntent < -0.33f && previousIntent >= -0.33f;

        public static ExternalRobotCommand Idle => new()
        {
            Translation = Vector2.zero,
            Rotation = 0f,
            TargetSetpoint = 0,
            ManipulatorIntent = 0f,
            StationMode = false,
            PlacePulse = false
        };
    }
}
