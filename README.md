# IntLimiter

A Windows-first native desktop utility for monitoring and limiting internet bandwidth per application and globally. Built on .NET 8, WinUI 3, and Windows App SDK. Inspired by NetLimiter.

## Features (Target)
- Real-time global upload/download monitoring.
- Real-time per-process upload/download monitoring.
- Configurable bandwidth limits per application.
- Global bandwidth limits.
- Persisted rules across automatic restarts.
- Auto-updating via GitHub Releases (Velopack).
- Windows 11 Task Manager-inspired native aesthetic.

## Architecture
See `ARCHITECTURE.md` for full implementation details, boundaries, and low-level privilege requirements.

## Building from source
This project requires Visual Studio 2022 or the latest .NET 8 SDK.

```bash
git clone https://github.com/yourname/IntLimiter.git
cd IntLimiter
dotnet build IntLimiter.sln
```

## Running
Currently, the UI layer can be launched natively via WinUI 3 tools. Traffic limiting depending on WFP/QoS will require running the `IntLimiter.Service` explicitly or as an Administrator.

## Known Limitations
- Precise per-process network filtering natively requires kernel-mode (WFP Callout) capabilities. This project documents the boundary where such signed components must be deployed.
- Not a replacement for an enterprise firewall.

## License
MIT (or your chosen project license).
