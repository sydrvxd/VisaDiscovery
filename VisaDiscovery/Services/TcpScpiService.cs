using System.Net.Sockets;
using System.Text;

namespace VisaDiscovery.Services;

/// <summary>
/// Raw TCP SCPI communication — no VISA runtime needed.
/// Works with any instrument that supports SCPI over raw TCP socket (port 5025).
/// </summary>
public class TcpScpiService
{
    private const int DefaultPort = 5025;
    private const int TimeoutMs = 3000;

    public async Task<string> QueryAsync(string host, int port, string command)
    {
        using var client = new TcpClient();
        client.ReceiveTimeout = TimeoutMs;
        client.SendTimeout = TimeoutMs;

        var connectTask = client.ConnectAsync(host, port);
        if (await Task.WhenAny(connectTask, Task.Delay(TimeoutMs)) != connectTask)
            throw new TimeoutException($"Connection to {host}:{port} timed out");

        await connectTask; // propagate exceptions

        var stream = client.GetStream();
        var commandBytes = Encoding.ASCII.GetBytes(command.TrimEnd() + "\n");
        await stream.WriteAsync(commandBytes);

        // Read response
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        stream.ReadTimeout = TimeoutMs;

        try
        {
            int bytesRead;
            do
            {
                bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (bytesRead > 0)
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            } while (bytesRead == buffer.Length);
        }
        catch (IOException) { /* timeout reading — return what we have */ }

        return sb.ToString().TrimEnd();
    }

    public async Task SendAsync(string host, int port, string command)
    {
        using var client = new TcpClient();
        client.SendTimeout = TimeoutMs;

        var connectTask = client.ConnectAsync(host, port);
        if (await Task.WhenAny(connectTask, Task.Delay(TimeoutMs)) != connectTask)
            throw new TimeoutException($"Connection to {host}:{port} timed out");

        await connectTask;

        var stream = client.GetStream();
        var commandBytes = Encoding.ASCII.GetBytes(command.TrimEnd() + "\n");
        await stream.WriteAsync(commandBytes);
    }

    /// <summary>
    /// Check if an instrument is reachable on a given port.
    /// </summary>
    public async Task<bool> IsReachableAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(1500)) != connectTask)
                return false;
            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Try *IDN? on a host:port, return null if not reachable or no response.
    /// </summary>
    public async Task<string?> TryIdentifyAsync(string host, int port = DefaultPort)
    {
        try
        {
            var response = await QueryAsync(host, port, "*IDN?");
            return string.IsNullOrWhiteSpace(response) ? null : response;
        }
        catch
        {
            return null;
        }
    }
}
