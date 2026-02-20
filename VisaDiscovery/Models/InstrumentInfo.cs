namespace VisaDiscovery.Models;

public class InstrumentInfo
{
    public string ResourceName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string IdnResponse { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsConnected { get; set; }

    public static InstrumentInfo FromIdnResponse(string resourceName, string idnResponse)
    {
        var info = new InstrumentInfo
        {
            ResourceName = resourceName,
            IdnResponse = idnResponse.Trim(),
            InterfaceType = GetInterfaceType(resourceName)
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

    public static InstrumentInfo CreateError(string resourceName, string error)
    {
        return new InstrumentInfo
        {
            ResourceName = resourceName,
            InterfaceType = GetInterfaceType(resourceName),
            Status = $"Error: {error}",
            IsConnected = false
        };
    }

    private static string GetInterfaceType(string resourceName)
    {
        if (resourceName.StartsWith("GPIB", StringComparison.OrdinalIgnoreCase)) return "GPIB";
        if (resourceName.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return "USB";
        if (resourceName.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase)) return "TCP/IP";
        if (resourceName.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase)) return "Serial";
        if (resourceName.StartsWith("PXI", StringComparison.OrdinalIgnoreCase)) return "PXI";
        if (resourceName.StartsWith("VXI", StringComparison.OrdinalIgnoreCase)) return "VXI";
        return "Unknown";
    }
}
