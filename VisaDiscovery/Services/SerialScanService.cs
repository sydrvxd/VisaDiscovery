using System.IO.Ports;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

/// <summary>
/// Scans COM ports for SCPI instruments.
/// </summary>
public class SerialScanService
{
    private static readonly int[] CommonBaudRates = [9600, 19200, 38400, 57600, 115200];
    private const int TimeoutMs = 2000;

    public event Action<string>? StatusUpdate;

    public async Task<List<InstrumentInfo>> ScanSerialPortsAsync(CancellationToken ct = default)
    {
        var results = new List<InstrumentInfo>();
        var portNames = SerialPort.GetPortNames();

        foreach (var portName in portNames)
        {
            ct.ThrowIfCancellationRequested();
            StatusUpdate?.Invoke($"Probing {portName}...");

            foreach (var baud in CommonBaudRates)
            {
                var idn = await TrySerialIdnAsync(portName, baud, ct);
                if (idn != null)
                {
                    var info = InstrumentInfo.FromIdnResponse(portName, baud, InterfaceType.Serial, idn);
                    results.Add(info);
                    StatusUpdate?.Invoke($"Found: {info.Manufacturer} {info.Model} @ {portName} ({baud} baud)");
                    break; // found at this baud rate, skip others
                }
            }
        }

        return results;
    }

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public static async Task<string?> TrySerialIdnAsync(string portName, int baudRate, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = TimeoutMs,
                    WriteTimeout = TimeoutMs,
                    NewLine = "\n",
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                port.WriteLine("*IDN?");
                Thread.Sleep(500); // give instrument time to respond

                var response = port.ReadLine().Trim();
                return response.Contains(',') ? response : null;
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    public static async Task<string> SendCommandAsync(string portName, int baudRate, string command)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 3000,
                    WriteTimeout = 3000,
                    NewLine = "\n",
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.Open();
                port.DiscardInBuffer();
                port.WriteLine(command);

                if (command.TrimEnd().EndsWith('?'))
                {
                    Thread.Sleep(300);
                    return port.ReadLine().Trim();
                }

                return "OK";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }
}
