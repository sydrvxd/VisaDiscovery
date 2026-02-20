using Ivi.Visa;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

public class VisaService
{
    private const int TimeoutMs = 3000;

    public IEnumerable<string> FindResources(string pattern = "?*INSTR")
    {
        try
        {
            var rm = GlobalResourceManager.Open();
            var resources = rm.Find(pattern);
            return resources;
        }
        catch (Exception)
        {
            return Enumerable.Empty<string>();
        }
    }

    public IEnumerable<string> FindAllResources()
    {
        var patterns = new[]
        {
            "GPIB?*INSTR",
            "USB?*INSTR",
            "TCPIP?*INSTR",
            "ASRL?*INSTR",
            "PXI?*INSTR",
            "VXI?*INSTR",
            "GPIB?*SOCKET",
            "TCPIP?*SOCKET"
        };

        var allResources = new HashSet<string>();

        // Try broad pattern first
        foreach (var resource in FindResources("?*INSTR"))
            allResources.Add(resource);

        foreach (var resource in FindResources("?*SOCKET"))
            allResources.Add(resource);

        // Then try specific patterns as fallback
        foreach (var pattern in patterns)
        {
            foreach (var resource in FindResources(pattern))
                allResources.Add(resource);
        }

        return allResources;
    }

    public async Task<InstrumentInfo> QueryInstrumentAsync(string resourceName)
    {
        return await Task.Run(() => QueryInstrument(resourceName));
    }

    public InstrumentInfo QueryInstrument(string resourceName)
    {
        try
        {
            using var session = GlobalResourceManager.Open(resourceName, AccessMode.ExclusiveLock, TimeoutMs) as IMessageBasedSession;
            if (session == null)
                return InstrumentInfo.CreateError(resourceName, "Not a message-based instrument");

            session.TimeoutMilliseconds = TimeoutMs;
            session.FormattedIO.WriteLine("*IDN?");
            var response = session.FormattedIO.ReadLine();

            return InstrumentInfo.FromIdnResponse(resourceName, response);
        }
        catch (Exception ex)
        {
            return InstrumentInfo.CreateError(resourceName, ex.Message);
        }
    }

    public async Task<string> SendCommandAsync(string resourceName, string command)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var session = GlobalResourceManager.Open(resourceName, AccessMode.ExclusiveLock, TimeoutMs) as IMessageBasedSession;
                if (session == null)
                    return "Error: Not a message-based instrument";

                session.TimeoutMilliseconds = TimeoutMs;

                if (command.TrimEnd().EndsWith("?"))
                {
                    session.FormattedIO.WriteLine(command);
                    return session.FormattedIO.ReadLine();
                }
                else
                {
                    session.FormattedIO.WriteLine(command);
                    return "OK";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }

    public bool IsVisaAvailable()
    {
        try
        {
            GlobalResourceManager.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
