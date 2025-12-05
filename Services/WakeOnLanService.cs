

namespace BootLauncherLite.Services
{
    public class WakeOnLanService
    {
        public void SendMagicPacket(string macAddress, int port = 9)
        {
            var macBytes = ParseMac(macAddress);
            if (macBytes.Length != 6)
                throw new ArgumentException("Invalid MAC address", nameof(macAddress));

            var packet = new byte[6 + 16 * 6];

            // 6 x 0xFF
            for (int i = 0; i < 6; i++)
                packet[i] = 0xFF;

            // 16 repetitions of MAC
            for (int i = 0; i < 16; i++)
                Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);

            using var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, port));
        }

        private static byte[] ParseMac(string mac)
        {
            string[] parts = mac.Split(':', '-', ' ');
            if (parts.Length < 6)
                throw new ArgumentException("Invalid MAC address", nameof(mac));

            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
                bytes[i] = Convert.ToByte(parts[i], 16);

            return bytes;
        }
    }
}

