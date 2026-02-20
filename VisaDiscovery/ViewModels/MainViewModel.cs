using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisaDiscovery.Models;
using VisaDiscovery.Services;

namespace VisaDiscovery.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly VisaService _visaService = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommandCommand))]
    private InstrumentInfo? _selectedInstrument;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _commandText = "*IDN?";

    [ObservableProperty]
    private string _commandResult = string.Empty;

    [ObservableProperty]
    private bool _visaAvailable;

    public ObservableCollection<InstrumentInfo> Instruments { get; } = new();

    public MainViewModel()
    {
        VisaAvailable = _visaService.IsVisaAvailable();
        if (!VisaAvailable)
            StatusText = "⚠ No VISA runtime detected. Install NI-VISA, Keysight IO Libraries, or R&S VISA.";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning for instruments...";
        Instruments.Clear();
        CommandResult = string.Empty;

        try
        {
            var resources = await Task.Run(() => _visaService.FindAllResources().ToList());

            if (resources.Count == 0)
            {
                StatusText = "No VISA resources found.";
                IsScanning = false;
                return;
            }

            StatusText = $"Found {resources.Count} resource(s). Querying...";

            foreach (var resource in resources)
            {
                var info = await _visaService.QueryInstrumentAsync(resource);
                Instruments.Add(info);
            }

            var connected = Instruments.Count(i => i.IsConnected);
            StatusText = $"Scan complete: {Instruments.Count} resource(s), {connected} responding.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanSendCommand() => SelectedInstrument?.IsConnected == true;

    [RelayCommand(CanExecute = nameof(CanSendCommand))]
    private async Task SendCommandAsync()
    {
        if (SelectedInstrument == null || string.IsNullOrWhiteSpace(CommandText))
            return;

        StatusText = $"Sending '{CommandText}' to {SelectedInstrument.ResourceName}...";

        try
        {
            CommandResult = await _visaService.SendCommandAsync(SelectedInstrument.ResourceName, CommandText);
            StatusText = "Command sent.";
        }
        catch (Exception ex)
        {
            CommandResult = $"Error: {ex.Message}";
            StatusText = "Command failed.";
        }
    }

    [RelayCommand]
    private void CopyResourceName()
    {
        if (SelectedInstrument != null)
        {
            Clipboard.SetText(SelectedInstrument.ResourceName);
            StatusText = "Resource name copied to clipboard.";
        }
    }

    [RelayCommand]
    private void CopyIdnResponse()
    {
        if (SelectedInstrument != null && !string.IsNullOrEmpty(SelectedInstrument.IdnResponse))
        {
            Clipboard.SetText(SelectedInstrument.IdnResponse);
            StatusText = "IDN response copied to clipboard.";
        }
    }
}
