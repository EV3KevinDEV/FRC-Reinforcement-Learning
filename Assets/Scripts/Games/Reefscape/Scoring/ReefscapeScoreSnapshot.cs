using System;

namespace Games.Reefscape.Scoring
{
    /// <summary>Immutable copy of an alliance's Reefscape score.</summary>
    [Serializable]
    public readonly struct ReefscapeScoreSnapshot
    {
        public readonly int CoralPoints;
        public readonly int TroughPoints;
        public readonly int NetPoints;
        public readonly int ProcessorPoints;
        public readonly int ClimbPoints;
        public readonly int ParkPoints;
        public readonly int LeavePoints;
        public readonly int CoralScored;
        public readonly int AlgaeScored;
        public readonly int TotalPoints;

        public ReefscapeScoreSnapshot(ReefscapeScoreData data)
        {
            CoralPoints = data.CoralPoints;
            TroughPoints = data.TroughPoints;
            NetPoints = data.NetPoints;
            ProcessorPoints = data.ProcessorPoints;
            ClimbPoints = data.ClimbPoints;
            ParkPoints = data.ParkPoints;
            LeavePoints = data.LeavePoints;
            CoralScored = data.CoralScored;
            AlgaeScored = data.AlgaeScored;
            TotalPoints = data.TotalPoints;
        }
    }
}
