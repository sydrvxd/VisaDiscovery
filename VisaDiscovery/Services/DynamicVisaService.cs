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

    public bool IsAvailable { get; private set; }
    public string? RuntimeInfo { get; private set; }
    public List<string> DiagnosticLog { get; } = [];

    public event Action<string>? StatusUpdate;

    /// <summary>
    /// Try to locate and load the IVI VISA assembly.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // Collect all candidate paths
        var candidates = GetCandidatePaths();
        DiagnosticLog.Add($"Found {candidates.Count} candidate path(s)");

        // Strategy 1: Try loading from GAC by various known names
        var gacNames = new[]
        {
            "Ivi.Visa, Culture=neutral, PublicKeyToken=a128c98f1d7717c1",
            "Ivi.Visa, Culture=neutral, PublicKeyToken=null",
            "Ivi.Visa"
        };

        foreach (var name in gacNames)
        {
            try
            {
                _visaAssembly = Assembly.Load(name);
                if (_visaAssembly != null)
                {
                    DiagnosticLog.Add($"Loaded from GAC: {name} → {_visaAssembly.Location}");
                    if (SetupTypes()) return;
                    _visaAssembly = null;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Add($"GAC '{name}': {ex.GetType().Name} — {ex.Message}");
            }
        }

        // Strategy 2: Try all candidate file paths
        foreach (var path in candidates)
        {
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    DiagnosticLog.Add($"Not found: {path}");
                    continue;
                }

                DiagnosticLog.Add($"Found DLL: {path}");
                _visaAssembly = Assembly.LoadFrom(path);

                if (_visaAssembly != null)
                {
                    DiagnosticLog.Add($"Loaded: {_visaAssembly.FullName}");
                    if (SetupTypes()) return;
                    _visaAssembly = null;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Add($"Load '{path}': {ex.GetType().Name} — {ex.Message}");
            }
        }

        // Strategy 3: Search entire IVI Foundation directory tree
        var searchRoots = new[]
        {
            @"C:\Program Files\IVI Foundation",
            @"C:\Program Files (x86)\IVI Foundation",
            @"C:\Program Files\National Instruments",
            @"C:\Program Files (x86)\National Instruments",
            @"C:\Program Files\Keysight\IO Libraries Suite",
        };

        foreach (var root in searchRoots)
        {
            try
            {
                if (!System.IO.Directory.Exists(root)) continue;

                var files = System.IO.Directory.GetFiles(root, "Ivi.Visa.dll", System.IO.SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (candidates.Contains(file)) continue; // already tried

                    try
                    {
                        DiagnosticLog.Add($"Deep search found: {file}");
                        _visaAssembly = Assembly.LoadFrom(file);
                        if (_visaAssembly != null)
                        {
                            DiagnosticLog.Add($"Loaded: {_visaAssembly.FullName}");
                            if (SetupTypes()) return;
                            _visaAssembly = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Add($"Load '{file}': {ex.GetType().Name} — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Add($"Search '{root}': {ex.Message}");
            }
        }

        IsAvailable = false;
        RuntimeInfo = "No compatible VISA assembly found. See diagnostic log for details.";
        DiagnosticLog.Add("--- VISA initialization failed ---");
    }

    private static List<string> GetCandidatePaths()
    {
        var paths = new List<string>();

        // IVI Foundation standard locations (.NET Standard / .NET Core compatible)
        var bases = new[]
        {
            @"C:\Program Files\IVI Foundation\VISA\Microsoft.NET",
            @"C:\Program Files (x86)\IVI Foundation\VISA\Microsoft.NET",
        };

        var subfolders = new[]
        {
            @"Framework64\Current",
            @"Framework32\Current",
            @"Framework64\v5.12.0",
            @"Framework64\v5.11.0",
            @"Framework64\v5.8.0",
            @"Framework32\v5.12.0",
            @"Framework32\v5.11.0",
            // .NET Standard versions (newer IVI shared components)
            @"netstandard2.0",
            @"net6.0",
            @"net8.0",
        };

        foreach (var b in bases)
        {
            foreach (var sub in subfolders)
            {
                paths.Add(System.IO.Path.Combine(b, sub, "Ivi.Visa.dll"));
            }
        }

        // NI-VISA specific paths
        paths.Add(@"C:\Program Files\National Instruments\Shared\NI-VISA\Ivi.Visa.dll");
        paths.Add(@"C:\Program Files\National Instruments\Shared\NI-VISA\.NET\Ivi.Visa.dll");
        paths.Add(@"C:\Program Files (x86)\National Instruments\Shared\NI-VISA\Ivi.Visa.dll");
        paths.Add(@"C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Ivi.Visa");

        // Keysight specific
        paths.Add(@"C:\Program Files\Keysight\IO Libraries Suite\Ivi.Visa.dll");
        paths.Add(@"C:\Program Files (x86)\Keysight\IO Libraries Suite\Ivi.Visa.dll");

        // R&S specific
        paths.Add(@"C:\Program Files\Rohde-Schwarz\RsVisa\Ivi.Visa.dll");

        return paths;
    }

    private bool SetupTypes()
    {
        // List all types for diagnostics
        try
        {
            var allTypes = _visaAssembly!.GetExportedTypes();
            var visaTypes = allTypes.Where(t => t.Namespace?.StartsWith("Ivi.Visa") == true).Select(t => t.FullName).ToList();
            DiagnosticLog.Add($"Exported Ivi.Visa types: {string.Join(", ", visaTypes.Take(10))}{(visaTypes.Count > 10 ? "..." : "")}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"Could not enumerate types: {ex.Message}");
        }

        _globalResourceManagerType = _visaAssembly!.GetType("Ivi.Visa.GlobalResourceManager");
        _accessModeType = _visaAssembly.GetType("Ivi.Visa.AccessMode");

        if (_globalResourceManagerType == null)
        {
            DiagnosticLog.Add("Type 'Ivi.Visa.GlobalResourceManager' not found in assembly");

            // Try to find any ResourceManager type
            try
            {
                var rmTypes = _visaAssembly.GetExportedTypes()
                    .Where(t => t.Name.Contains("ResourceManager", StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.FullName)
                    .ToList();
                DiagnosticLog.Add($"ResourceManager-like types: {string.Join(", ", rmTypes)}");
            }
            catch { }

            return false;
        }

        if (_accessModeType == null)
        {
            DiagnosticLog.Add("Type 'Ivi.Visa.AccessMode' not found — trying without exclusive lock");
        }
        else
        {
            try
            {
                _exclusiveLockValue = Enum.Parse(_accessModeType, "ExclusiveLock");
            }
            catch
            {
                // Try "None" as fallback
                try { _exclusiveLockValue = Enum.Parse(_accessModeType, "None"); }
                catch { _exclusiveLockValue = null; }
            }
        }

        // Test that VISA is actually functional — but don't require Open() to succeed
        // Just verify we can call Find()
        try
        {
            var findMethod = _globalResourceManagerType.GetMethod("Find",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string)], null);

            if (findMethod != null)
            {
                IsAvailable = true;
                var version = _visaAssembly.GetName().Version;
                RuntimeInfo = $"IVI VISA {version} ({_visaAssembly.Location})";
                DiagnosticLog.Add($"VISA ready: {RuntimeInfo}");
                return true;
            }

            DiagnosticLog.Add("Find() method not found on GlobalResourceManager");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"VISA functional test failed: {ex.GetType().Name} — {ex.InnerException?.Message ?? ex.Message}");
        }

        // Even if Find() isn't available, try Open() approach
        try
        {
            var openMethodNoArgs = _globalResourceManagerType.GetMethod("Open", Type.EmptyTypes);
            if (openMethodNoArgs != null)
            {
                IsAvailable = true;
                var version = _visaAssembly.GetName().Version;
                RuntimeInfo = $"IVI VISA {version} ({_visaAssembly.Location})";
                DiagnosticLog.Add($"VISA ready (Open method): {RuntimeInfo}");
                return true;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"Open() test: {ex.InnerException?.Message ?? ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Find VISA resources matching a pattern.
    /// </summary>
    public List<string> FindResources(string pattern = "?*INSTR")
    {
        if (!IsAvailable || _globalResourceManagerType == null) return [];

        try
        {
            var findMethod = _globalResourceManagerType.GetMethod("Find",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string)], null);

            if (findMethod == null)
            {
                // Alternative: Open resource manager, then call Find on the instance
                var openMethod = _globalResourceManagerType.GetMethod("Open", Type.EmptyTypes);
                if (openMethod == null) return [];

                var rm = openMethod.Invoke(null, null);
                if (rm == null) return [];

                try
                {
                    var instanceFind = rm.GetType().GetMethod("Find", [typeof(string)]);
                    var result = instanceFind?.Invoke(rm, [pattern]);
                    if (result is IEnumerable<string> resources)
                        return resources.ToList();
                }
                finally
                {
                    if (rm is IDisposable d) d.Dispose();
                }

                return [];
            }

            var staticResult = findMethod.Invoke(null, [pattern]);
            if (staticResult is IEnumerable<string> staticResources)
                return staticResources.ToList();

            return [];
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"FindResources('{pattern}'): {ex.InnerException?.Message ?? ex.Message}");
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
            try
            {
                foreach (var resource in FindResources(pattern))
                    all.Add(resource);
            }
            catch { /* skip failed patterns */ }
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
            object? session = OpenSession(resourceName);
            if (session == null)
                return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "Could not open session");

            try
            {
                var response = QuerySession(session, "*IDN?");
                if (response == null)
                    return InstrumentInfo.CreateError(resourceName, 0, GetVisaInterfaceType(resourceName), "No response");

                var iface = GetVisaInterfaceType(resourceName);
                return InstrumentInfo.FromIdnResponse(resourceName, 0, iface, response);
            }
            finally
            {
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
                var session = OpenSession(resourceName);
                if (session == null) return "Error: Could not open session";

                try
                {
                    if (command.TrimEnd().EndsWith('?'))
                    {
                        return QuerySession(session, command) ?? "Error: No response";
                    }
                    else
                    {
                        WriteSession(session, command);
                        return "OK";
                    }
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

    private object? OpenSession(string resourceName)
    {
        // Try: GlobalResourceManager.Open(resourceName, AccessMode.ExclusiveLock, 3000)
        if (_accessModeType != null && _exclusiveLockValue != null)
        {
            try
            {
                var openMethod = _globalResourceManagerType!.GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Static,
                    null, [typeof(string), _accessModeType, typeof(int)], null);

                if (openMethod != null)
                    return openMethod.Invoke(null, [resourceName, _exclusiveLockValue, 3000]);
            }
            catch { /* try simpler overload */ }
        }

        // Fallback: GlobalResourceManager.Open(resourceName)
        try
        {
            var openMethod = _globalResourceManagerType!.GetMethod("Open",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string)], null);

            return openMethod?.Invoke(null, [resourceName]);
        }
        catch
        {
            return null;
        }
    }

    private static string? QuerySession(object session, string command)
    {
        var sessionType = session.GetType();

        // Set timeout
        var timeoutProp = sessionType.GetProperty("TimeoutMilliseconds");
        timeoutProp?.SetValue(session, 3000);

        // Try FormattedIO approach first
        var formattedIoProp = sessionType.GetProperty("FormattedIO");
        var formattedIo = formattedIoProp?.GetValue(session);

        if (formattedIo != null)
        {
            var fioType = formattedIo.GetType();
            var writeMethod = fioType.GetMethod("WriteLine", [typeof(string)]);
            writeMethod?.Invoke(formattedIo, [command]);

            var readMethod = fioType.GetMethod("ReadLine", Type.EmptyTypes);
            return readMethod?.Invoke(formattedIo, null) as string;
        }

        // Fallback: try RawIO
        var rawIoProp = sessionType.GetProperty("RawIO");
        var rawIo = rawIoProp?.GetValue(session);

        if (rawIo != null)
        {
            var rawType = rawIo.GetType();
            var writeMethod = rawType.GetMethod("Write", [typeof(string)]);
            writeMethod?.Invoke(rawIo, [command + "\n"]);

            var readMethod = rawType.GetMethod("ReadString", Type.EmptyTypes)
                          ?? rawType.GetMethod("Read", Type.EmptyTypes);
            return readMethod?.Invoke(rawIo, null) as string;
        }

        return null;
    }

    private static void WriteSession(object session, string command)
    {
        var sessionType = session.GetType();

        var formattedIoProp = sessionType.GetProperty("FormattedIO");
        var formattedIo = formattedIoProp?.GetValue(session);

        if (formattedIo != null)
        {
            var fioType = formattedIo.GetType();
            var writeMethod = fioType.GetMethod("WriteLine", [typeof(string)]);
            writeMethod?.Invoke(formattedIo, [command]);
            return;
        }

        var rawIoProp = sessionType.GetProperty("RawIO");
        var rawIo = rawIoProp?.GetValue(session);

        if (rawIo != null)
        {
            var rawType = rawIo.GetType();
            var writeMethod = rawType.GetMethod("Write", [typeof(string)]);
            writeMethod?.Invoke(rawIo, [command + "\n"]);
        }
    }

    /// <summary>
    /// Get full diagnostic info as string (for troubleshooting).
    /// </summary>
    public string GetDiagnosticReport()
    {
        return $"VISA Available: {IsAvailable}\n" +
               $"Runtime: {RuntimeInfo}\n" +
               $"Assembly: {_visaAssembly?.FullName ?? "none"}\n" +
               $"Location: {_visaAssembly?.Location ?? "none"}\n" +
               $"GlobalResourceManager: {_globalResourceManagerType?.FullName ?? "not found"}\n" +
               $"AccessMode: {_accessModeType?.FullName ?? "not found"}\n\n" +
               $"--- Log ---\n{string.Join("\n", DiagnosticLog)}";
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
