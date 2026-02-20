namespace VisaDiscovery.Models;

public enum InterfaceType
{
    TcpRaw,     // SCPI raw socket (port 5025)
    LxiVxi11,   // LXI/VXI-11 (port 111)
    LxiMdns,    // Discovered via mDNS
    HiSlip,     // HiSLIP (port 4880)
    Serial,     // COM port
    Unknown
}

public class InstrumentInfo
{
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string IdnResponse { get; set; } = string.Empty;
    public InterfaceType Interface { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string Hostname { get; set; } = string.Empty;

    public string DisplayAddress => Interface == InterfaceType.Serial
        ? Address
        : Port > 0 ? $"{Address}:{Port}" : Address;

    public string InterfaceLabel => Interface switch
    {
        InterfaceType.TcpRaw => "TCP/SCPI",
        InterfaceType.LxiVxi11 => "LXI/VXI-11",
        InterfaceType.LxiMdns => "LXI/mDNS",
        InterfaceType.HiSlip => "HiSLIP",
        InterfaceType.Serial => "Serial",
        _ => "Unknown"
    };

    public static InstrumentInfo FromIdnResponse(string address, int port, InterfaceType iface, string idnResponse)
    {
        var info = new InstrumentInfo
        {
            Address = address,
            Port = port,
            Interface = iface,
            IdnResponse = idnResponse.Trim(),
        };

        var parts = idnResponse.Trim().Split(',');
        if (parts.Length >= 1) info.Manufacturer = parts[0].Trim();
        if (parts.Length >= 2) info.Model = parts[1].Trim();
        if (parts.Length >= 3) info.SerialNumber = parts[2].Trim();
        if (parts.Length >= 4) info.FirmwareVersion = parts[3].Trim();

        info.Status = "Connected";
        info.IsConnected = true;
        return info;
    }

    public static InstrumentInfo CreateError(string address, int port, InterfaceType iface, string error)
    {
        return new InstrumentInfo
        {
            Address = address,
            Port = port,
            Interface = iface,
            Status = $"Error: {error}",
            IsConnected = false
        };
    }

    public static InstrumentInfo CreateDiscovered(string address, int port, InterfaceType iface, string hostname = "")
    {
        return new InstrumentInfo
        {
            Address = address,
            Port = port,
            Interface = iface,
            Hostname = hostname,
            Status = "Discovered (not queried)",
            IsConnected = false
        };
    }
}
