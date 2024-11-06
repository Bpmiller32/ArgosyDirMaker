using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server.Tester;

public class ControlPort
{
    private readonly Socket socket;
    private readonly ILogger logger;

    public ControlPort(ILogger logger, string ipAddress, int port)
    {
        IPEndPoint endPoint = new(IPAddress.Parse(ipAddress), port);
        socket = new(SocketType.Stream, ProtocolType.Tcp);

        socket.Connect(endPoint);
        this.logger = logger;

    }

    public void RequestDirectoryInfo()
    {
        byte[] socketMessage = [0, 0, 0, 38, 65, 80, 67, 84, 76, 65, 0, 0, 19, 154, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 43, 0, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 1];
        socket.Send(socketMessage);
    }

    public void RequestStatusAlerts()
    {
        byte[] socketMessage = [0, 0, 0, 38, 65, 80, 67, 84, 76, 65, 0, 0, 19, 155, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 3, 153, 0, 0, 0, 1, 0, 0, 0, 4, 0, 0, 0, 50];
        socket.Send(socketMessage);
    }

    public void RequestConfigChange(string configName)
    {
        byte[] headerBytes = [0, 0, 0, 0, 65, 80, 67, 84, 76, 65, 0, 0, 19, 165, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 17, 213, 0, 0, 0, 1, 0, 0, 0, 0];
        byte[] configBytes = Encoding.ASCII.GetBytes(configName);

        byte[] socketMessage = new byte[headerBytes.Length + configBytes.Length];

        for (int i = 0; i < headerBytes.Length; i++)
        {
            if (i == 3 || i == 37)
            {
                // Set special length bytes
                socketMessage[3] = (byte)(socketMessage.Length - 4);
                socketMessage[37] = (byte)configBytes.Length;
                continue;
            }

            socketMessage[i] = headerBytes[i];
        }

        for (int i = 0; i < configBytes.Length; i++)
        {
            socketMessage[headerBytes.Length + i] = configBytes[i];
        }

        socket.Send(socketMessage);
    }

    public async Task<bool> RecieveMessage()
    {
        Stack<int> messageStack = new();
        int dirCheck = 0;

        while (true)
        {
            if (socket.Available <= 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            SocketMessage socketMessage = new(socket);
            await socketMessage.ReadMessageHeader();
            await socketMessage.ReadAllDataSections();

            logger.LogDebug($"MessageType: {socketMessage.MessageType}");

            // 6011: Info, 6010 PosAck for directory info request, 6015 PosAck for everything else?, 6016 NegAck
            switch (socketMessage.MessageType)
            {
                case 6016:
                    logger.LogInformation("Unable to change to known working configuration, donglelist likely performing correctly");
                    return false;
                case 6015:
                    messageStack.Push(socketMessage.MessageType);
                    break;
                case 6004:
                    messageStack.Push(socketMessage.MessageType);
                    break;
                case 6011:
                    messageStack.Push(socketMessage.MessageType);
                    break;
                case 6010:
                    if (CheckDirectoryInfo(socketMessage))
                    {
                        return true;
                    }
                    break;
                default:
                    break;
            }

            if (messageStack.Count >= 4)
            {
                int[] messageArray = new int[messageStack.Count];
                messageStack.CopyTo(messageArray, 0);

                if (messageArray[0] == 6011 && messageArray[1] == 6011 && messageArray[2] == 6004 && messageArray[3] == 6015)
                {
                    RequestDirectoryInfo();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    dirCheck++;
                }
            }

            if (dirCheck >= 10)
            {
                throw new Exception("Unexpected responses from socket connection");
            }
        }
    }

    private bool CheckDirectoryInfo(SocketMessage socketMessage)
    {
        // Product populated with something
        if (!string.IsNullOrEmpty(socketMessage.DataSections[3]))
        {
            logger.LogWarning("Changed to known working configuration, dongle on list");
            return true;
        }

        return false;
    }
}
