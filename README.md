# VISA Discovery Tool

A simple WPF tool for discovering lab instruments — **no VISA runtime required** (no NI-VISA, no Keysight IO Libraries, no R&S VISA).

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![WPF](https://img.shields.io/badge/UI-WPF-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![No VISA](https://img.shields.io/badge/VISA_Runtime-Not_Required-brightgreen)

## How It Works

Instead of relying on vendor-specific VISA runtimes, this tool communicates directly with instruments using:

| Method | Protocol | What it finds |
|--------|----------|---------------|
| **TCP/SCPI** | Raw socket on port 5025 | Any networked SCPI instrument |
| **HiSLIP** | Port 4880 | Modern LXI instruments |
| **mDNS/DNS-SD** | Multicast DNS | LXI instruments advertising `_lxi._tcp` |
| **Serial** | COM ports at common baud rates | RS-232/USB-Serial instruments |

## Features

- 🔍 **Subnet scan** — scans your local /24 network for instruments
- 📡 **mDNS discovery** — finds LXI instruments via Bonjour/mDNS
- 🔌 **Serial scan** — probes COM ports at common baud rates
- 📋 **`*IDN?` identification** — Manufacturer, Model, Serial, Firmware
- 💬 **SCPI command interface** — send any command to selected instruments
- ➕ **Manual add** — connect to a specific IP:port
- ⏹ **Cancellable scans** — stop anytime
- 🌙 **Dark theme** UI

## Prerequisites

- .NET 8 SDK (Windows)
- No VISA runtime needed!

## Build & Run

```bash
dotnet build
dotnet run --project VisaDiscovery
```

## Usage

1. Select which scan methods to use (TCP, mDNS, Serial)
2. Choose/edit the subnet to scan
3. Click **⟳ Scan All**
4. Or manually add an instrument via **IP:Port** → **+ Add**
5. Select an instrument → type SCPI command → **Send**

## Architecture

```
VisaDiscovery/
├── Models/
│   └── InstrumentInfo.cs          # Instrument data model
├── ViewModels/
│   └── MainViewModel.cs           # Main view logic (MVVM)
├── Views/
│   └── MainWindow.xaml(.cs)       # UI
├── Services/
│   ├── TcpScpiService.cs          # Raw TCP SCPI communication
│   ├── NetworkScanService.cs      # Subnet scanner (port 5025/4880/5555)
│   ├── SerialScanService.cs       # COM port scanner
│   └── MdnsDiscoveryService.cs    # mDNS/DNS-SD LXI discovery
└── Converters/
    └── InverseBoolConverter.cs
```

## Dependencies

| Package | Purpose |
|---------|---------|
| CommunityToolkit.Mvvm | MVVM framework |
| Makaretu.Dns.Multicast | mDNS/DNS-SD discovery |
| System.IO.Ports | Serial port communication |

## Known Instrument Ports

| Port | Protocol | Vendors |
|------|----------|---------|
| 5025 | SCPI raw socket | Keysight, R&S, Tektronix, Rigol, Siglent |
| 4880 | HiSLIP | Keysight, R&S |
| 5555 | SCPI raw socket | Some Rigol/Siglent models |

## License

MIT
