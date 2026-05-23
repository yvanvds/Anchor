# FocusAgent (student agent)

Windows-only WinUI 3 desktop app that runs on each student's laptop during a
focus session. This folder is the scaffold only — the foreground hook, silent
Entra auth, SignalR listener, and MSIX packaging land in follow-up issues
(#24, #25, #26).

Design rationale lives in [focus-system-design.md](../focus-system-design.md)
§5 (student agent behaviour) and §5.3 (native interop layer).

## Layout

```
agent/
├── FocusAgent.sln
├── src/
│   ├── FocusAgent.App      — WinUI 3 desktop app, unpackaged, tray + hidden main window
│   ├── FocusAgent.Core     — DTOs (mirroring backend SignalR payloads), settings, log paths
│   └── FocusAgent.Native   — Win32 P/Invoke surface (placeholder; populated as hooks land)
└── tests/
    └── FocusAgent.Core.Tests — xUnit smoke tests for Core
```

The Native project is intentionally isolated so all Win32 interop has a single
boundary to audit and iterate against real device behaviour.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 1809 (build 17763) or newer for runtime; build host should be Windows 11
- Visual Studio 2022 17.10+ is convenient for XAML designer/debugging but not required

The first `dotnet restore` pulls Windows App SDK 1.7 and H.NotifyIcon.WinUI as
NuGet packages — no `dotnet workload` install is required for unpackaged WinUI 3
apps on .NET 10.

## Build

```powershell
cd agent
dotnet restore
dotnet build -c Debug -p:Platform=x64
```

## Run

```powershell
cd agent
dotnet run --project src/FocusAgent.App -c Debug -p:Platform=x64
```

The app starts hidden and places a tray icon. Right-click it for **Open**
(brings up the main window) and **Quit**. Only one instance can run at a time;
launching a second copy exits immediately.

`dotnet run` stays attached to the running process. Ctrl+C in the terminal
does **not** stop the app — WinUI 3 apps are WinExe with no console attached,
so the signal never reaches them. Use the tray Quit, or
`Stop-Process -Name FocusAgent.App` from another shell.

## Test

```powershell
cd agent
dotnet test
```

## Configuration

`src/FocusAgent.App/appsettings.json` carries:

| Key | Default | Notes |
| --- | --- | --- |
| `Backend:BaseUrl` | `http://localhost:5000` | Dev backend URL. Override per deploy. |
| `Backend:HubPath` | `/hubs/session` | Path of the backend SignalR hub. |

Logs roll daily into `%LOCALAPPDATA%\Anchor\FocusAgent\logs\focusagent-*.log`
(14-day retention) via Serilog, plus debug output during development.

## Next issues

- **#24** — Entra silent auth (WAM) and SignalR session listener
- **#25** — Foreground hook and minimize off-list apps
- **#26** — MSIX packaging for the student agent
