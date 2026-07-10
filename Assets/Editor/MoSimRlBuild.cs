using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MoSimRlBuild
{
    private const string ReefscapeScene = "Assets/Scenes/Reefscape.unity";
    private const string ServerPath = "_Build/RL/LinuxServer/MoSimRL.x86_64";
    private const string DevelopmentPath = "_Build/RL/LinuxDevelopment/MoSimRL.x86_64";

    [MenuItem("MoSimulator/RL/Build Linux Dedicated Server")]
    public static void BuildLinuxServer()
    {
        Build(ServerPath, StandaloneBuildSubtarget.Server, BuildOptions.None);
    }

    [MenuItem("MoSimulator/RL/Build Linux Development Player")]
    public static void BuildLinuxDevelopment()
    {
        Build(
            DevelopmentPath,
            StandaloneBuildSubtarget.Player,
            BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    private static void Build(
        string location,
        StandaloneBuildSubtarget subtarget,
        BuildOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(location) ?? "_Build/RL");
        var build = new BuildPlayerOptions
        {
            scenes = new[] { ReefscapeScene },
            locationPathName = location,
            target = BuildTarget.StandaloneLinux64,
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

        Debug.Log(
            $"MoSimulator RL build completed: {location} " +
            $"({report.summary.totalSize / (1024f * 1024f):F1} MiB)");
    }
}
