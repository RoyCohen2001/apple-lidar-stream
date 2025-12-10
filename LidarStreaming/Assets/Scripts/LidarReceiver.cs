using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class LidarReceiver : MonoBehaviour
{
    public RawImage rgbRawImage;
    public RawImage depthRawImage;

    [Header("Network Settings")]
    public int port = 5500;
    public int[] fallbackPorts = { 5501, 5502, 5503, 8080, 9000 };

    private TcpListener server;
    private Thread receiveThread;
    private bool isRunning = true;

    private byte[] rgbBytes;
    private byte[] depthBytes;
    private int frameWidth, frameHeight;
    private bool newFrameAvailable = false;

    private Texture2D rgbTexture;
    private Texture2D depthTexture;

    void Start()
    {
        if (!StartServer())
        {
            Debug.LogError("Failed to start server on any available port. Please check Windows Firewall or run as Administrator.");
            return;
        }

        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();
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
        if (server == null)
        {
            Debug.LogError("Server is null, cannot receive data");
            return;
        }

        try
        {
            TcpClient client = server.AcceptTcpClient();
            NetworkStream stream = client.GetStream();
            Debug.Log("Python connected");

            while (isRunning)
            {
                // Read 4-byte packet length
                byte[] lengthBytes = new byte[4];
                ReadFully(stream, lengthBytes, 4);
                int packetLength = BitConverter.ToInt32(lengthBytes, 0);

                // Read full packet
                byte[] packetBytes = new byte[packetLength];
                ReadFully(stream, packetBytes, packetLength);

                // Extract width and height
                frameWidth = BitConverter.ToInt32(packetBytes, 0);
                frameHeight = BitConverter.ToInt32(packetBytes, 4);

                int depthLength = frameWidth * frameHeight * 2; // ushort
                int rgbLength = frameWidth * frameHeight * 3;   // byte

                depthBytes = new byte[depthLength];
                rgbBytes = new byte[rgbLength];

                Array.Copy(packetBytes, 8, depthBytes, 0, depthLength);
                Array.Copy(packetBytes, 8 + depthLength, rgbBytes, 0, rgbLength);

                newFrameAvailable = true;
            }
        }
        catch (Exception e)
        {
            if (isRunning)
            {
                Debug.LogError("Receive thread exception: " + e);
            }
        }
    }

    void Update()
    {
        if (!newFrameAvailable) return;

        // Create or resize textures
        if (rgbTexture == null || rgbTexture.width != frameWidth || rgbTexture.height != frameHeight)
        {
            rgbTexture = new Texture2D(frameWidth, frameHeight, TextureFormat.RGB24, false);
            rgbRawImage.texture = rgbTexture;

            depthTexture = new Texture2D(frameWidth, frameHeight, TextureFormat.R16, false);
            depthRawImage.texture = depthTexture;
        }

        // Update textures
        rgbTexture.LoadRawTextureData(rgbBytes);
        rgbTexture.Apply();

        depthTexture.LoadRawTextureData(depthBytes);
        depthTexture.Apply();

        newFrameAvailable = false;
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