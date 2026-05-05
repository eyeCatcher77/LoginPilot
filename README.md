# LoginPilot

**Proxy, VPN & RDP Manager for Windows**

Lightweight Windows tray tool for automatic proxy switching, VPN launch and RDP session management. Designed for office/home office environments with Microsoft infrastructure. Portable, no installation required, config via XML. Reducing Clicks and complexity for login Attempts.

---

## Features

- **Auto-detection** – detects the current network (office/home office) by subnet and switches proxy settings automatically
- **Proxy management** – enables/disables Windows proxy via registry (HKCU), no admin rights required
- **VPN launch** – starts OpenVPN GUI and waits for connection before launching RDP
- **RDP launch** – starts the correct `.rdp` file depending on location (office vs. home office)
- **Tunnel check** – verifies RDP host reachability on port 3389 before connecting
- **Tray icon** – color-coded status indicator (green = office, blue = VPN, orange = no connection)
- **Auto-update** – checks for new version on startup and updates silently
- **Autostart** – installs as Scheduled Task (starts earlier than regular startup entries)
- **Dark/Light theme**
- **Portable** – no installation, config file sits next to the EXE

---

## Requirements

- Windows 10 / 11
- .NET Framework 4.0 or higher (included in Windows by default)
- OpenVPN GUI (optional, only if VPN feature is enabled)

---

## Installation

1. Download `LoginPilot.exe` from [Releases](../../releases/latest)
2. Place the EXE in a folder of your choice (e.g. `C:\MASTER\`)
3. Run `LoginPilot.exe` – on first start the settings dialog opens automatically
4. Configure your proxy profiles and optionally enable RDP/VPN features
5. Enable autostart in settings if desired

> **Portable:** The config file `loginpilot_config.xml` is created next to the EXE. No registry entries, no installation directory.

---

## Configuration

Settings are stored in `loginpilot_config.xml` next to the EXE.

Open settings via:
- Button in the main window
- Keyboard shortcut `#` in the main window
- Command line: `LoginPilot.exe /k`

> Locked configs (for managed deployments) can still be opened via `#` or `/k`.

### Proxy Profiles

Each profile defines a location:

| Field | Description |
|-------|-------------|
| Name | Display name (e.g. "Office Berlin", "Office Hamburg", "HomeOffice") |
| Proxy Server | Proxy hostname or IP |
| Port | Proxy port (default: 3128) |
| Exceptions | Semicolon-separated bypass list |
| Subnet Prefix | Used for auto-detection (e.g. `192.168.151.`) |
| RDP File | `.rdp` file for this location (optional) |
| HomeOffice | If checked: proxy is disabled for this profile |

### RDP File locations

LoginPilot searches for `.rdp` files in the following order:
1. Absolute path (if configured)
2. Next to the EXE
3. User desktop
4. Public desktop
5. `C:\MASTER\`

---

## Auto-Update

LoginPilot checks for updates on every start by comparing the local version against `version.txt` in this repository.

- **Silent** – no popups, no user interaction required
- **Safe** – old EXE is renamed to `.bak` before replacing
- **Offline-tolerant** – if GitHub is unreachable, the check is skipped silently

To publish a new version:
1. Compile the new EXE
2. Update `version.txt` (e.g. `3.2`)
3. Push both files to the repo – all clients update on next start

---

## Building from Source

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /target:winexe ^
  /win32icon:loginpilot_icon.ico ^
  /out:LoginPilot.exe ^
  LoginPilot.cs
```

Requires only the .NET Framework SDK (included with Windows). No Visual Studio needed.

---

## Security

LoginPilot is designed with a minimal attack surface:

- No internet connection (except GitHub update check)
- No credential storage
- No open ports or network listeners
- No admin rights required (except optional autostart for all users)
- No external DLLs – single file executable
- Proxy settings written to HKCU only (user scope, not system-wide)


---

## File Structure

```
LoginPilot.exe                     ← Main executable
LoginPilot.cs                      ← Source code
loginpilot_config.xml              ← Config (created on first run, not in repo)
loginpilot_icon.ico                ← Tray icon (optional)
logo.png                           ← Custom logo shown in main window (optional)
version.txt                        ← Current version number (e.g. 3.2)
```

---

## License

Internal tool. No warranty. Use at your own risk.
