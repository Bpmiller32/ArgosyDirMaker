using System.Net.Sockets;
using System.Text;

namespace Server.Tester;

public class SocketMessage
{
    public int MessageType { get; set; }
    public Dictionary<int, string> DataSections { get; set; } = new();

    private readonly Socket socket;
    private int remainingBytes;

    private int dataSectionType;
    private int dataSectionSize;

    public SocketMessage(Socket socket)
    {
        this.socket = socket;
    }

    public async Task ReadMessageHeader()
    {
        byte[] sizeBytes = new byte[4];
        byte[] signatureBytes = new byte[6];
        byte[] typeBytes = new byte[4];

        await RecieveFromSocket(sizeBytes);
        await RecieveFromSocket(signatureBytes);
        await RecieveFromSocket(typeBytes);

        MessageType = Utils.ConvertIntBytes(typeBytes);

        // Subtract the above header reading (6 from signature + 4 from type, messageSize excluded itself)
        remainingBytes = Utils.ConvertIntBytes(sizeBytes) - 10;
    }

    public async Task ReadAllDataSections()
    {
        while (remainingBytes > 0)
        {
            await ReadSection();
        }
    }

    private async Task ReadSection()
    {
        // Read section header
        byte[] typeBytes = new byte[4];
        byte[] sizeBytes = new byte[4];

        await RecieveFromSocket(typeBytes);
        await RecieveFromSocket(sizeBytes);

        dataSectionType = Utils.ConvertIntBytes(typeBytes);
        dataSectionSize = Utils.ConvertIntBytes(sizeBytes);

        // Read section value
        byte[] valueBytes = new byte[dataSectionSize];
        await RecieveFromSocket(valueBytes);
        DataSections.Add(dataSectionType, Encoding.UTF8.GetString(valueBytes));
    }

    private async Task RecieveFromSocket(byte[] bytes)
    {
        await socket.ReceiveAsync(bytes, SocketFlags.None);
        remainingBytes -= bytes.Length;
    }
}
