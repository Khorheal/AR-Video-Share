using UnityEngine;
using UnityEngine.UI;
using System.Net.Sockets;
using System;
using System.Text;
using System.Collections;
using System.Threading.Tasks;

public class Client : MonoBehaviour
{
    public RawImage image;
    public bool enableLog = false;

    const int port = 8010;
    const int port2 = 8020;
    public string IP = "192.168.1.165";
    TcpClient client;
    TcpClient input;

    Texture2D tex;

    Touch touch;

    private bool stop = false;
    private bool displayImage = false;
    bool readyToReadAgain = false;
    bool disconnected = false;

    bool startCoroutine = false;
    bool canGetInput = false;
    Vector3 pos; // Touch position

    //This must be the-same with SEND_COUNT on the server
    const int SEND_RECEIVE_COUNT = 15;
    const int SEND_RECEIVE_INPUT_COUNT = 8;

    byte[] imageBytes = null;

    // Use this for initialization
    void Start()
    {
        Application.runInBackground = true;

        tex = new Texture2D(0, 0);
        client = new TcpClient();
        input = new TcpClient();
        //image.rectTransform.sizeDelta = new Vector2(Screen.width, Screen.height);

        //Connect to server from another Thread
        Task serverConnect = Task.Run(() =>
        {
            LOGWARNING("Connecting to server...");

            // Connect to main port
            client.Connect(IP, port);
            LOGWARNING("Connected! client");

            // Connect to input port
            input.Connect(IP, port2);
            LOGWARNING("Connected! input");

            // Set this to true so that we can send Input
            startCoroutine = true;
            imageReceiver();
        });

        StartCoroutine(inputSender());
    }


    void imageReceiver()
    {
        //While loop in another Thread is fine so we don't block main Unity Thread
        Task imageReceive = Task.Run(() =>
        {
            while (!stop)
            {
                //Read Image Count
                int imageSize = readImageByteSize(SEND_RECEIVE_COUNT);
                LOGWARNING("Received Image byte Length: " + imageSize);

                //Read Image Bytes and Display it
                readFrameByteArray(imageSize);
            }
        });
    }

    IEnumerator inputSender()
    {
        // Wait until Input is connected
        while (!startCoroutine)
        {
            Debug.Log("Waiting to connect to input");
            yield return null;
        }

        NetworkStream inputStream = input.GetStream();

        while (!stop)
        {
            byte[] touchBytesLength = new byte[SEND_RECEIVE_INPUT_COUNT]; // X pos byte length
            byte[] touchBytesLength2 = new byte[SEND_RECEIVE_INPUT_COUNT]; // Y pos byte length
            byte[] inputBytes = null; // x pos
            byte[] inputBytes2 = null; // y pos

            //Read input
            Task getInput = Task.Run(() =>
            {
                // It first sends the X value then Y value
                if (canGetInput)
                {
                    canGetInput = false;

                    // Change our string to bytes
                    inputBytes = Encoding.UTF8.GetBytes(pos.x.ToString());

                    // Fill total byte length to send. Store result in touchBytesLength
                    byteLengthToFrameByteArray(inputBytes.Length, touchBytesLength);

                    Debug.Log("x Input byte Length: " + touchBytesLength.Length);

                    // Send total byte count first
                    inputStream.Write(touchBytesLength, 0, touchBytesLength.Length);
                    Debug.Log("Sent x Input byte Length: " + touchBytesLength.Length);

                    // Send the input bytes
                    inputStream.Write(inputBytes, 0, inputBytes.Length);
                    Debug.Log("Sending x Input byte array data: " + inputBytes.Length);

                    // Change our string to bytes
                    inputBytes2 = Encoding.UTF8.GetBytes(pos.y.ToString());

                    // Fill total byte length to send. Store result in touchBytesLength
                    byteLengthToFrameByteArray(inputBytes2.Length, touchBytesLength2);

                    Debug.Log("y Input byte Length: " + touchBytesLength2.Length);

                    // Send total byte count first
                    inputStream.Write(touchBytesLength2, 0, touchBytesLength2.Length);
                    Debug.Log("Sent y Input byte Length: " + touchBytesLength2.Length);

                    // Send the input bytes
                    inputStream.Write(inputBytes2, 0, inputBytes2.Length);
                    Debug.Log("Sending y Input byte array data: " + inputBytes2.Length);
                }

                // Wait until we can send another
                while (!canGetInput && Application.isPlaying)
                {
                    //Debug.Log("Waiting for input");
                }
            });

            // Wait until we are ready to get new input
            while (!canGetInput)
            {
                //Debug.Log("Waiting to get new Input");
                yield return null;
            }
        }
    }


    //Converts the data size to byte array and put result to the fullBytes array
    void byteLengthToFrameByteArray(int byteLength, byte[] fullBytes)
    {
        //Clear old data
        Array.Clear(fullBytes, 0, fullBytes.Length);
        //Convert int to bytes
        byte[] bytesToSendCount = BitConverter.GetBytes(byteLength);
        //Copy result to fullBytes
        bytesToSendCount.CopyTo(fullBytes, 0);
    }

    //Converts the byte array to the data size and returns the result
    int frameByteArrayToByteLength(byte[] frameBytesLength)
    {
        int byteLength = BitConverter.ToInt32(frameBytesLength, 0);
        return byteLength;
    }


    /////////////////////////////////////////////////////Read Image SIZE from Server///////////////////////////////////////////////////
    private int readImageByteSize(int size)
    {
        NetworkStream serverStream = client.GetStream();
        byte[] imageBytesCount = new byte[size];
        var total = 0;
        do
        {
            var read = serverStream.Read(imageBytesCount, total, size - total);
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
            byteLength = frameByteArrayToByteLength(imageBytesCount);
        }
        return byteLength;
    }

    /////////////////////////////////////////////////////Read Image Data Byte Array from Server///////////////////////////////////////////////////
    private void readFrameByteArray(int size)
    {
        NetworkStream serverStream = client.GetStream();
        byte[] imageBytesInFunc = new byte[size];
        var total = 0;
        do
        {
            var read = serverStream.Read(imageBytesInFunc, total, size - total);
            //Debug.LogFormat("Client recieved {0} bytes", total);
            if (read == 0)
            {
                disconnected = true;
                break;
            }
            total += read;
        } while (total != size);

        //Debug.LogFormat("Finished receiving");

        imageBytes = imageBytesInFunc;

        displayImage = true;

        //Wait until old Image is displayed
        while (!readyToReadAgain)
        {
            //Debug.LogFormat("Waiting");
            //System.Threading.Thread.Sleep(1);
        }
    }


    void displayReceivedImage(byte[] receivedImageBytes)
    {
        tex.LoadImage(receivedImageBytes);
        //tex.Resize(tex.width * 2, tex.height * 2);
        image.texture = tex;
        readyToReadAgain = true;
    }


    // Update is called once per frame
    void Update()
    {
        if (!disconnected)
        {
            //Display Image
            if (displayImage)
            {
                displayImage = false;
                displayReceivedImage(imageBytes);
            }
        }

        if (Input.GetMouseButton(0))
        {
            pos = Input.mousePosition;
            Debug.Log("Touch pos: " + pos);
            canGetInput = true;
        }

        /*if (Input.touchCount < 1)
        {
            canGetInput = false;
            return;
        }*/
    }


    void LOG(string messsage)
    {
        if (enableLog)
            Debug.Log(messsage);
    }

    void LOGWARNING(string messsage)
    {
        if (enableLog)
            Debug.LogWarning(messsage);
    }

    void OnApplicationQuit()
    {
        LOGWARNING("OnApplicationQuit");
        stop = true;

        if (client != null)
        {
            client.Close();
        }
        if (input != null)
        {
            input.Close();
        }
    }
}