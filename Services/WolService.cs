
using System.Text.RegularExpressions;


namespace BootLauncherLite.Services
{
    public class WolService
    {
        private static byte[] BuildMagicPacket(string mac)
        {
            // Accept formats like "01-23-45-67-89-AB", "01:23:45:67:89:AB", "0123456789AB"
            var clean = Regex.Replace(mac, @"[^0-9A-Fa-f]", "");
            if (clean.Length != 12)
                throw new ArgumentException($"Invalid MAC address: {mac}");

            var macBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                macBytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            }

            var packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++)
                packet[i] = 0xFF;

            for (int i = 6; i < packet.Length; i += 6)
                Buffer.BlockCopy(macBytes, 0, packet, i, 6);

            return packet;
        }

        public async Task<bool> WakeAsync(string mac, string? ipAddress = null,
                                          int retries = 3, int retryDelaySeconds = 5)
        {
            if (string.IsNullOrWhiteSpace(mac))
                throw new ArgumentException("MAC must not be empty.", nameof(mac));

            byte[] packet = BuildMagicPacket(mac);
            IPAddress broadcast = IPAddress.Parse("255.255.255.255");

            for (int attempt = 1; attempt <= retries; attempt++)
            {
                // Send broadcast
                using (var client = new UdpClient())
                {
                    client.EnableBroadcast = true;
                    await client.SendAsync(packet, packet.Length, new IPEndPoint(broadcast, 9));
                }

                // Optionally also send to a specific IP if provided
                if (!string.IsNullOrWhiteSpace(ipAddress) &&
                    IPAddress.TryParse(ipAddress, out var ip))
                {
                    using (var client = new UdpClient())
                    {
                        await client.SendAsync(packet, packet.Length, new IPEndPoint(ip, 9));
                    }
                }

                // Give it some time to respond
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));

                // If we have an IP, try pinging to see if it came up
                if (!string.IsNullOrWhiteSpace(ipAddress))
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ipAddress, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            return true; // machine is awake
                        }
                    }
                    catch
                    {
                        // ignore ping errors and continue retrying
                    }
                }
                else
                {
                    // No IP to ping? After first send, just assume success
                    if (attempt == 1)
                        return true;
                }
            }

            return false;
        }
    }
}

