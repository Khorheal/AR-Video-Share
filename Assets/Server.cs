using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Timers;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using GoogleARCore;

public class Server : MonoBehaviour
{
    public RenderTexture camera;
    public RawImage myImage;
    public GameObject paint;
    public bool enableLog = false;
    private List<GameObject> paints = new List<GameObject>();

    Texture2D currentTexture;

    private TcpListener listener;
    private TcpListener inputListener;
    private const int port = 8010;
    private const int port2 = 8020;
    private bool stop = false;
    private bool disconnected = false;
    bool gotAll = false;

    TcpClient inputClient = null;
    NetworkStream inputStream = null;

    private bool isX = true;
    private Vector3 remoteTouchLocation;
    bool readyToReadAgain = false;
    bool remoteHitScreen = false;
    bool firstTouch = true;
    bool remoteFirstTouch = true;

    private List<TcpClient> clients = new List<TcpClient>(); // List of clients. May not be needed

    // Example class from CloudAnchors example
    public GoogleARCore.Examples.CloudAnchors.ARCoreWorldOriginHelper ARCoreWorldOriginHelper;

    // Example class from CloudAnchors example
    private GoogleARCore.Examples.CloudAnchors.ARKitHelper m_ARKit = new GoogleARCore.Examples.CloudAnchors.ARKitHelper();

    public Camera ARKitFirstPersonCamera; // ARKit Camera required for IOS support
    private Pose? m_LastHitPose = null; // Hit Location
    public GameObject Andy; // Debug spawn object

    // Must be same with SEND_COUNT on the client
    const int SEND_RECEIVE_COUNT = 15;
    const int SEND_RECEIVE_INPUT_COUNT = 8;

    private void Start()
    {
        Application.runInBackground = true; // Must be set to true or sockets will disconnect

        // Render coroutine
        StartCoroutine(initAndWaitForRenderTexture());
    }

    // Converts the data size to byte array and put result to the fullBytes array
    void byteLengthToFrameByteArray(int byteLength, byte[] fullBytes)
    {
        // Clear old data
        Array.Clear(fullBytes, 0, fullBytes.Length);
        // Convert int to bytes
        byte[] bytesToSendCount = BitConverter.GetBytes(byteLength);
        // Copy result to fullBytes
        bytesToSendCount.CopyTo(fullBytes, 0);
    }

    // Converts the byte array to the data size and returns the result
    int frameByteArrayToByteLength(byte[] frameBytesLength)
    {
        int byteLength = BitConverter.ToInt32(frameBytesLength, 0);
        return byteLength;
    }

    IEnumerator initAndWaitForRenderTexture()
    {
        // Show our camera on screen
        myImage.texture = camera;

        // Create texture to send
        currentTexture = new Texture2D(camera.width, camera.height, TextureFormat.RGB24, false);

        // Connect to the server
        listener = new TcpListener(IPAddress.Any, port);
        inputListener = new TcpListener(IPAddress.Any, port2);

        listener.Start();
        inputListener.Start();

        while (camera.width < 100)
        {
            yield return null;
        }

        // Start sending coroutine
        StartCoroutine(senderCOR());
    }

    WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
    IEnumerator senderCOR()
    {
        bool isConnected = false;
        bool isInputConnected = false;
        TcpClient client = null;
        NetworkStream stream = null;

        // Wait for client to connect in another Thread
        Task clientConnect = Task.Run(() =>
        {
            while (!stop)
            {
                // Wait for client connection
                client = listener.AcceptTcpClient();
                // Connected
                clients.Add(client);

                isConnected = true;
                stream = client.GetStream();
            }
        });

        // Wait for inputClient to connect in another Thread
        Task inputConnect = Task.Run(() =>
        {
            while (!stop)
            {
                // Wait for client connection
                inputClient = inputListener.AcceptTcpClient();
                // Connected
                clients.Add(inputClient);

                isInputConnected = true;
                inputStream = inputClient.GetStream();
            }
        });

        // Wait until client has connected
        while (!isConnected)
        {
            yield return null;
        }

        while (!isInputConnected)
        {
            yield return null;
        }

        Debug.Log("Connected!");

        bool readyToGetFrame = true;

        byte[] frameBytesLength = new byte[SEND_RECEIVE_COUNT];

        getInput();

        while (!stop)
        {
            // Wait for End of frame
            yield return endOfFrame;

            RenderTexture.active = camera;

            currentTexture.ReadPixels(new Rect(0, 0, camera.width, camera.height), 0, 0);
            currentTexture.Apply();

            byte[] pngBytes = currentTexture.EncodeToPNG();
            // Fill total byte length to send. Store result in frameBytesLength
            byteLengthToFrameByteArray(pngBytes.Length, frameBytesLength);

            // Set readyToGetFrame false
            readyToGetFrame = false;

            Task sendImage = Task.Run(() =>
            {
                // Send total byte count first
                stream.Write(frameBytesLength, 0, frameBytesLength.Length);
                //Debug.Log("Sent Image byte Length: " + frameBytesLength.Length);

                // Send the image bytes
                stream.Write(pngBytes, 0, pngBytes.Length);
                //Debug.Log("Sending Image byte array data: " + pngBytes.Length);

                // Sent. Set readyToGetFrame true
                readyToGetFrame = true;
            });

            // Wait until we are ready to get new frame
            while (!readyToGetFrame)
            {
                //Debug.Log("Waiting to get new frame");
                yield return null;
            }
        }
    }

    private void getInput()
    {
        //While loop in another Thread is fine so we don't block main Unity Thread
        Task inputReceive = Task.Run(() =>
        {
            while (!stop)
            {
                //Read Input Count
                int inputSize = readInputByteSize(SEND_RECEIVE_INPUT_COUNT);
                Debug.Log("Received Input byte Length: " + inputSize);

                //Read Input Bytes
                readInputByteArray(inputSize);
            }
        });
    }

    /////////////////////////////////////////////////////Read Input SIZE from Client///////////////////////////////////////////////////
    private int readInputByteSize(int size)
    {
        NetworkStream clientStream = inputClient.GetStream();
        byte[] inputBytesCount = new byte[size];
        var total = 0;
        do
        {
            var read = clientStream.Read(inputBytesCount, total, size - total);
            //Debug.LogFormat("Client recieved {0} bytes", total);
            if (read == 0)
            {
                disconnected = true;
                break;
            }
            total += read;
        } while (total != size);

        int byteLength;

        if (disconnected)
        {
            byteLength = -1;
        }
        else
        {
            byteLength = frameByteArrayToByteLength(inputBytesCount);
        }
        return byteLength;
    }

    /////////////////////////////////////////////////////Read Input Data Byte Array from Client///////////////////////////////////////////////////
    private void readInputByteArray(int size)
    {
        NetworkStream clientStream = inputClient.GetStream();
        byte[] inputBytes = new byte[size];
        var total = 0;

        float xPos;
        float yPos;

        do
        {
            var read = clientStream.Read(inputBytes, total, size - total);
            //Debug.LogFormat("Client recieved {0} bytes", total);
            if (read == 0)
            {
                disconnected = true;
                break;
            }
            total += read;
        } while (total != size);
        
        // Encode input and if Y coordinate is read change remoteHitScreen to true
        if (!disconnected)
        {
            string pos = Encoding.UTF8.GetString(inputBytes);
            Debug.Log("I AM INPUT: " + pos);
            string result = Regex.Match(pos, @"\d*\.\d*").Value;
            Debug.Log("Regex: " + result);

            if (isX)
            {
                xPos = float.Parse(pos);
                remoteTouchLocation.x = xPos;
                isX = false;
                readyToReadAgain = true;
            }
            else
            {
                yPos = float.Parse(pos);
                remoteTouchLocation.y = yPos;
                isX = true;
                gotAll = true;
            }

            if (gotAll)
            {
                Debug.Log("Remote touch: " + remoteTouchLocation);
                gotAll = false;
                remoteHitScreen = true;
            }
        }

        while (!readyToReadAgain)
        {
            Debug.Log("Am I waiting?");
            //Debug.LogFormat("Waiting");
            //System.Threading.Thread.Sleep(1);
        }
    }


    private void Update()
    {
        // Show the camera on screen
        myImage.texture = camera;

        m_LastHitPose = null;

        // If it's touched by client instantiate
        if (remoteHitScreen)
        {
            remoteHitScreen = false;

            raycastScreen(remoteTouchLocation.x, remoteTouchLocation.y);
            Debug.Log("Remote raycasting");

            if (m_LastHitPose != null)
            {
                if (remoteFirstTouch)
                {
                    remoteFirstTouch = false;
                    Debug.Log("Remote painting");
                    addPaintingToScreen();
                    readyToReadAgain = true;
                }
                else
                {
                    Debug.Log("Remote draw");
                    drawToScreen();
                    readyToReadAgain = true;
                }
            }
        }

        // If the player has not touched the screen then the update is complete.
        Touch touch;
        if (Input.touchCount < 1)
        {
            return;
        }
        touch = Input.GetTouch(0);

        //Debug.Log("X Pos: " + touch.position.x);
        //Debug.Log("Y Pos: " + touch.position.y);

        raycastScreen(touch.position.x, touch.position.y);

        // If there was a touch instantiate
        if (m_LastHitPose != null)
        {
            if (touch.phase == TouchPhase.Began)
            {
                addPaintingToScreen();
            }

            else if (touch.phase == TouchPhase.Moved)
            {
                drawToScreen();
            }
        }
    }

    private void addPaintingToScreen()
    {
        GameObject painting = new GameObject();
        painting.AddComponent<TrailRenderer>();
        painting.GetComponent<TrailRenderer>().startWidth = 0.01f;
        painting.GetComponent<TrailRenderer>().endWidth = 0.01f;
        painting.GetComponent<TrailRenderer>().minVertexDistance = 0;
        painting.GetComponent<TrailRenderer>().time = Mathf.Infinity;
        painting.GetComponent<TrailRenderer>().enabled = true;
        painting.SetActive(true);

        Instantiate(painting, m_LastHitPose.Value.position, m_LastHitPose.Value.rotation);
        firstTouch = true;
        paints.Add(painting);
        painting.GetComponent<TrailRenderer>().Clear();
        Debug.Log("Painting start pos: " + painting.transform.position);
    }

    private void drawToScreen()
    {
        GameObject painting = paints[paints.Count - 1];
        painting.transform.position = m_LastHitPose.Value.position;
        if (firstTouch)
        {
            painting.GetComponent<TrailRenderer>().Clear();
            firstTouch = false;
        }
        Debug.Log("Painting pos: " + painting.transform.position);
    }

    // Raycast against the location the player touched to search for planes.
    // Uses example functions from CloudAnchors example should implement own functions to avoid copyright
    // TODO Android support
    private void raycastScreen(float posX, float posY)
    {
        TrackableHit arcoreHitResult = new TrackableHit();

        if (Application.platform != RuntimePlatform.IPhonePlayer)
        {
            // Example function from ARCoreWorldOriginHelper
            if (ARCoreWorldOriginHelper.Raycast(posX, posY,
                    TrackableHitFlags.PlaneWithinPolygon, out arcoreHitResult))
            {
                m_LastHitPose = arcoreHitResult.Pose;
            }
        }
        else
        {
            // Example function from ARKitHelper
            Pose hitPose;
            switch (Input.deviceOrientation)
            {
                case DeviceOrientation.Portrait:
                    if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 3f, posY / 5.5f, out hitPose))
                    {
                        m_LastHitPose = hitPose;
                    }
                    break;
                case DeviceOrientation.PortraitUpsideDown:
                    if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 3f, posY / 5.5f, out hitPose))
                    {
                        m_LastHitPose = hitPose;
                    }
                    break;
                case DeviceOrientation.LandscapeLeft:
                    if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 5.5f, posY / 3f, out hitPose))
                    {
                        m_LastHitPose = hitPose;
                    }
                    break;
                case DeviceOrientation.LandscapeRight:
                    if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 5.5f, posY / 3f, out hitPose))
                    {
                        m_LastHitPose = hitPose;
                    }
                    break;
                case DeviceOrientation.FaceDown:
                    if (Screen.width > Screen.height)
                    {
                        if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 5.5f, posY / 3f, out hitPose))
                        {
                            m_LastHitPose = hitPose;
                        }
                    }
                    else
                    {
                        if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 3f, posY / 5.5f, out hitPose))
                        {
                            m_LastHitPose = hitPose;
                        }
                    }
                    break;
                case DeviceOrientation.FaceUp:
                    if (Screen.width > Screen.height)
                    {
                        if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 5.5f, posY / 3f, out hitPose))
                        {
                            m_LastHitPose = hitPose;
                        }
                    }
                    else
                    {
                        if (m_ARKit.RaycastPlane(
                        ARKitFirstPersonCamera, posX / 3f, posY / 5.5f, out hitPose))
                        {
                            m_LastHitPose = hitPose;
                        }
                    }
                    break;
            }
        }
    }


    // stop everything
    private void OnApplicationQuit()
    {
        stop = true;

        if (camera != null && !camera.isReadable)
        {
            camera.Release();
        }

        if (listener != null)
        {
            listener.Stop();
        }
        if (inputListener != null)
        {
            inputListener.Stop();
        }

        foreach (TcpClient c in clients)
            c.Close();
    }
}
