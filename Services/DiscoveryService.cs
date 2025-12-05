using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BootLauncherLite.Services
{
    /// <summary>
    /// Simple LAN discovery via UDP broadcast.
    /// All instances of BootLauncher on the same subnet will see each other.
    /// </summary>
    public class DiscoveryService : IDisposable
    {
        private const int Port = 49525;
        private const string MagicHeader = "BOOTLAUNCHER_DISCOVERY";

        private readonly CancellationTokenSource _cts = new();
        private readonly string _machineName;
        private readonly Func<bool> _isMasterFunc;

        // Manual heartbeat toggle
        private volatile bool _heartbeatEnabled = true;

        /// <summary>
        /// name, ip, mode, mac, isSelf
        /// </summary>
        public event Action<string, string, string, string, bool>? NodeDiscovered;

        public DiscoveryService(Func<bool> isMasterFunc)
        {
            _machineName = Environment.MachineName;
            _isMasterFunc = isMasterFunc;
        }

        public void Start()
        {
            Task.Run(() => ListenLoopAsync(_cts.Token));
            Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Enable/disable periodic heartbeat broadcasts.
        /// </summary>
        public void SetHeartbeatEnabled(bool enabled)
        {
            _heartbeatEnabled = enabled;
        }

        /// <summary>
        /// Periodic "I'm here" broadcast every 5 seconds.
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_heartbeatEnabled)
                    {
                        await SendHeartbeatAsync(udp);
                    }
                }
                catch
                {
                    // ignore errors
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Manual "send my info immediately".
        /// </summary>
        public void ForceBroadcast()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.EnableBroadcast = true;
                    await SendHeartbeatAsync(udp);
                }
                catch
                {
                    // ignore
                }
            });
        }

        /// <summary>
        /// Creates and sends the heartbeat message.
        /// Protocol v3:
        ///   Magic|3|Name|Mode|PrimaryIP|MAC|ip1;ip2;ip3
        /// </summary>
        private async Task SendHeartbeatAsync(UdpClient udp)
        {
            string mode = _isMasterFunc() ? "Master" : "Slave";

            var ips = GetUsableIPv4Addresses();
            string primaryIp = ips.FirstOrDefault() ?? "0.0.0.0";

            string mac = GetPrimaryMacAddress(primaryIp) ?? "00-00-00-00-00-00";

            // if somehow no usable IPs, still send one placeholder so node exists
            if (ips.Count == 0)
                ips.Add(primaryIp);

            string ipList = string.Join(";", ips);

            // Magic|Version|Name|Mode|PrimaryIP|MAC|IP_LIST
            string payload = $"{MagicHeader}|3|{_machineName}|{mode}|{primaryIp}|{mac}|{ipList}";
            byte[] data = Encoding.UTF8.GetBytes(payload);

            var endpoint = new IPEndPoint(IPAddress.Broadcast, Port);
            await udp.SendAsync(data, data.Length, endpoint);
        }

        /// <summary>
        /// Listen for incoming discovery packets.
        /// </summary>
        private async Task ListenLoopAsync(CancellationToken ct)
        {
            using var udp = new UdpClient(Port);

            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    continue;
                }

                try
                {
                    string msg = Encoding.UTF8.GetString(result.Buffer);
                    var parts = msg.Split('|');
                    if (parts.Length < 5)
                        continue;
                    if (parts[0] != MagicHeader)
                        continue;

                    string version = parts[1];

                    // --- v3: Magic|3|Name|Mode|PrimaryIP|MAC|IP_LIST ---
                    if (version == "3")
                    {
                        if (parts.Length < 7)
                            continue;

                        string name = parts[2];
                        string mode = parts[3];
                        string primaryIp = parts[4];
                        string mac = parts[5];
                        string ipListRaw = parts[6];

                        bool isSelf = string.Equals(name, _machineName, StringComparison.OrdinalIgnoreCase);

                        var ips = new List<string>();

                        if (!string.IsNullOrWhiteSpace(ipListRaw))
                        {
                            ips.AddRange(
                                ipListRaw
                                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim())
                                    .Where(s => s.Length > 0));
                        }

                        if (ips.Count == 0 && !string.IsNullOrWhiteSpace(primaryIp))
                        {
                            ips.Add(primaryIp);
                        }

                        foreach (var ip in ips)
                        {
                            NodeDiscovered?.Invoke(name, ip, mode, mac, isSelf);
                        }
                    }
                    else
                    {
                        // --- v2 or older (backwards compatible) ---
                        // Existing format: Magic|2|Name|Mode|IP|MAC
                        if (parts.Length < 5)
                            continue;

                        string name = parts[2];
                        string mode = parts[3];
                        string ip = parts[4];
                        string mac = (parts.Length >= 6) ? parts[5] : "";

                        bool isSelf = string.Equals(name, _machineName, StringComparison.OrdinalIgnoreCase);

                        NodeDiscovered?.Invoke(name, ip, mode, mac, isSelf);
                    }
                }
                catch
                {
                    // ignore malformed
                }
            }
        }

        /// <summary>
        /// Returns ALL usable IPv4 addresses:
        /// - Adapter UP
        /// - Not Loopback / Tunnel
        /// - Not 127.x.x.x, 169.254.x.x, 0.x.x.x
        /// </summary>
        private static List<string> GetUsableIPv4Addresses()
        {
            var result = new List<string>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    var ipProps = ni.GetIPProperties();

                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        var ip = ua.Address.ToString();

                        // Skip loopback & link-local & zero
                        if (IPAddress.IsLoopback(ua.Address))
                            continue;
                        if (ip.StartsWith("169.254.")) // APIPA
                            continue;
                        if (ip.StartsWith("0."))
                            continue;

                        if (!result.Contains(ip))
                            result.Add(ip);
                    }
                }

                if (result.Count == 0)
                {
                    // Fallback: any IPv4 from host entry, but still filter obvious junk
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var a in host.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
                    {
                        var ip = a.ToString();
                        if (IPAddress.IsLoopback(a))
                            continue;
                        if (ip.StartsWith("169.254.") || ip.StartsWith("0."))
                            continue;

                        result.Add(ip);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return result;
        }

        /// <summary>
        /// Old helper still used by others: returns *one* IPv4 (first usable).
        /// Now built on top of GetUsableIPv4Addresses().
        /// </summary>
        private static string? GetLocalIPv4()
        {
            return GetUsableIPv4Addresses().FirstOrDefault();
        }

        public void RequestImmediateUpdate()
        {
            // For now this just reuses the manual broadcast helper
            ForceBroadcast();
        }

        /// <summary>
        /// Pick a MAC, preferring the NIC that owns 'lanIpOverride' when possible.
        /// </summary>
        private static string? GetPrimaryMacAddress(string? lanIpOverride = null)
        {
            try
            {
                var lanIp = lanIpOverride ?? GetLocalIPv4();

                var all = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic =>
                        nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

                NetworkInterface? chosen = null;

                if (!string.IsNullOrWhiteSpace(lanIp))
                {
                    chosen = all.FirstOrDefault(nic =>
                        nic.GetIPProperties().UnicastAddresses.Any(a =>
                            a.Address.AddressFamily == AddressFamily.InterNetwork &&
                            a.Address.ToString() == lanIp));
                }

                chosen ??= all.FirstOrDefault(nic =>
                    nic.GetIPProperties().UnicastAddresses.Any(a =>
                        a.Address.AddressFamily == AddressFamily.InterNetwork));

                if (chosen == null)
                    return null;

                var macBytes = chosen.GetPhysicalAddress().GetAddressBytes();
                if (macBytes == null || macBytes.Length == 0)
                    return null;

                return string.Join("-", macBytes.Select(b => b.ToString("X2")));
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}

