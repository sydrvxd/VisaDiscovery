using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

/// <summary>
/// Scans a subnet for instruments on known SCPI/LXI ports.
/// </summary>
public class NetworkScanService
{
    // Standard instrument ports
    private static readonly int[] InstrumentPorts = [5025, 4880, 80, 5555];
    // 5025 = SCPI raw socket (most common)
    // 4880 = HiSLIP
    // 80   = LXI web interface
    // 5555 = some Rigol/Siglent instruments

    private const int ScanTimeoutMs = 800;
    private const int MaxParallel = 50;

    public event Action<string>? StatusUpdate;

    /// <summary>
    /// Get local subnet info for scanning.
    /// </summary>
    public static List<(string BaseAddress, int PrefixLength)> GetLocalSubnets()
    {
        var subnets = new List<(string, int)>();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (iface.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;

            var props = iface.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var ip = addr.Address.ToString();
                var prefix = addr.PrefixLength;
                if (prefix is >= 16 and <= 24)
                    subnets.Add((ip, prefix));
            }
        }

        return subnets;
    }

    /// <summary>
    /// Get the base address (e.g., "192.168.1") from an IP.
    /// </summary>
    public static string GetSubnetBase(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : ip;
    }

    /// <summary>
    /// Scan a /24 subnet for instruments on known ports.
    /// </summary>
    public async Task<List<InstrumentInfo>> ScanSubnetAsync(string subnetBase, CancellationToken ct = default)
    {
        var results = new List<InstrumentInfo>();
        var tcpService = new TcpScpiService();
        var semaphore = new SemaphoreSlim(MaxParallel);

        var tasks = new List<Task>();

        for (int i = 1; i <= 254; i++)
        {
            var host = $"{subnetBase}.{i}";
            ct.ThrowIfCancellationRequested();

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    // Try SCPI raw socket first (most common)
                    var idn = await TryConnectAsync(host, 5025, ct);
                    if (idn != null)
                    {
                        var info = InstrumentInfo.FromIdnResponse(host, 5025, InterfaceType.TcpRaw, idn);
                        info.Hostname = await ResolveHostnameAsync(host);
                        lock (results) results.Add(info);
                        StatusUpdate?.Invoke($"Found: {info.Manufacturer} {info.Model} @ {host}:5025");
                        return;
                    }

                    // Try HiSLIP
                    idn = await TryConnectAsync(host, 4880, ct);
                    if (idn != null)
                    {
                        var info = InstrumentInfo.FromIdnResponse(host, 4880, InterfaceType.HiSlip, idn);
                        info.Hostname = await ResolveHostnameAsync(host);
                        lock (results) results.Add(info);
                        StatusUpdate?.Invoke($"Found: {info.Manufacturer} {info.Model} @ {host}:4880");
                        return;
                    }

                    // Try alternate port 5555 (Rigol, Siglent)
                    idn = await TryConnectAsync(host, 5555, ct);
                    if (idn != null)
                    {
                        var info = InstrumentInfo.FromIdnResponse(host, 5555, InterfaceType.TcpRaw, idn);
                        info.Hostname = await ResolveHostnameAsync(host);
                        lock (results) results.Add(info);
                        StatusUpdate?.Invoke($"Found: {info.Manufacturer} {info.Model} @ {host}:5555");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Address).ToList();
    }

    private static async Task<string?> TryConnectAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var delayTask = Task.Delay(ScanTimeoutMs, ct);

            if (await Task.WhenAny(connectTask, delayTask) != connectTask)
                return null;

            await connectTask;

            if (!client.Connected) return null;

            var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;

            var cmd = System.Text.Encoding.ASCII.GetBytes("*IDN?\n");
            await stream.WriteAsync(cmd, ct);

            var buffer = new byte[4096];
            var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
            if (await Task.WhenAny(readTask, Task.Delay(2000, ct)) != readTask)
                return null;

            var bytesRead = await readTask;
            if (bytesRead == 0) return null;

            var response = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
            // Basic sanity: IDN response should have at least one comma
            return response.Contains(',') ? response : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ResolveHostnameAsync(string ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
