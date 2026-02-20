using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisaDiscovery.Models;
using VisaDiscovery.Services;

namespace VisaDiscovery.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TcpScpiService _tcpService = new();
    private readonly NetworkScanService _networkScan = new();
    private readonly SerialScanService _serialScan = new();
    private readonly MdnsDiscoveryService _mdnsScan = new();
    private readonly DynamicVisaService _visaService = new();
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommandCommand))]
    private InstrumentInfo? _selectedInstrument;

    [ObservableProperty]
    private string _statusText = "Ready — no VISA runtime required";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _commandText = "*IDN?";

    [ObservableProperty]
    private string _commandResult = string.Empty;

    [ObservableProperty]
    private string _subnetFilter = string.Empty;

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private bool _scanTcp = true;

    [ObservableProperty]
    private bool _scanMdns = true;

    [ObservableProperty]
    private bool _scanSerial = true;

    [ObservableProperty]
    private bool _scanVisa;

    [ObservableProperty]
    private bool _visaAvailable;

    [ObservableProperty]
    private string _visaInfo = string.Empty;

    [ObservableProperty]
    private string _manualHost = string.Empty;

    public ObservableCollection<InstrumentInfo> Instruments { get; } = new();
    public ObservableCollection<string> DetectedSubnets { get; } = new();

    public MainViewModel()
    {
        _networkScan.StatusUpdate += msg => Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _serialScan.StatusUpdate += msg => Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _mdnsScan.StatusUpdate += msg => Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _visaService.StatusUpdate += msg => Application.Current.Dispatcher.Invoke(() => StatusText = msg);

        // Try to load VISA runtime
        _visaService.Initialize();
        VisaAvailable = _visaService.IsAvailable;
        VisaInfo = _visaService.RuntimeInfo ?? "";
        ScanVisa = VisaAvailable; // auto-enable if available

        // Detect local subnets
        foreach (var (addr, prefix) in NetworkScanService.GetLocalSubnets())
        {
            var subnet = NetworkScanService.GetSubnetBase(addr);
            if (!DetectedSubnets.Contains(subnet))
                DetectedSubnets.Add(subnet);
        }

        if (DetectedSubnets.Count > 0)
            SubnetFilter = DetectedSubnets[0];

        var status = VisaAvailable
            ? $"Ready — VISA detected: {VisaInfo}"
            : "Ready — no VISA runtime (TCP/mDNS/Serial only)";
        StatusText = status;
    }

    private bool CanScan() => !IsScanning;
    private bool CanStopScan() => IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        Instruments.Clear();
        CommandResult = string.Empty;
        ScanProgress = 0;

        try
        {
            // 1. mDNS discovery
            if (ScanMdns)
            {
                StatusText = "mDNS: Discovering LXI instruments...";
                var mdnsResults = await _mdnsScan.DiscoverAsync(3000, ct);

                foreach (var info in mdnsResults)
                {
                    // Try to query *IDN? on discovered instruments
                    var idn = await _tcpService.TryIdentifyAsync(info.Address, info.Port > 0 ? info.Port : 5025);
                    if (idn != null)
                    {
                        var identified = InstrumentInfo.FromIdnResponse(info.Address, info.Port, InterfaceType.LxiMdns, idn);
                        identified.Hostname = info.Hostname;
                        Instruments.Add(identified);
                    }
                    else
                    {
                        Instruments.Add(info);
                    }
                }
            }

            ScanProgress = 10;

            // 2. VISA scan (GPIB, USB-TMC, PXI, etc.)
            if (ScanVisa && VisaAvailable)
            {
                StatusText = "VISA: Scanning for GPIB, USB, PXI instruments...";
                var visaResources = await Task.Run(() => _visaService.FindAllResources(), ct);

                StatusText = $"VISA: Found {visaResources.Count} resource(s). Querying...";

                foreach (var resource in visaResources)
                {
                    ct.ThrowIfCancellationRequested();
                    var info = await _visaService.QueryInstrumentAsync(resource);
                    info.VisaResource = resource;

                    // Set address from resource string for display
                    if (string.IsNullOrEmpty(info.Address) || info.Address == resource)
                        info.Address = resource;

                    Instruments.Add(info);
                }
            }

            ScanProgress = 30;

            // 3. TCP subnet scan
            if (ScanTcp && !string.IsNullOrWhiteSpace(SubnetFilter))
            {
                StatusText = $"TCP: Scanning {SubnetFilter}.0/24 for instruments...";
                var tcpResults = await _networkScan.ScanSubnetAsync(SubnetFilter, ct);

                foreach (var info in tcpResults)
                {
                    // Avoid duplicates from mDNS
                    if (!Instruments.Any(i => i.Address == info.Address && i.Port == info.Port))
                        Instruments.Add(info);
                }
            }

            ScanProgress = 80;

            // 4. Serial ports
            if (ScanSerial)
            {
                StatusText = "Serial: Probing COM ports...";
                var serialResults = await _serialScan.ScanSerialPortsAsync(ct);
                foreach (var info in serialResults)
                    Instruments.Add(info);
            }

            ScanProgress = 100;

            var connected = Instruments.Count(i => i.IsConnected);
            StatusText = $"Scan complete: {Instruments.Count} instrument(s) found, {connected} identified.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Scan cancelled. {Instruments.Count} instrument(s) found so far.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopScan))]
    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task AddManualAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualHost)) return;

        var parts = ManualHost.Split(':');
        var host = parts[0].Trim();
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 5025;

        StatusText = $"Connecting to {host}:{port}...";

        var idn = await _tcpService.TryIdentifyAsync(host, port);
        if (idn != null)
        {
            var info = InstrumentInfo.FromIdnResponse(host, port, InterfaceType.TcpRaw, idn);
            Instruments.Add(info);
            StatusText = $"Connected: {info.Manufacturer} {info.Model}";
        }
        else
        {
            var info = InstrumentInfo.CreateError(host, port, InterfaceType.TcpRaw, "No response to *IDN?");
            Instruments.Add(info);
            StatusText = $"No response from {host}:{port}";
        }
    }

    private bool CanSendCommand() => SelectedInstrument != null;

    [RelayCommand(CanExecute = nameof(CanSendCommand))]
    private async Task SendCommandAsync()
    {
        if (SelectedInstrument == null || string.IsNullOrWhiteSpace(CommandText)) return;

        StatusText = $"Sending '{CommandText}' to {SelectedInstrument.DisplayAddress}...";

        try
        {
            var iface = SelectedInstrument.Interface;

            if (iface is InterfaceType.VisaGpib or InterfaceType.VisaUsb or InterfaceType.VisaTcpip
                or InterfaceType.VisaPxi or InterfaceType.VisaOther
                && !string.IsNullOrEmpty(SelectedInstrument.VisaResource))
            {
                // Use VISA for VISA-discovered instruments
                CommandResult = await _visaService.SendCommandAsync(SelectedInstrument.VisaResource, CommandText);
            }
            else if (iface == InterfaceType.Serial)
            {
                CommandResult = await SerialScanService.SendCommandAsync(
                    SelectedInstrument.Address, SelectedInstrument.Port, CommandText);
            }
            else
            {
                // TCP/SCPI for network instruments
                if (CommandText.TrimEnd().EndsWith('?'))
                    CommandResult = await _tcpService.QueryAsync(SelectedInstrument.Address, SelectedInstrument.Port, CommandText);
                else
                {
                    await _tcpService.SendAsync(SelectedInstrument.Address, SelectedInstrument.Port, CommandText);
                    CommandResult = "OK";
                }
            }
            StatusText = "Command sent.";
        }
        catch (Exception ex)
        {
            CommandResult = $"Error: {ex.Message}";
            StatusText = "Command failed.";
        }
    }

    [RelayCommand]
    private void CopyAddress()
    {
        if (SelectedInstrument != null)
        {
            Clipboard.SetText(SelectedInstrument.DisplayAddress);
            StatusText = "Address copied.";
        }
    }

    [RelayCommand]
    private void ShowVisaDiagnostics()
    {
        var report = _visaService.GetDiagnosticReport();
        CommandResult = report;
        MessageBox.Show(report, "VISA Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void CopyIdn()
    {
        if (SelectedInstrument != null && !string.IsNullOrEmpty(SelectedInstrument.IdnResponse))
        {
            Clipboard.SetText(SelectedInstrument.IdnResponse);
            StatusText = "IDN response copied.";
        }
    }
}
