using System.Collections.Generic;
using System.Linq;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.Robots;
using MoSimCore.Enums;
using UnityEngine;

namespace MoSimRL
{
    /// <summary>Maintains the current coral-cycle subgoal for one Unity worker.</summary>
    public sealed class RlScenarioManager
    {
        private readonly List<Transform> _blueTargets = new();
        private Transform _target;
        private int _stage;
        private int _targetLevel = 1;
        private string _scenario = "drive_leave";

        public int Stage => _stage;
        public string Scenario => _scenario;

        public void Configure(int stage, string scenario, ReefscapeRobotBase robot)
        {
            _stage = Mathf.Clamp(stage, 0, 4);
            _scenario = string.IsNullOrWhiteSpace(scenario) ? StageName(_stage) : scenario;
            if (_scenario == "pickup_test" && robot != null && !robot.PrepareRlGroundPickupTest())
            {
                Debug.LogWarning("RL pickup test could not place coral in the Team 118 ground intake.");
            }
            else if (_scenario == "empty_start" && robot != null && !robot.PrepareRlEmptyStart())
            {
                Debug.LogWarning("RL empty-start test could not remove Team 118's preload.");
            }
            RebuildTargets();
            SelectNextTarget(robot);
        }

        public void OnCycleSucceeded(ReefscapeRobotBase robot)
        {
            if (_stage is 1 or 2 && !robot.HasCoral)
            {
                robot.AddRlCoralPreload();
            }
            SelectNextTarget(robot);
        }

        public RlTaskDto Capture(ReefscapeRobotBase robot)
        {
            if (_target == null)
            {
                RebuildTargets();
                SelectNextTarget(robot);
            }

            var task = new RlTaskDto { target_level = _targetLevel };
            var coral = _stage >= 3 ? FindNearestCoral(robot) : null;
            if (coral != null)
            {
                var localCoral = robot.transform.InverseTransformPoint(coral.transform.position);
                task.coral_relative = new[] { localCoral.x, localCoral.z };
                task.coral_distance = Vector3.Distance(robot.transform.position, coral.transform.position);
                task.coral_valid = true;
                if (coral.TryGetComponent<Rigidbody>(out var coralBody))
                {
                    var localVelocity = robot.transform.InverseTransformDirection(coralBody.velocity);
                    task.coral_velocity = new[] { localVelocity.x, localVelocity.z };
                }
            }

            if (_target != null)
            {
                var localTarget = robot.transform.InverseTransformPoint(_target.position);
                task.target_relative = new[] { localTarget.x, localTarget.z };
                task.target_distance = Vector3.Distance(robot.transform.position, _target.position);
                task.heading_error = Mathf.DeltaAngle(
                    robot.transform.eulerAngles.y,
                    _target.eulerAngles.y) * Mathf.Deg2Rad;
            }

            if (!robot.HasCoral)
            {
                task.phase = task.coral_valid && task.coral_distance < 1.2f ? "intake" : "seek";
                task.active_distance = task.coral_valid ? task.coral_distance : task.target_distance;
            }
            else if (task.target_distance < 0.45f)
            {
                task.phase = "score";
                task.active_distance = task.target_distance;
            }
            else if (task.target_distance < 1.5f)
            {
                task.phase = "align";
                task.active_distance = task.target_distance;
            }
            else
            {
                task.phase = "carry";
                task.active_distance = task.target_distance;
            }

            return task;
        }

        private void RebuildTargets()
        {
            _blueTargets.Clear();
            var nodes = Object.FindObjectsByType<AlignNode>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (var node in nodes)
            {
                var reef = node.GetComponentInParent<Reef>();
                if (reef == null || reef.Alliance != Alliance.Blue)
                {
                    continue;
                }
                if (node.LeftNode != null)
                {
                    _blueTargets.Add(node.LeftNode.transform);
                }
                if (node.RightNode != null)
                {
                    _blueTargets.Add(node.RightNode.transform);
                }
            }
        }

        private void SelectNextTarget(ReefscapeRobotBase robot)
        {
            _targetLevel = _stage switch
            {
                0 => 1,
                1 => Random.Range(1, 3),
                _ => Random.Range(1, 5)
            };
            if (_blueTargets.Count == 0)
            {
                _target = null;
                return;
            }

            if (_stage == 0 && robot != null)
            {
                _target = _blueTargets.OrderBy(target =>
                    Vector3.Distance(target.position, robot.transform.position)).First();
            }
            else
            {
                _target = _blueTargets[Random.Range(0, _blueTargets.Count)];
            }
        }

        private static GameObject FindNearestCoral(ReefscapeRobotBase robot)
        {
            GameObject[] corals;
            try
            {
                corals = GameObject.FindGameObjectsWithTag("Coral");
            }
            catch (UnityException)
            {
                return null;
            }

            GameObject nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var coral in corals)
            {
                if (coral == null || !coral.activeInHierarchy || coral.transform.IsChildOf(robot.transform))
                {
                    continue;
                }
                var distance = (coral.transform.position - robot.transform.position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = coral;
                }
            }
            return nearest;
        }

        private static string StageName(int stage) => stage switch
        {
            0 => "drive_leave",
            1 => "preloaded_l1_l2",
            2 => "preloaded_all_levels",
            3 => "short_cycle",
            _ => "official_match"
        };
    }
}
