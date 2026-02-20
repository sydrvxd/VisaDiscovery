using System.Net;
using Makaretu.Dns;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

/// <summary>
/// Discovers LXI instruments via mDNS/DNS-SD (_lxi._tcp, _scpi-raw._tcp).
/// No VISA runtime needed — uses multicast DNS.
/// </summary>
public class MdnsDiscoveryService
{
    // Standard mDNS service types for instruments
    private static readonly string[] ServiceTypes =
    [
        "_lxi._tcp",
        "_scpi-raw._tcp",
        "_hislip._tcp",
        "_vxi-11._tcp"
    ];

    public event Action<string>? StatusUpdate;

    public async Task<List<InstrumentInfo>> DiscoverAsync(int durationMs = 5000, CancellationToken ct = default)
    {
        var results = new List<InstrumentInfo>();
        var seen = new HashSet<string>();

        using var mdns = new MulticastService();
        using var sd = new ServiceDiscovery(mdns);

        sd.ServiceInstanceDiscovered += (_, e) =>
        {
            var name = e.ServiceInstanceName.ToString();
            StatusUpdate?.Invoke($"mDNS: found service {name}");

            // Resolve the instance to get SRV/A records
            mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
            mdns.SendQuery(e.ServiceInstanceName, type: DnsType.A);
        };

        mdns.AnswerReceived += (_, e) =>
        {
            foreach (var record in e.Message.Answers.Concat(e.Message.AdditionalRecords))
            {
                if (record is SRVRecord srv)
                {
                    var key = $"{srv.Target}:{srv.Port}";
                    if (seen.Add(key))
                    {
                        var info = InstrumentInfo.CreateDiscovered(
                            srv.Target.ToString().TrimEnd('.'),
                            srv.Port,
                            InterfaceType.LxiMdns,
                            srv.Target.ToString().TrimEnd('.')
                        );
                        lock (results) results.Add(info);
                        StatusUpdate?.Invoke($"mDNS: {srv.Target}:{srv.Port}");
                    }
                }

                if (record is ARecord a)
                {
                    // Update existing results with resolved IP
                    var hostname = a.Name.ToString().TrimEnd('.');
                    lock (results)
                    {
                        foreach (var r in results.Where(r => r.Hostname == hostname && r.Address == hostname))
                        {
                            r.Address = a.Address.ToString();
                        }
                    }
                }
            }
        };

        mdns.Start();

        foreach (var serviceType in ServiceTypes)
        {
            sd.QueryServiceInstances(serviceType);
        }

        // Wait for responses
        try
        {
            await Task.Delay(durationMs, ct);
        }
        catch (TaskCanceledException) { }

        mdns.Stop();

        return results;
    }
}
