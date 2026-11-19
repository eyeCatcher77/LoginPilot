# LoginPilot

**Proxy, VPN & RDP Manager for Windows**

Lightweight Windows tray tool for automatic proxy switching, VPN launch and RDP session management. Designed for office/home office environments with Microsoft infrastructure. Portable, no installation required, config via XML. The easiest way to connect to your RDP Servers.

---

## Features

- **Auto-detection** – detects the current network (office/home office) by subnet and switches proxy settings automatically
- **Proxy management** – enables/disables Windows proxy via registry (HKCU), no admin rights required
- **VPN launch** – starts OpenVPN GUI and waits for connection before launching RDP
- **RDP launch** – starts the correct `.rdp` file depending on location (office vs. home office)
- **VPN profile check** – before connecting, verifies that the OpenVPN profile exists exactly once (global and user config folders, including subfolders)
- **Tunnel check** – verifies RDP host reachability on port 3389 before connecting; if it fails, a dialog offers "connect anyway"
- **Tray icon** – color-coded status indicator (green = office, blue = VPN, orange = no connection)
- **Auto-update** – checks for new versions in the background, verifies the download (SHA-256) and updates silently
- **Log file** – `loginpilot.log` records connection steps and update checks for troubleshooting
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

LoginPilot compares its own version against `version.txt` in this repository (`System.Version` comparison, so `3.10` is newer than `3.9`).

- **Schedule** – first check 20 s after start; every 4 h after a successful check, every 5 min after a failed one; additionally 30 s after resume from standby or a network change
- **Proxy-aware** – downloads via the system proxy with default credentials first, then directly without proxy
- **Verified** – the new EXE is only installed if its SHA-256 matches `LoginPilot.exe.sha256`; a missing or wrong hash means no replacement
- **Safe** – write access to the EXE folder is tested first; the old EXE is kept as `.bak` and restored if the swap fails; at most one download attempt per run
- **No interruption** – the restart waits until a running connection attempt has finished; the new instance shows a tray notice
- **Silent** – no popups; only a tray notice if the update cannot be installed for lack of write access

To publish a new version:
1. Compile the new EXE
2. Create `LoginPilot.exe.sha256` containing only the 64 lowercase hex characters of the EXE's SHA-256 (e.g. `certutil -hashfile LoginPilot.exe SHA256`)
3. Push EXE and hash file first, then update `version.txt` (plain number, e.g. `3.5`) – clients update on their next check

Command-line options for testing and administration:

| Option | Description |
|--------|-------------|
| `/updatecheck` | Runs exactly one update check including the swap, without window or restart. Exit code: 0 = up to date, 10 = updated, 20 = check failed, 30 = no write access, 40 = hash wrong or missing |
| `/selftest` | Checks the internal logic offline with dummy data; results in `%TEMP%\loginpilot-selftest.txt`, exit code = number of failures |

For local tests, the environment variable `LOGINPILOT_UPDATE_URL` can point to a test server. It is only honoured if it starts with `http://localhost:` or `http://127.0.0.1:`; any other value is ignored and logged.

---

## Log File

LoginPilot writes `loginpilot.log` next to the EXE. If that folder is not writable, it uses `%LOCALAPPDATA%\LoginPilot\loginpilot.log`.

- Line format: `YYYY-MM-DD HH:MM:SS [Area] Text`
- Logged: program start, every step of a connection attempt (VPN profile check, VPN start, tunnel checks, RDP start, cancel/timeout), resume and network changes, every update step including error messages
- Never logged: PINs, passwords, contents of `.ovpn` files
- Above 300 KB the file is renamed to `loginpilot.log.1` (one generation is kept)
- Open it via the tray menu entry **Protokoll öffnen**

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
LoginPilot.exe.sha256              ← SHA-256 of the EXE (checked by the auto-update)
loginpilot.log                     ← Log file (created at runtime, not in repo)
version.txt                        ← Current version number (e.g. 3.5)
```

---

## License

MIT License – see [LICENSE](LICENSE) for details.

Copyright (c) 2026 Stefan Diller