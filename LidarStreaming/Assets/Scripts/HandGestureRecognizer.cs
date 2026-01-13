using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

[System.Serializable]
public class GestureEvent : UnityEvent<string> { }

public class HandGestureRecognizer : MonoBehaviour
{
    [Header("Gesture Settings")]
    public float thumbIndexDistanceThreshold = 0.5f; // For pinch detection
    public float fingerCurlThreshold = 0.7f; // How curled a finger needs to be
    public float gestureConfidenceThreshold = 0.8f;

    [Header("Events")]
    public GestureEvent OnGestureDetected;

    // Current gesture state for each hand
    private string[] currentGestures = new string[2];

    // MediaPipe hand landmark indices
    private const int WRIST = 0;
    private const int THUMB_TIP = 4;
    private const int INDEX_TIP = 8;
    private const int INDEX_MCP = 5;
    private const int MIDDLE_TIP = 12;
    private const int MIDDLE_MCP = 9;
    private const int RING_TIP = 16;
    private const int RING_MCP = 13;
    private const int PINKY_TIP = 20;
    private const int PINKY_MCP = 17;

    void Awake()
    {
        if (OnGestureDetected == null)
            OnGestureDetected = new GestureEvent();
    }

    public void RecognizeGesture(List<Vector3> handJoints, int handIndex)
    {
        if (handJoints.Count < 21)
            return;

        string detectedGesture = DetectGesture(handJoints);

        // Only fire event if gesture changed
        if (detectedGesture != currentGestures[handIndex])
        {
            currentGestures[handIndex] = detectedGesture;
            OnGestureDetected.Invoke($"Hand {handIndex}: {detectedGesture}");
            Debug.Log($"Hand {handIndex} gesture: {detectedGesture}");
        }
    }

    private string DetectGesture(List<Vector3> joints)
    {
        // Check various gestures in priority order
        if (IsPinching(joints))
            return "Pinch";

        if (IsPointing(joints))
            return "Point";

        if (IsFist(joints))
            return "Fist";

        if (IsOpenPalm(joints))
            return "Open Palm";

        if (IsThumbsUp(joints))
            return "Thumbs Up";

        if (IsPeaceSign(joints))
            return "Peace Sign";

        if (IsOkaySign(joints))
            return "OK Sign";

        return "Unknown";
    }

    // Pinch: Thumb tip close to index tip
    private bool IsPinching(List<Vector3> joints)
    {
        float distance = Vector3.Distance(joints[THUMB_TIP], joints[INDEX_TIP]);
        return distance < thumbIndexDistanceThreshold;
    }

    // Point: Index finger extended, others curled
    private bool IsPointing(List<Vector3> joints)
    {
        bool indexExtended = IsFingerExtended(joints, INDEX_TIP, INDEX_MCP, WRIST);
        bool middleCurled = !IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringCurled = !IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyCurled = !IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return indexExtended && middleCurled && ringCurled && pinkyCurled;
    }

    // Fist: All fingers curled
    private bool IsFist(List<Vector3> joints)
    {
        bool indexCurled = !IsFingerExtended(joints, INDEX_TIP, INDEX_MCP, WRIST);
        bool middleCurled = !IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringCurled = !IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyCurled = !IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return indexCurled && middleCurled && ringCurled && pinkyCurled;
    }

    // Open Palm: All fingers extended
    private bool IsOpenPalm(List<Vector3> joints)
    {
        bool indexExtended = IsFingerExtended(joints, INDEX_TIP, INDEX_MCP, WRIST);
        bool middleExtended = IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringExtended = IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyExtended = IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return indexExtended && middleExtended && ringExtended && pinkyExtended;
    }

    // Thumbs Up: Thumb extended, others curled
    private bool IsThumbsUp(List<Vector3> joints)
    {
        bool thumbExtended = IsThumbExtended(joints);
        bool indexCurled = !IsFingerExtended(joints, INDEX_TIP, INDEX_MCP, WRIST);
        bool middleCurled = !IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringCurled = !IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyCurled = !IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return thumbExtended && indexCurled && middleCurled && ringCurled && pinkyCurled;
    }

    // Peace Sign: Index and middle extended, others curled
    private bool IsPeaceSign(List<Vector3> joints)
    {
        bool indexExtended = IsFingerExtended(joints, INDEX_TIP, INDEX_MCP, WRIST);
        bool middleExtended = IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringCurled = !IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyCurled = !IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return indexExtended && middleExtended && ringCurled && pinkyCurled;
    }

    // OK Sign: Thumb and index form circle, others extended
    private bool IsOkaySign(List<Vector3> joints)
    {
        float thumbIndexDistance = Vector3.Distance(joints[THUMB_TIP], joints[INDEX_TIP]);
        bool circleFormed = thumbIndexDistance < thumbIndexDistanceThreshold * 1.5f;

        bool middleExtended = IsFingerExtended(joints, MIDDLE_TIP, MIDDLE_MCP, WRIST);
        bool ringExtended = IsFingerExtended(joints, RING_TIP, RING_MCP, WRIST);
        bool pinkyExtended = IsFingerExtended(joints, PINKY_TIP, PINKY_MCP, WRIST);

        return circleFormed && middleExtended && ringExtended && pinkyExtended;
    }

    // Helper: Check if finger is extended
    private bool IsFingerExtended(List<Vector3> joints, int tipIndex, int mcpIndex, int wristIndex)
    {
        Vector3 tip = joints[tipIndex];
        Vector3 mcp = joints[mcpIndex];
        Vector3 wrist = joints[wristIndex];

        // Distance from tip to wrist should be greater than MCP to wrist
        float tipToWrist = Vector3.Distance(tip, wrist);
        float mcpToWrist = Vector3.Distance(mcp, wrist);

        return tipToWrist > mcpToWrist * fingerCurlThreshold;
    }

    // Helper: Check if thumb is extended (different logic due to thumb orientation)
    private bool IsThumbExtended(List<Vector3> joints)
    {
        Vector3 thumbTip = joints[4];
        Vector3 thumbMcp = joints[2];
        Vector3 wrist = joints[0];

        float tipToWrist = Vector3.Distance(thumbTip, wrist);
        float mcpToWrist = Vector3.Distance(thumbMcp, wrist);

        return tipToWrist > mcpToWrist * 1.2f;
    }

    public string GetCurrentGesture(int handIndex)
    {
        if (handIndex >= 0 && handIndex < currentGestures.Length)
            return currentGestures[handIndex];
        return "Unknown";
    }
}