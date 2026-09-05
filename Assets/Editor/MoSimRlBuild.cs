using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MoSimRlBuild
{
    private const string ReefscapeScene = "Assets/Scenes/Reefscape.unity";
    private const string LinuxServerPath = "_Build/RL/LinuxServer/MoSimRL.x86_64";
    private const string LinuxDevelopmentPath = "_Build/RL/LinuxDevelopment/MoSimRL.x86_64";
    private const string WindowsServerPath = "_Build/RL/WindowsServer/MoSimRL.exe";
    private const string WindowsDevelopmentPath = "_Build/RL/WindowsDevelopment/MoSimRL.exe";

    [MenuItem("MoSimulator/RL/Build Linux Dedicated Server")]
    public static void BuildLinuxServer()
    {
        Build(
            LinuxServerPath,
            BuildTarget.StandaloneLinux64,
            StandaloneBuildSubtarget.Server,
            BuildOptions.None);
    }

    [MenuItem("MoSimulator/RL/Build Linux Development Player")]
    public static void BuildLinuxDevelopment()
    {
        Build(
            LinuxDevelopmentPath,
            BuildTarget.StandaloneLinux64,
            StandaloneBuildSubtarget.Player,
            BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    [MenuItem("MoSimulator/RL/Build Windows Dedicated Server")]
    public static void BuildWindowsServer()
    {
        Build(
            WindowsServerPath,
            BuildTarget.StandaloneWindows64,
            StandaloneBuildSubtarget.Server,
            BuildOptions.None);
    }

    [MenuItem("MoSimulator/RL/Build Windows Development Player")]
    public static void BuildWindowsDevelopment()
    {
        Build(
            WindowsDevelopmentPath,
            BuildTarget.StandaloneWindows64,
            StandaloneBuildSubtarget.Player,
            BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    private static void Build(
        string location,
        BuildTarget target,
        StandaloneBuildSubtarget subtarget,
        BuildOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(location) ?? "_Build/RL");
        var build = new BuildPlayerOptions
        {
            scenes = new[] { ReefscapeScene },
            locationPathName = location,
            target = target,
            subtarget = (int)subtarget,
            options = options
        };

        var report = BuildPipeline.BuildPlayer(build);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"MoSimulator RL build failed: {report.summary.result} " +
                $"({report.summary.totalErrors} errors)");
        }

        // Addressables can log a content-build failure while BuildPlayer still
        // reports success. Such a player starts but cannot spawn robot 118.
        var addressablesSettings = Path.Combine(
            Path.GetDirectoryName(location) ?? ".",
            Path.GetFileNameWithoutExtension(location) + "_Data",
            "StreamingAssets", "aa", "settings.json");
        if (!File.Exists(addressablesSettings))
        {
            throw new InvalidOperationException(
                "MoSimulator RL build is missing its Addressables robot content. " +
                "Check the Addressables errors in the build log and rebuild.");
        }

        Debug.Log(
            $"MoSimulator RL build completed: {location} " +
            $"({report.summary.totalSize / (1024f * 1024f):F1} MiB)");
    }
}
