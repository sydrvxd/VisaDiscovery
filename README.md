# VISA Discovery Tool

A simple WPF tool for discovering and identifying VISA-compatible lab instruments (oscilloscopes, multimeters, power supplies, etc.).

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![WPF](https://img.shields.io/badge/UI-WPF-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- 🔍 **Auto-scan** for VISA resources (GPIB, USB, TCP/IP, Serial, PXI, VXI)
- 📋 **Identify instruments** via `*IDN?` query (Manufacturer, Model, Serial, Firmware)
- 💬 **Send SCPI commands** to selected instruments
- 📎 **Copy** resource names and IDN responses to clipboard
- 🌙 **Dark theme** UI

## Prerequisites

A VISA runtime must be installed on your system:

| Runtime | Vendor | Download |
|---------|--------|----------|
| NI-VISA | National Instruments | [ni.com/visa](https://www.ni.com/en/support/downloads/drivers/download.ni-visa.html) |
| Keysight IO Libraries | Keysight | [keysight.com](https://www.keysight.com/find/iolib) |
| R&S VISA | Rohde & Schwarz | [rohde-schwarz.com](https://www.rohde-schwarz.com/applications/r-s-visa-application-note_56280-148812.html) |

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run --project VisaDiscovery
```

## Usage

1. Click **⟳ Scan** to discover all connected VISA instruments
2. Select an instrument from the list to see details
3. Type a SCPI command (e.g., `*IDN?`, `*RST`, `MEAS:VOLT?`) and click **Send**
4. Use the copy buttons to grab resource names or IDN responses

## Architecture

```
VisaDiscovery/
├── Models/
│   └── InstrumentInfo.cs      # Instrument data model
├── ViewModels/
│   └── MainViewModel.cs       # Main view logic (MVVM)
├── Views/
│   └── MainWindow.xaml(.cs)   # UI
├── Services/
│   └── VisaService.cs         # VISA communication layer
└── Converters/
    └── InverseBoolConverter.cs
```

- **MVVM** with CommunityToolkit.Mvvm
- **IVI VISA .NET** for instrument communication
- **.NET 8 WPF**

## License

MIT
