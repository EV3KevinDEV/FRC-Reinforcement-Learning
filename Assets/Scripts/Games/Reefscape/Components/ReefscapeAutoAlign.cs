using System.Collections.Generic;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.Robots;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

public class ReefscapeAutoAlign : AutoAlign
{
    [Header("Offsets")]
    public Vector3 offset;
    private Vector3 realOffset;
    public float rotation;
    [Header("Auto Align Direction")]
    [Tooltip("Enable forward auto align (when facing the reef)")]
    public bool enableForwardAlign = true;
    [Tooltip("Enable backwards auto align (when facing away from the reef)")]
    public bool enableBackwardsAlign = true;
    [Header("Auto Align Settings")]
    [Tooltip("Maximum distance from alignment node for auto align to activate (in feet)")]
    [SerializeField] private float maxAlignDistanceFeet = 15f;
    
    private const float FEET_TO_METERS = 0.3048f;
    
    private List<AlignNode> targetNodes = new List<AlignNode>();
    private Dictionary<Transform, AlignNode> parentLookup = new Dictionary<Transform, AlignNode>();

    private AlignNode closest;
    private AlignNode secondClosest;
    
    private ReefscapeRobotBase _base;

    private (Transform, float)[] candidates;

    private bool startup;

    private Transform closests;
    private Transform secondCloses;

    private void Awake()
    {
        startup = true;
        _base = GetComponent<ReefscapeRobotBase>();
    }

    private void Update()
    {
        if (_base == null) return;

        InitializeTargets();
        
        if (_base.WasAutoAlignLeftTriggered() || _base.WasAutoAlignRightTriggered())
        {
            RefreshClosestTargets();
        }

        realOffset = offset * 0.0254f;
    }

    private void FixedUpdate()
    {
        if (_base == null) return;

        InitializeTargets();

        // A held external bumper can arrive without its one-frame pulse being
        // observed by Update. Resolve a target here as a safe fallback.
        if ((_base.IsAutoAlignLeftPressed() || _base.IsAutoAlignRightPressed()) &&
            (closests == null || _base.WasAutoAlignLeftTriggered() ||
             _base.WasAutoAlignRightTriggered()))
        {
            RefreshClosestTargets();
        }
        realOffset = offset * 0.0254f;

        if (PlayerPrefs.GetInt("PerspectiveAutoAlign", 1) == 1)
        {
            perspectiveRelativeAlign();
        }
        else
        {
            ReefRelativeAlign();
        }
        
    }

    private void InitializeTargets()
    {
        if (!startup) return;

        targetNodes.Clear();
        parentLookup.Clear();

        var nodes = GameObject.FindGameObjectsWithTag("ReefFace");
        foreach (var node in nodes)
        {
            if (node.TryGetComponent<AlignNode>(out var target) && target != null &&
                target.LeftNode != null && target.RightNode != null)
            {
                targetNodes.Add(target);
            }
        }

        foreach (var node in targetNodes)
        {
            parentLookup.TryAdd(node.LeftNode.transform, node);
            parentLookup.TryAdd(node.RightNode.transform, node);
        }

        candidates = new (Transform, float)[4];
        startup = false;
    }

    private bool RefreshClosestTargets()
    {
        InitializeTargets();
        ClosestFaces();
        (closests, secondCloses) = ClosestPoints();
        return closests != null;
    }

    private bool CameraFacesNode(AlignNode node)
    {
        if (node == null || _base == null) return false;

        GameObject activeCamera = _base.GetActiveCamera();
        if (activeCamera == null) return false;

        var nodeTransform = node.transform;
        
        Vector3 nodeForward = nodeTransform.forward;
    
        Vector3 cameraForward = activeCamera.transform.forward;
    
        float dotProduct = Vector3.Dot(cameraForward, nodeForward);
    
        return dotProduct > 0;
    }

    private void perspectiveRelativeAlign()
    {
        if (_base == null) return;

        bool alignLeft = _base.IsAutoAlignLeftPressed();
        bool alignRight = _base.IsAutoAlignRightPressed();
        if (!alignLeft && !alignRight) return;

        if (closests == null && !RefreshClosestTargets()) return;
        
        if (alignLeft)
        {
            if (TryPerspectiveAlignToNode(closests, false)) return;
            if (TryPerspectiveAlignToNode(secondCloses, false)) return;
            if (TryPerspectiveFaceFallback(closests, false)) return;
        }
        
        if (alignRight)
        {
            if (TryPerspectiveAlignToNode(closests, true)) return;
            if (TryPerspectiveAlignToNode(secondCloses, true)) return;
            TryPerspectiveFaceFallback(closests, true);
        }
    }

    private bool TryPerspectiveAlignToNode(Transform targetNode, bool rightButton)
    {
        if (targetNode == null ||
            !parentLookup.TryGetValue(targetNode, out var parentNode) ||
            parentNode == null)
        {
            return false;
        }

        bool cameraFacesNode = CameraFacesNode(parentNode);
        return TryAlignToNode(targetNode, rightButton ? cameraFacesNode : !cameraFacesNode);
    }

    private bool TryPerspectiveFaceFallback(Transform targetNode, bool rightButton)
    {
        if (targetNode == null ||
            !parentLookup.TryGetValue(targetNode, out var parentNode) ||
            parentNode == null)
        {
            return false;
        }

        bool cameraFacesNode = CameraFacesNode(parentNode);
        bool isLeftSide = rightButton ? cameraFacesNode : !cameraFacesNode;
        return TryAlignToNode(parentNode.LeftNode.transform, isLeftSide) ||
               TryAlignToNode(parentNode.RightNode.transform, isLeftSide);
    }

    private void ReefRelativeAlign()
    {
        if (_base == null) return;
        
        if (_base.IsAutoAlignLeftPressed())
        {
            TryAlignToNode(closests, true);
            TryAlignToNode(secondCloses, true);
        }
        
        if (_base.IsAutoAlignRightPressed())
        {
            TryAlignToNode(closests, false);
            TryAlignToNode(secondCloses, false);
        }
    }
    
    private bool TryAlignToNode(Transform targetNode, bool isLeftSide)
    {
        if (targetNode == null) return false;
        
        if (!parentLookup.TryGetValue(targetNode, out var parentNode)) return false;
        
        // Check if this is the correct side node
        bool isCorrectNode = isLeftSide 
            ? parentNode.LeftNode.transform == targetNode 
            : parentNode.RightNode == targetNode.gameObject;
            
        if (!isCorrectNode) return false;
        
        bool isFacingReef = _base.GetFacingReef();
        
        // Check if within max distance (convert feet to meters)
        float maxDistanceMeters = maxAlignDistanceFeet * FEET_TO_METERS;
        if (Vector3.Distance(transform.position, targetNode.position) > maxDistanceMeters) return false;
        
        var target = targetNode.transform;
        Quaternion targetRotation = target.rotation;
        Vector3 finalTarget = target.position;
        
        if ((!isFacingReef && enableBackwardsAlign) || !enableForwardAlign)
        {
            targetRotation *= Quaternion.Euler(0, 180, 0);
        }

        finalTarget += target.rotation * realOffset;
        
        targetRotation *= Quaternion.Euler(0, rotation, 0);
        
        AlignPosition(finalTarget, targetRotation);
        
        return true;
    }


    private (Transform close, Transform sec) ClosestPoints()
    {
        if (closest == null || secondClosest == null)
        {
            return (null, null);
        }
        
        var pointA = closest.LeftNode.transform;
        var pointB = closest.RightNode.transform;
        var pointC = secondClosest.LeftNode.transform;
        var pointD = secondClosest.RightNode.transform;

        var origin = transform.position;
    
        var distA = Vector3.Distance(pointA.position, origin);
        var distB = Vector3.Distance(pointB.position, origin);
        var distC = Vector3.Distance(pointC.position, origin);
        var distD = Vector3.Distance(pointD.position, origin);

        Transform finalClosest = null;
        var finalCloseDist = float.MaxValue;
        Transform finalSecondClosest = null;
        var finalSecondCloseDist = float.MaxValue;
    
        candidates[0] = (pointA, distA);
        candidates[1] = (pointB, distB);
        candidates[2] = (pointC, distC);
        candidates[3] = (pointD, distD);
        foreach (var (currentPoint, currentDistance) in candidates)
        {
            if (currentDistance < finalCloseDist)
            {
                finalSecondClosest = finalClosest;
                finalSecondCloseDist = finalCloseDist;
            
                finalClosest = currentPoint;
                finalCloseDist = currentDistance;
            }
            else if (currentDistance < finalSecondCloseDist)
            {
                // Node is not the closest, but is the new second closest
                finalSecondClosest = currentPoint;
                finalSecondCloseDist = currentDistance;
            }
        }

        return (finalClosest, finalSecondClosest);
    }
    
    private void ClosestFaces()
    {
        float closestDist = float.MaxValue;
        float secondClosestDist = float.MaxValue;
    
        closest = null;
        secondClosest = null;

        foreach (var node in targetNodes)
        {
            if (node == null || node.transform == null)
            {
                continue;
            }
        
            float currentDistance = Vector3.Distance(transform.position, node.transform.position);

            if (currentDistance < closestDist)
            {
                secondClosestDist = closestDist;
                secondClosest = closest;

                closestDist = currentDistance;
                closest = node;
            }
        
            else if (currentDistance < secondClosestDist)
            {
                secondClosestDist = currentDistance;
                secondClosest = node;
            }
        }
    }
}
