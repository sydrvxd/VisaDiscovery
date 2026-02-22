using System.Runtime.InteropServices;
using System.Text;
using VisaDiscovery.Models;

namespace VisaDiscovery.Services;

/// <summary>
/// Native VISA service using P/Invoke to call visa32.dll/visa64.dll directly.
/// Works with NI-VISA, Keysight IO Libraries, R&amp;S VISA, or OpenVISA — any
/// implementation that provides the standard visa32/visa64 C API.
/// 
/// Unlike DynamicVisaService (which needs the .NET Ivi.Visa assembly),
/// this talks directly to the native DLL — no managed wrapper required.
/// </summary>
public class NativeVisaService
{
    private const int MAX_DESC = 256;
    private const int BUF_SIZE = 65536;

    // VISA status codes
    private const int VI_SUCCESS = 0;
    private const int VI_SUCCESS_TERM_CHAR = 0x3FFF0005;
    private const int VI_SUCCESS_MAX_CNT = 0x3FFF0006;
    private const int VI_ERROR_RSRC_NFOUND = unchecked((int)0xBFFF0011);
    private const int VI_ERROR_INV_EXPR = unchecked((int)0xBFFF0010);

    // Access modes
    private const uint VI_NO_LOCK = 0;
    private const uint VI_EXCLUSIVE_LOCK = 1;

    public bool IsAvailable { get; private set; }
    public string? RuntimeInfo { get; private set; }
    public List<string> DiagnosticLog { get; } = [];

    public event Action<string>? StatusUpdate;

    #region Native P/Invoke — visa32/visa64

    // The DLL name "visa32" works for both 32-bit and 64-bit on Windows:
    // - NI-VISA installs visa32.dll in both System32 (64-bit) and SysWOW64 (32-bit)
    // - Keysight IO Libraries does the same
    // - OpenVISA provides visa32.dll (32-bit) and visa64.dll (64-bit)
    //
    // For maximum compatibility we try visa64 first on 64-bit, then fall back to visa32.
    // On Linux, this maps to libvisa.so.

    private static class Visa64
    {
        private const string DLL = "visa64";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viOpenDefaultRM")]
        public static extern int viOpenDefaultRM(out uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viClose")]
        public static extern int viClose(uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viOpen")]
        public static extern int viOpen(uint sesn,
            [MarshalAs(UnmanagedType.LPStr)] string rsrcName,
            uint accessMode, uint openTimeout, out uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viFindRsrc")]
        public static extern int viFindRsrc(uint sesn,
            [MarshalAs(UnmanagedType.LPStr)] string expr,
            out uint findList, out uint retcnt,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viFindNext")]
        public static extern int viFindNext(uint findList,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viRead")]
        public static extern int viRead(uint vi, byte[] buf, uint count, out uint retCount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viWrite")]
        public static extern int viWrite(uint vi, byte[] buf, uint count, out uint retCount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viSetAttribute")]
        public static extern int viSetAttribute(uint vi, uint attribute, uint attrState);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viGetAttribute")]
        public static extern int viGetAttribute(uint vi, uint attribute, StringBuilder attrState);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viStatusDesc")]
        public static extern int viStatusDesc(uint vi, int status,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viClear")]
        public static extern int viClear(uint vi);
    }

    private static class Visa32
    {
        private const string DLL = "visa32";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viOpenDefaultRM")]
        public static extern int viOpenDefaultRM(out uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viClose")]
        public static extern int viClose(uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viOpen")]
        public static extern int viOpen(uint sesn,
            [MarshalAs(UnmanagedType.LPStr)] string rsrcName,
            uint accessMode, uint openTimeout, out uint vi);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viFindRsrc")]
        public static extern int viFindRsrc(uint sesn,
            [MarshalAs(UnmanagedType.LPStr)] string expr,
            out uint findList, out uint retcnt,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viFindNext")]
        public static extern int viFindNext(uint findList,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viRead")]
        public static extern int viRead(uint vi, byte[] buf, uint count, out uint retCount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viWrite")]
        public static extern int viWrite(uint vi, byte[] buf, uint count, out uint retCount);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viSetAttribute")]
        public static extern int viSetAttribute(uint vi, uint attribute, uint attrState);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viGetAttribute")]
        public static extern int viGetAttribute(uint vi, uint attribute, StringBuilder attrState);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viStatusDesc")]
        public static extern int viStatusDesc(uint vi, int status,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder desc);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "viClear")]
        public static extern int viClear(uint vi);
    }

    #endregion

    #region Dispatch — auto-detect visa64 vs visa32

    private enum VisaDll { None, Visa64, Visa32 }
    private VisaDll _activeDll = VisaDll.None;
    private uint _defaultRM;

    private int CallOpenDefaultRM(out uint vi)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viOpenDefaultRM(out vi),
            VisaDll.Visa32 => Visa32.viOpenDefaultRM(out vi),
            _ => throw new InvalidOperationException("No VISA DLL loaded")
        };
    }

    private int CallClose(uint vi)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viClose(vi),
            VisaDll.Visa32 => Visa32.viClose(vi),
            _ => -1
        };
    }

    private int CallOpen(uint sesn, string rsrc, uint mode, uint timeout, out uint vi)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viOpen(sesn, rsrc, mode, timeout, out vi),
            VisaDll.Visa32 => Visa32.viOpen(sesn, rsrc, mode, timeout, out vi),
            _ => throw new InvalidOperationException("No VISA DLL loaded")
        };
    }

    private int CallFindRsrc(uint sesn, string expr, out uint findList, out uint retcnt, StringBuilder desc)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viFindRsrc(sesn, expr, out findList, out retcnt, desc),
            VisaDll.Visa32 => Visa32.viFindRsrc(sesn, expr, out findList, out retcnt, desc),
            _ => throw new InvalidOperationException("No VISA DLL loaded")
        };
    }

    private int CallFindNext(uint findList, StringBuilder desc)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viFindNext(findList, desc),
            VisaDll.Visa32 => Visa32.viFindNext(findList, desc),
            _ => -1
        };
    }

    private int CallRead(uint vi, byte[] buf, uint count, out uint retCount)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viRead(vi, buf, count, out retCount),
            VisaDll.Visa32 => Visa32.viRead(vi, buf, count, out retCount),
            _ => throw new InvalidOperationException("No VISA DLL loaded")
        };
    }

    private int CallWrite(uint vi, byte[] buf, uint count, out uint retCount)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viWrite(vi, buf, count, out retCount),
            VisaDll.Visa32 => Visa32.viWrite(vi, buf, count, out retCount),
            _ => throw new InvalidOperationException("No VISA DLL loaded")
        };
    }

    private int CallSetAttribute(uint vi, uint attr, uint val)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viSetAttribute(vi, attr, val),
            VisaDll.Visa32 => Visa32.viSetAttribute(vi, attr, val),
            _ => -1
        };
    }

    private int CallClear(uint vi)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viClear(vi),
            VisaDll.Visa32 => Visa32.viClear(vi),
            _ => -1
        };
    }

    private int CallStatusDesc(uint vi, int status, StringBuilder desc)
    {
        return _activeDll switch
        {
            VisaDll.Visa64 => Visa64.viStatusDesc(vi, status, desc),
            VisaDll.Visa32 => Visa32.viStatusDesc(vi, status, desc),
            _ => -1
        };
    }

    #endregion

    /// <summary>
    /// Try to load the native VISA DLL and open the default resource manager.
    /// </summary>
    public void Initialize()
    {
        // Try visa64 first on 64-bit systems
        if (Environment.Is64BitProcess)
        {
            if (TryInitDll(VisaDll.Visa64, "visa64")) return;
            DiagnosticLog.Add("visa64 not found, trying visa32...");
        }

        // Fall back to visa32 (NI-VISA uses visa32.dll for both architectures)
        if (TryInitDll(VisaDll.Visa32, "visa32")) return;

        // On Linux, try libvisa.so via visa64 name mapping
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DiagnosticLog.Add("No native VISA DLL found on this system.");
        }

        IsAvailable = false;
        RuntimeInfo = "No native VISA library found (visa32/visa64). See diagnostic log.";
        DiagnosticLog.Add("--- Native VISA initialization failed ---");
    }

    private bool TryInitDll(VisaDll dll, string name)
    {
        try
        {
            _activeDll = dll;
            int status = CallOpenDefaultRM(out _defaultRM);

            if (status == VI_SUCCESS)
            {
                IsAvailable = true;
                RuntimeInfo = $"Native {name} (Resource Manager handle: {_defaultRM})";
                DiagnosticLog.Add($"Loaded {name} — viOpenDefaultRM() = {_defaultRM}");

                // Try to get implementation info
                try
                {
                    var desc = new StringBuilder(MAX_DESC);
                    CallStatusDesc(_defaultRM, VI_SUCCESS, desc);
                    DiagnosticLog.Add($"viStatusDesc: {desc}");
                }
                catch { /* optional */ }

                return true;
            }
            else
            {
                DiagnosticLog.Add($"{name}: viOpenDefaultRM() returned 0x{status:X8}");
                _activeDll = VisaDll.None;
                return false;
            }
        }
        catch (DllNotFoundException)
        {
            DiagnosticLog.Add($"{name}: DLL not found");
            _activeDll = VisaDll.None;
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            DiagnosticLog.Add($"{name}: Entry point missing — {ex.Message}");
            _activeDll = VisaDll.None;
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"{name}: {ex.GetType().Name} — {ex.Message}");
            _activeDll = VisaDll.None;
            return false;
        }
    }

    /// <summary>
    /// Find VISA resources matching a pattern via native viFindRsrc.
    /// </summary>
    public List<string> FindResources(string pattern = "?*INSTR")
    {
        var results = new List<string>();
        if (!IsAvailable) return results;

        try
        {
            var desc = new StringBuilder(MAX_DESC);
            int status = CallFindRsrc(_defaultRM, pattern, out uint findList, out uint count, desc);

            if (status == VI_ERROR_RSRC_NFOUND || status == VI_ERROR_INV_EXPR)
                return results;

            if (status != VI_SUCCESS)
            {
                DiagnosticLog.Add($"viFindRsrc('{pattern}'): 0x{status:X8}");
                return results;
            }

            results.Add(desc.ToString());

            for (uint i = 1; i < count; i++)
            {
                desc.Clear();
                status = CallFindNext(findList, desc);
                if (status != VI_SUCCESS) break;
                results.Add(desc.ToString());
            }

            CallClose(findList);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Add($"FindResources('{pattern}'): {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Find all VISA resources across all interface types.
    /// </summary>
    public List<string> FindAllResources()
    {
        var all = new HashSet<string>();

        string[] patterns = ["?*INSTR", "?*SOCKET", "GPIB?*INSTR", "USB?*INSTR",
                             "TCPIP?*INSTR", "ASRL?*INSTR", "PXI?*INSTR"];

        foreach (var pattern in patterns)
        {
            try
            {
                foreach (var resource in FindResources(pattern))
                    all.Add(resource);
            }
            catch { /* skip failed patterns */ }
        }

        return [.. all];
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
        if (!IsAvailable)
            return InstrumentInfo.CreateError(resourceName, 0, GetInterfaceType(resourceName), "Native VISA not available");

        try
        {
            int status = CallOpen(_defaultRM, resourceName, VI_NO_LOCK, 5000, out uint session);
            if (status != VI_SUCCESS)
                return InstrumentInfo.CreateError(resourceName, 0, GetInterfaceType(resourceName),
                    $"viOpen failed: 0x{status:X8}");

            try
            {
                // Set timeout to 3 seconds
                CallSetAttribute(session, 0x3FFF001A /* VI_ATTR_TMO_VALUE */, 3000);

                // Send *IDN?
                var cmd = Encoding.ASCII.GetBytes("*IDN?\n");
                status = CallWrite(session, cmd, (uint)cmd.Length, out _);

                if (status != VI_SUCCESS)
                    return InstrumentInfo.CreateError(resourceName, 0, GetInterfaceType(resourceName),
                        $"viWrite failed: 0x{status:X8}");

                // Read response
                var buf = new byte[BUF_SIZE];
                status = CallRead(session, buf, (uint)buf.Length, out uint retCount);

                if (status != VI_SUCCESS && status != VI_SUCCESS_TERM_CHAR && status != VI_SUCCESS_MAX_CNT)
                    return InstrumentInfo.CreateError(resourceName, 0, GetInterfaceType(resourceName),
                        $"viRead failed: 0x{status:X8}");

                var response = Encoding.ASCII.GetString(buf, 0, (int)retCount).TrimEnd('\n', '\r');
                var iface = GetInterfaceType(resourceName);
                return InstrumentInfo.FromIdnResponse(resourceName, 0, iface, response);
            }
            finally
            {
                CallClose(session);
            }
        }
        catch (Exception ex)
        {
            return InstrumentInfo.CreateError(resourceName, 0, GetInterfaceType(resourceName), ex.Message);
        }
    }

    /// <summary>
    /// Send a SCPI command via native VISA and optionally read response.
    /// </summary>
    public async Task<string> SendCommandAsync(string resourceName, string command)
    {
        return await Task.Run(() =>
        {
            if (!IsAvailable) return "Error: Native VISA not available";

            try
            {
                int status = CallOpen(_defaultRM, resourceName, VI_NO_LOCK, 5000, out uint session);
                if (status != VI_SUCCESS) return $"Error: viOpen failed (0x{status:X8})";

                try
                {
                    CallSetAttribute(session, 0x3FFF001A, 3000);

                    var cmdBytes = Encoding.ASCII.GetBytes(command.TrimEnd() + "\n");
                    status = CallWrite(session, cmdBytes, (uint)cmdBytes.Length, out _);
                    if (status != VI_SUCCESS) return $"Error: viWrite failed (0x{status:X8})";

                    if (command.TrimEnd().EndsWith('?'))
                    {
                        var buf = new byte[BUF_SIZE];
                        status = CallRead(session, buf, (uint)buf.Length, out uint retCount);

                        if (status != VI_SUCCESS && status != VI_SUCCESS_TERM_CHAR && status != VI_SUCCESS_MAX_CNT)
                            return $"Error: viRead failed (0x{status:X8})";

                        return Encoding.ASCII.GetString(buf, 0, (int)retCount).TrimEnd('\n', '\r');
                    }

                    return "OK";
                }
                finally
                {
                    CallClose(session);
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// Get diagnostic info for troubleshooting.
    /// </summary>
    public string GetDiagnosticReport()
    {
        return $"Native VISA Available: {IsAvailable}\n" +
               $"Active DLL: {_activeDll}\n" +
               $"Runtime: {RuntimeInfo}\n" +
               $"Resource Manager: {_defaultRM}\n" +
               $"Process: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}\n" +
               $"OS: {RuntimeInformation.OSDescription}\n\n" +
               $"--- Log ---\n{string.Join("\n", DiagnosticLog)}";
    }

    /// <summary>
    /// Close the default resource manager on dispose.
    /// </summary>
    public void Shutdown()
    {
        if (IsAvailable && _defaultRM != 0)
        {
            try { CallClose(_defaultRM); } catch { }
            _defaultRM = 0;
            IsAvailable = false;
        }
    }

    private static InterfaceType GetInterfaceType(string resourceName)
    {
        if (resourceName.StartsWith("GPIB", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaGpib;
        if (resourceName.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaUsb;
        if (resourceName.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaTcpip;
        if (resourceName.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase)) return InterfaceType.Serial;
        if (resourceName.StartsWith("PXI", StringComparison.OrdinalIgnoreCase)) return InterfaceType.VisaPxi;
        return InterfaceType.VisaOther;
    }
}
