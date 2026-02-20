using System.Reflection;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

/// <summary>
/// Dynamically loads IVI VISA at runtime via reflection — no compile-time reference needed.
/// Works with any installed VISA runtime (NI-VISA, Keysight IO Libraries, R&S VISA).
/// If no VISA runtime is installed, IsAvailable returns false and all methods gracefully fail.
/// </summary>
public class DynamicVisaService
{
    private Assembly? _visaAssembly;
    private Type? _globalResourceManagerType;
    private Type? _accessModeType;
    private object? _exclusiveLockValue;
    private bool _initialized;

    // Known IVI VISA DLL locations
    private static readonly string[] VisaPaths =
    [
        @"C:\Program Files\IVI Foundation\VISA\Microsoft.NET\Framework64\Current\Ivi.Visa.dll",
        @"C:\Program Files (x86)\IVI Foundation\VISA\Microsoft.NET\Framework32\Current\Ivi.Visa.dll",
        @"C:\Program Files\IVI Foundation\VISA\Microsoft.NET\Framework64\v5.12.0\Ivi.Visa.dll",
        @"C:\Program Files\IVI Foundation\VISA\Microsoft.NET\Framework64\v5.11.0\Ivi.Visa.dll",
    ];

    public bool IsAvailable { get; private set; }
    public string? RuntimeInfo { get; private set; }

    public event Action<string>? StatusUpdate;

    /// <summary>
    /// Try to locate and load the IVI VISA assembly.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // Strategy 1: Try loading from GAC by name
        try
        {
            _visaAssembly = Assembly.Load("Ivi.Visa, Culture=neutral, PublicKeyToken=a128c98f1d7717c1");
            if (_visaAssembly != null)
            {
                SetupTypes();
                return;
            }
        }
        catch { /* not in GAC */ }

        // Strategy 2: Try known file paths
        foreach (var path in VisaPaths)
        {
            try
            {
                if (!System.IO.File.Exists(path)) continue;
                _visaAssembly = Assembly.LoadFrom(path);
                if (_visaAssembly != null)
                {
                    SetupTypes();
                    return;
                }
            }
            catch { /* try next */ }
        }

        // Strategy 3: Search IVI Foundation directory
        try
        {
            var iviRoot = @"C:\Program Files\IVI Foundation\VISA\Microsoft.NET";
            if (System.IO.Directory.Exists(iviRoot))
            {
                var files = System.IO.Directory.GetFiles(iviRoot, "Ivi.Visa.dll", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        _visaAssembly = Assembly.LoadFrom(file);
                        if (_visaAssembly != null)
                        {
                            SetupTypes();
                            return;
                        }
                    }
                    catch { /* try next */ }
                }
            }
        }
        catch { /* no IVI Foundation directory */ }

        IsAvailable = false;
        RuntimeInfo = "No VISA runtime found";
    }

    private void SetupTypes()
    {
        _globalResourceManagerType = _visaAssembly!.GetType("Ivi.Visa.GlobalResourceManager");
        _accessModeType = _visaAssembly.GetType("Ivi.Visa.AccessMode");

        if (_globalResourceManagerType == null || _accessModeType == null)
        {
            IsAvailable = false;
            RuntimeInfo = "VISA assembly loaded but API types not found";
            return;
        }

        _exclusiveLockValue = Enum.Parse(_accessModeType, "ExclusiveLock");

        // Test that VISA is actually functional
        try
        {
            var openMethod = _globalResourceManagerType.GetMethod("Open", Type.EmptyTypes);
            openMethod?.Invoke(null, null);
            IsAvailable = true;

            var version = _visaAssembly.GetName().Version;
            RuntimeInfo = $"IVI VISA {version} ({_visaAssembly.Location})";
        }
        catch (Exception ex)
        {
            // Assembly loaded but runtime not functional
            IsAvailable = false;
            var inner = ex.InnerException?.Message ?? ex.Message;
            RuntimeInfo = $"VISA assembly found but runtime error: {inner}";
        }
    }

    /// <summary>
    /// Find VISA resources matching a pattern.
    /// </summary>
    public List<string> FindResources(string pattern = "?*INSTR")
    {
        if (!IsAvailable || _globalResourceManagerType == null) return [];

        try
        {
            // GlobalResourceManager.Find(pattern)
            var findMethod = _globalResourceManagerType.GetMethod("Find",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string)], null);

            if (findMethod == null) return [];

            var result = findMethod.Invoke(null, [pattern]);
            if (result is IEnumerable<string> resources)
                return resources.ToList();

            return [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Find all VISA resources across all interface types.
    /// </summary>
    public List<string> FindAllResources()
    {
        var all = new HashSet<string>();

        var patterns = new[]
        {
            "?*INSTR",
            "?*SOCKET",
            "GPIB?*INSTR",
            "USB?*INSTR",
            "TCPIP?*INSTR",
            "ASRL?*INSTR",
            "PXI?*INSTR",
            "VXI?*INSTR"
        };

        foreach (var pattern in patterns)
        {
            foreach (var resource in FindResources(pattern))
                all.Add(resource);
        }

        return all.ToList();
    }

    /// <summary>
    /// Open a VISA session, send *IDN?, return the response.
    /// </summary>
    public async Task<InstrumentInfo> QueryInstrumentAsync(string resourceName)
    {
        return await Task.Run(() => QueryInstrument(resourceName));
    }

    public InstrumentInfo QueryInstrument(string resourceName)
    {
        if (!IsAvailable || _globalResourceManagerType == null)
            return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "VISA not available");

        try
        {
            // GlobalResourceManager.Open(resourceName, AccessMode.ExclusiveLock, 3000)
            var openMethod = _globalResourceManagerType.GetMethod("Open",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string), _accessModeType!, typeof(int)], null);

            if (openMethod == null)
                return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "Open method not found");

            var session = openMethod.Invoke(null, [resourceName, _exclusiveLockValue!, 3000]);
            if (session == null)
                return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "Session is null");

            try
            {
                // Check if it's IMessageBasedSession
                var sessionType = session.GetType();

                // Set timeout: session.TimeoutMilliseconds = 3000
                var timeoutProp = sessionType.GetProperty("TimeoutMilliseconds");
                timeoutProp?.SetValue(session, 3000);

                // Get FormattedIO
                var formattedIoProp = sessionType.GetProperty("FormattedIO");
                var formattedIo = formattedIoProp?.GetValue(session);

                if (formattedIo == null)
                    return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "Not a message-based session");

                var fioType = formattedIo.GetType();

                // FormattedIO.WriteLine("*IDN?")
                var writeMethod = fioType.GetMethod("WriteLine", [typeof(string)]);
                writeMethod?.Invoke(formattedIo, ["*IDN?"]);

                // FormattedIO.ReadLine()
                var readMethod = fioType.GetMethod("ReadLine", Type.EmptyTypes);
                var response = readMethod?.Invoke(formattedIo, null) as string ?? "";

                var iface = GetVisaInterfaceType(resourceName);
                return InstrumentInfo.FromIdnResponse(resourceName, 0, iface, response);
            }
            finally
            {
                // Dispose session
                if (session is IDisposable disposable)
                    disposable.Dispose();
            }
        }
        catch (TargetInvocationException ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), msg);
        }
        catch (Exception ex)
        {
            return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), ex.Message);
        }
    }

    /// <summary>
    /// Send a SCPI command via VISA and optionally read response.
    /// </summary>
    public async Task<string> SendCommandAsync(string resourceName, string command)
    {
        return await Task.Run(() =>
        {
            if (!IsAvailable || _globalResourceManagerType == null)
                return "Error: VISA not available";

            try
            {
                var openMethod = _globalResourceManagerType.GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Static,
                    null, [typeof(string), _accessModeType!, typeof(int)], null);

                var session = openMethod?.Invoke(null, [resourceName, _exclusiveLockValue!, 3000]);
                if (session == null) return "Error: Could not open session";

                try
                {
                    var sessionType = session.GetType();
                    var timeoutProp = sessionType.GetProperty("TimeoutMilliseconds");
                    timeoutProp?.SetValue(session, 3000);

                    var formattedIoProp = sessionType.GetProperty("FormattedIO");
                    var formattedIo = formattedIoProp?.GetValue(session);
                    if (formattedIo == null) return "Error: Not a message-based session";

                    var fioType = formattedIo.GetType();
                    var writeMethod = fioType.GetMethod("WriteLine", [typeof(string)]);
                    writeMethod?.Invoke(formattedIo, [command]);

                    if (command.TrimEnd().EndsWith('?'))
                    {
                        var readMethod = fioType.GetMethod("ReadLine", Type.EmptyTypes);
                        return readMethod?.Invoke(formattedIo, null) as string ?? "";
                    }

                    return "OK";
                }
                finally
                {
                    if (session is IDisposable disposable)
                        disposable.Dispose();
                }
            }
            catch (TargetInvocationException ex)
            {
                return $"Error: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }

    private static InterfaceType GetVisaInterfaceType(string resourceName)
    {
        if (resourceName.StartsWith("GPIB", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaGpib;
        if (resourceName.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaUsb;
        if (resourceName.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaTcpip;
        if (resourceName.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Serial;
        if (resourceName.StartsWith("PXI", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaPxi;
        if (resourceName.StartsWith("VXI", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaOther;
        return InterfaceType.VisaOther;
    }
}
