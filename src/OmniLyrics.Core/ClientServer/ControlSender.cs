using System.Net.Sockets;
using System.Text;

public static class ControlSender
{
    public static async Task SendAsync(string cmd)
    {
        using var udp = new UdpClient();
        byte[] data = Encoding.UTF8.GetBytes(cmd);
        await udp.SendAsync(data, data.Length, "127.0.0.1", 32651);
    }
}