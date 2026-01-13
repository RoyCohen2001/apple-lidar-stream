using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

public class LidarReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    public int port = 5500;
    public int[] fallbackPorts = { 5501, 5502, 5503, 8080, 9000 };

    [Header("Hand Tracking")]
    public GameObject jointPrefab;
    public float scaleX = 10f; // Scale normalized coords to Unity space
    public float scaleY = 10f;
    public float scaleZ = 1f;
    public Vector3 offset = new Vector3(-5f, -5f, 0f); // Center the hand
    public int maxHands = 2; // Number of hands to track

    [Header("Line Rendering")]
    public Material lineMaterial;
    public float lineWidth = 0.02f;
    public Color[] handColors = { Color.black, Color.blue }; // Colors for each hand

    [Header("Gesture Recognition")]
    public bool enableGestureRecognition = true;
    public HandGestureRecognizer gestureRecognizer;

    private TcpListener server;
    private Thread receiveThread;
    private bool isRunning = true;

    private List<List<Vector3>> allHandJoints = new List<List<Vector3>>(); // Multiple hands
    private bool newHandFrame = false;
    private readonly object lockObject = new object();

    private List<HandVisuals> handVisuals = new List<HandVisuals>();
    private GameObject handsContainer;

    private double lastFrameReceiveTime;
    private StreamWriter logWriter;
    private double pythonTimestamp;

    // Use Stopwatch for thread-safe timing
    private Stopwatch stopwatch;

    
    // MediaPipe hand landmark connections (21 joints, indexed 0-20)
    private readonly int[][] handConnections = new int[][]
    {
        // Thumb
        new int[] { 0, 1 }, new int[] { 1, 2 }, new int[] { 2, 3 }, new int[] { 3, 4 },
        // Index finger
        new int[] { 0, 5 }, new int[] { 5, 6 }, new int[] { 6, 7 }, new int[] { 7, 8 },
        // Middle finger
        new int[] { 0, 9 }, new int[] { 9, 10 }, new int[] { 10, 11 }, new int[] { 11, 12 },
        // Ring finger
        new int[] { 0, 13 }, new int[] { 13, 14 }, new int[] { 14, 15 }, new int[] { 15, 16 },
        // Pinky
        new int[] { 0, 17 }, new int[] { 17, 18 }, new int[] { 18, 19 }, new int[] { 19, 20 },
        // Palm connections
        new int[] { 5, 9 }, new int[] { 9, 13 }, new int[] { 13, 17 }
    };

    // Class to hold visuals for one hand
    private class HandVisuals
    {
        public GameObject container;
        public List<GameObject> jointObjects = new List<GameObject>();
        public LineRenderer lineRenderer;
    }


    void Start()
    {
        if (!StartServer())
        {
            Debug.LogError("Failed to start server on any available port. Please check Windows Firewall or run as Administrator.");
            return;
        }

        string logPath = Path.Combine(Application.dataPath, "unity_hand_tracking_log.csv");
        logWriter = new StreamWriter(logPath);
        logWriter.WriteLine("unity_time,python_time,latency_ms,fps,num_hands,num_joints");
        logWriter.Flush();

        // Initialize stopwatch for thread-safe timing
        stopwatch = Stopwatch.StartNew();
        lastFrameReceiveTime = stopwatch.Elapsed.TotalSeconds;

        // Create a container for all hands
        handsContainer = new GameObject("HandsContainer");
        handsContainer.transform.SetParent(transform);

        // Initialize structures for multiple hands
        for (int handIndex = 0; handIndex < maxHands; handIndex++)
        {
            HandVisuals hand = new HandVisuals();
            hand.container = new GameObject($"Hand_{handIndex}");
            hand.container.transform.SetParent(handsContainer.transform);

            // Create joint objects for this hand
            for (int i = 0; i < 21; i++)
            {
                GameObject j = Instantiate(jointPrefab);
                j.transform.SetParent(hand.container.transform);
                j.SetActive(false); // Start inactive
                hand.jointObjects.Add(j);
            }

            SetupLineRenderer(hand, handIndex);
            handVisuals.Add(hand);
            allHandJoints.Add(new List<Vector3>());
        }

        // Setup gesture recognizer if not assigned
        if (gestureRecognizer == null && enableGestureRecognition)
        {
            gestureRecognizer = gameObject.AddComponent<HandGestureRecognizer>();
        }

        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log($"Tracking up to {maxHands} hands");
        if (enableGestureRecognition)
        {
            Debug.Log("Gesture recognition enabled");
        }
    }

    private void SetupLineRenderer(HandVisuals hand, int handIndex)
    {
        GameObject lineObj = new GameObject($"HandLines_{handIndex}");
        lineObj.transform.SetParent(hand.container.transform);
        hand.lineRenderer = lineObj.AddComponent<LineRenderer>();

        if (lineMaterial != null)
        {
            hand.lineRenderer.material = lineMaterial;
        }
        else
        {
            // Create a default material if none is assigned
            hand.lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }

        Color handColor = handIndex < handColors.Length ? handColors[handIndex] : Color.white;

        hand.lineRenderer.startWidth = lineWidth;
        hand.lineRenderer.endWidth = lineWidth;
        hand.lineRenderer.startColor = handColor;
        hand.lineRenderer.endColor = handColor;
        hand.lineRenderer.positionCount = 0;
        hand.lineRenderer.useWorldSpace = true;
    }

    private bool StartServer()
    {
        // Try primary port first
        if (TryStartServer(port))
            return true;

        // Try fallback ports
        foreach (int fallbackPort in fallbackPorts)
        {
            if (TryStartServer(fallbackPort))
            {
                port = fallbackPort;
                return true;
            }
        }

        return false;
    }

    private bool TryStartServer(int portToTry)
    {
        try
        {
            server = new TcpListener(System.Net.IPAddress.Loopback, portToTry);
            server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            server.Start();
            Debug.Log($"Server started successfully on port {portToTry}. Waiting for Python LiDAR connection...");
            return true;
        }
        catch (SocketException ex)
        {
            Debug.LogWarning($"Failed to bind to port {portToTry}: {ex.Message}");
            server = null;
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error starting server on port {portToTry}: {ex.Message}");
            server = null;
            return false;
        }
    }

    void ReceiveData()
    {
        try
        {
            TcpClient client = server.AcceptTcpClient();
            NetworkStream stream = client.GetStream();

            while (isRunning)
            {
                byte[] header = new byte[12];
                ReadFully(stream, header, 12);

                int packetLength = BitConverter.ToInt32(header, 0);
                pythonTimestamp = BitConverter.ToDouble(header, 4);


                byte[] payload = new byte[packetLength];
                ReadFully(stream, payload, packetLength);

                string data = System.Text.Encoding.UTF8.GetString(payload);

                lock (lockObject)
                {
                    ParseHandData(data);

                    // Use Stopwatch instead of Unity Time API (thread-safe)
                    double unityTime = stopwatch.Elapsed.TotalSeconds;
                    double latencyMs = (unityTime - pythonTimestamp) * 1000.0;

                    double fps = 1.0 / (unityTime - lastFrameReceiveTime);
                    lastFrameReceiveTime = unityTime;

                    int numHands = 0;
                    int numJoints = 0;
                    foreach (var hand in allHandJoints)
                    {
                        if (hand.Count >= 21) numHands++;
                        numJoints += hand.Count;
                    }

                    logWriter.WriteLine(
                        $"{unityTime:F6},{pythonTimestamp:F6},{latencyMs:F2},{fps:F2},{numHands},{numJoints}"
                    );
                    logWriter.Flush();

                    newHandFrame = true;
                }

                int totalJoints = 0;
                foreach (var hand in allHandJoints)
                {
                    totalJoints += hand.Count;
                }
            }
        }
        catch (Exception e)
        {
            if (isRunning)
            {
                Debug.LogError($"Receive thread exception: {e}");
            }
        }
    }

    void ParseHandData(string data)
    {
        // Clear all previous hand data
        foreach (var handJoints in allHandJoints)
        {
            handJoints.Clear();
        }

        try
        {
            // Check if data contains multiple hands (separated by "||")
            string[] handsData = data.Split(new string[] { "||" }, StringSplitOptions.RemoveEmptyEntries);


            for (int handIndex = 0; handIndex < handsData.Length && handIndex < maxHands; handIndex++)
            {
                string handData = handsData[handIndex];
                handData = handData.Replace("[[", "").Replace("]]", "");
                string[] joints = handData.Split(new string[] { "], [" }, StringSplitOptions.None);


                foreach (string j in joints)
                {
                    string clean = j.Replace("[", "").Replace("]", "").Trim();
                    string[] xyz = clean.Split(',');

                    if (xyz.Length != 3)
                    {
                        continue;
                    }

                    float x = float.Parse(xyz[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    float y = float.Parse(xyz[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    float z = float.Parse(xyz[2].Trim(), System.Globalization.CultureInfo.InvariantCulture);

                    // Scale normalized coordinates to Unity world space
                    Vector3 worldPos = new Vector3(
                        x * scaleX + offset.x,
                        (1f - y) * scaleY + offset.y, // Flip Y (screen coords are top-down)
                        z * scaleZ + offset.z
                    );

                    allHandJoints[handIndex].Add(worldPos);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing hand data: {e.Message}\nData: {data}");
        }
    }


    void Update()
    {
        lock (lockObject)
        {
            if (!newHandFrame) return;

            // Update each hand
            for (int handIndex = 0; handIndex < allHandJoints.Count && handIndex < handVisuals.Count; handIndex++)
            {
                List<Vector3> handJoints = allHandJoints[handIndex];
                HandVisuals visuals = handVisuals[handIndex];

                bool handActive = handJoints.Count >= 21;

                // Show/hide hand based on whether we have data
                visuals.container.SetActive(handActive);

                if (!handActive)
                {
                    continue;
                }


                // Update joint positions directly
                for (int i = 0; i < handJoints.Count && i < visuals.jointObjects.Count; i++)
                {
                    visuals.jointObjects[i].SetActive(true);
                    visuals.jointObjects[i].transform.position = handJoints[i];
                }

                // Hide unused joints
                for (int i = handJoints.Count; i < visuals.jointObjects.Count; i++)
                {
                    visuals.jointObjects[i].SetActive(false);
                }

                UpdateHandLines(visuals, handJoints);

                // Recognize gestures
                if (enableGestureRecognition && gestureRecognizer != null)
                {
                    gestureRecognizer.RecognizeGesture(handJoints, handIndex);
                }
            }

            newHandFrame = false;
        }
    }

    private void UpdateHandLines(HandVisuals visuals, List<Vector3> handJoints)
    {
        if (visuals.lineRenderer == null || handJoints.Count < 21)
        {
            if (visuals.lineRenderer != null)
            {
                visuals.lineRenderer.positionCount = 0;
            }
            return;
        }

        // Calculate total number of line segments
        int totalPoints = handConnections.Length * 2;
        visuals.lineRenderer.positionCount = totalPoints;

        int pointIndex = 0;
        foreach (int[] connection in handConnections)
        {
            int startIdx = connection[0];
            int endIdx = connection[1];

            if (startIdx < handJoints.Count && endIdx < handJoints.Count)
            {
                visuals.lineRenderer.SetPosition(pointIndex++, handJoints[startIdx]);
                visuals.lineRenderer.SetPosition(pointIndex++, handJoints[endIdx]);
            }
        }
    }

    private void ReadFully(NetworkStream stream, byte[] buffer, int length)
    {
        int offset = 0;
        while (offset < length)
        {
            int read = stream.Read(buffer, offset, length - offset);
            if (read == 0) throw new Exception("Socket closed");
            offset += read;
        }
    }

    void OnApplicationQuit()
    {
        isRunning = false;
        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Join(1000); // Wait max 1 second

        try
        {
            logWriter?.Close();
            server?.Stop();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error stopping server: {ex.Message}");
        }
    }


    void OnDestroy()
    {
        OnApplicationQuit();
    }
}