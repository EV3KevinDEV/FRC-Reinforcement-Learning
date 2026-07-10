using System;
using MoSimCore;
using UnityEngine;

namespace MoSimRL
{
    public static class RlBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--rl") < 0)
            {
                return;
            }

            var host = ReadString(args, "--rl-host", "127.0.0.1");
            var port = ReadInt(args, "--rl-port", 51000);
            var workerId = ReadInt(args, "--rl-worker-id", 0);
            var seed = ReadInt(args, "--rl-seed", workerId);
            var frameSkip = ReadInt(args, "--rl-frame-skip", 5);
            var graphical = Array.IndexOf(args, "--rl-graphical") >= 0;
            var realtime = Array.IndexOf(args, "--rl-realtime") >= 0;
            // Preserve the timestep that MoSimulator's mechanisms, drivetrain,
            // game pieces, and suspended cages were authored against. The bridge
            // controls decision duration independently from the PhysX solver rate.
            var nativeFixedDeltaTime = Time.fixedDeltaTime;
            RlRuntimeSettings.Configure(
                host,
                port,
                workerId,
                seed,
                118,
                frameSkip,
                20f,
                graphical,
                realtime,
                nativeFixedDeltaTime);

            PlayerPrefs.SetInt("GameMode", 0);
            PlayerPrefs.SetInt("MultiplayerMode", 0);
            PlayerPrefs.SetInt("Alliance", 0);
            PlayerPrefs.SetInt("CameraMode", 0);
            PlayerPrefs.SetFloat("ControllerRumble", 0f);
            PlayerPrefs.Save();

            Application.runInBackground = true;
            if (!graphical)
            {
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
            }
            // Prevent an accelerated rendered frame from queueing more than one
            // physics update. Rendered realtime sessions retain the regular
            // client's project frame pacing and maximum timestep.
            if (!realtime)
            {
                Time.maximumDeltaTime = RlRuntimeSettings.FixedDeltaTime;
            }

            var bridge = new GameObject($"MoSimRL-Worker-{workerId}");
            UnityEngine.Object.DontDestroyOnLoad(bridge);
            bridge.AddComponent<RlEnvironmentController>();
        }

        private static string ReadString(string[] args, string key, string fallback)
        {
            var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static int ReadInt(string[] args, string key, int fallback)
        {
            var raw = ReadString(args, key, fallback.ToString());
            return int.TryParse(raw, out var value) ? value : fallback;
        }
    }
}
