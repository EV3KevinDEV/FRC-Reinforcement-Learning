namespace MoSimCore
{
    /// <summary>
    /// Cross-assembly runtime settings populated by the RL bootstrap before the
    /// Reefscape scene starts. Keeping this in MoSimCore avoids circular assembly
    /// references from game and robot systems back to the bridge.
    /// </summary>
    public static class RlRuntimeSettings
    {
        public const int ProtocolVersion = 1;
        public const int ActionDimension = 6;
        public const int ObservationDimension = 62;

        public static bool Enabled { get; private set; }
        public static string Host { get; private set; } = "127.0.0.1";
        public static int Port { get; private set; } = 51000;
        public static int WorkerId { get; private set; }
        public static int Seed { get; private set; }
        public static int FrameSkip { get; set; } = 5;
        public static int TeamNumber { get; private set; } = 118;
        public static bool Graphical { get; private set; }
        public static bool Realtime { get; private set; }
        /// <summary>The project's native PhysX step. MoSimulator 2025 uses ~222.2 Hz.</summary>
        public static float FixedDeltaTime { get; private set; } = 0.0045f;
        /// <summary>Duration represented by one protocol frame-skip unit.</summary>
        public const float ControlDeltaTime = 0.02f;
        public static float ActiveTimeScale { get; private set; } = 20f;

        public static void Configure(
            string host,
            int port,
            int workerId,
            int seed,
            int teamNumber = 118,
            int frameSkip = 5,
            float activeTimeScale = 20f,
            bool graphical = false,
            bool realtime = false,
            float fixedDeltaTime = 0.0045f)
        {
            Enabled = true;
            Host = host;
            Port = port;
            WorkerId = workerId;
            Seed = seed;
            TeamNumber = teamNumber;
            FrameSkip = frameSkip;
            Graphical = graphical;
            Realtime = realtime;
            FixedDeltaTime = fixedDeltaTime;
            ActiveTimeScale = graphical ? 1f : activeTimeScale;
        }
    }
}
