# FRPTray

**FRPTray** is a lightweight Windows system tray application for managing an [FRP](https://github.com/fatedier/frp) (`frpc`) tunnel.  
It provides a simple UI to start, stop, and monitor a local-to-remote ports forwarding tunnel, with automatic reconnection and optional startup settings.

---

## Features

- **Tray icon control**
  - Start/Stop tunnel from the context menu
  - Live connection status (gray = disconnected, green = connected)
  - Quick copy of the public tunnel URLs
  - Status window with latest logs
- **Automatic tunnel management**
  - Periodic health checks (process and remote port)
  - Auto-reconnect with backoff and jitter
  - Network change handling (pause/reconnect on disconnect/reconnect)
- **Customizable settings**
  - Local ports, remote ports, server address, server control port, and secure token (don't forget to change!)
  - Run on Windows startup
  - Start tunnel automatically on application launch
- **FRPC embedded**
  - `frpc.exe` is stored as an AES-encrypted resource and extracted at runtime
  - Automatic cleanup of temporary files on stop/exit
- **Windows Defender handling**
  - Optional prompt to add FRPC to Defender exclusions if blocked
- **Thread-safe UI updates**
  - All tray/menu updates are marshalled to the UI thread
- **Settings persistence**
  - Uses [`Bluegrams/SettingsProviders`](https://github.com/Bluegrams/SettingsProviders) for portable and registry-based settings storage

---

## Requirements

- **Windows 7 SP1** or later (tested on Windows 10/11)
- .NET Framework **4.8.1**
- Network access to the FRP server

---

## Installation

1. Place `FRPTray.exe` in any folder.
2. Launch the application — the tray icon will appear in the system tray.
3. Right-click the tray icon and open **Settings...** to configure:
   - Local port (1–65535)
   - Remote port (1–65535)
   - Server address (IP or hostname)
   - Server control port (default 7000)
   - Server secret token
   - Run on Windows startup (optional)
   - Start tunnel on run (optional)
4. Click **Start tunnel** to establish the connection.

---

## How it works

- **Configuration**  
  The application generates a temporary `frpc.ini` file based on your settings.
- **FRPC extraction**  
  The bundled encrypted `frpc.exe` is decrypted into a temporary file at runtime.
- **Monitoring**  
  A background timer checks both the FRPC process and remote TCP connectivity.  
  If either fails, the tunnel is restarted with exponential backoff.
- **Cleanup**  
  On stop or exit, temporary FRPC files are deleted.

---

## Build

1. Clone this repository:
   ```bash
   git clone https://github.com/sensboston/FRPTray
2. If you want to update frpc.exe (FPR client app for Windows), do the following:
   - temprary disable Windows Defender antivirus, to avoid false positive for the latest FPRC releases
   - download latest (or preferred) release from https://github.com/fatedier/frp/releases 
   - unpack frpc.exe to the FRPTray folder
   - run PowerShell script **encode.ps1**
   - you should have updated frpc.enc and frpc_keys.txt files
   - delete original frpc.exe
   - enable Windows Defender
3. Build solution in Visual Studio.
4. Enjoy!

Please note, if you're running FPRTray for the first time, it will ask you to create Windows Defender exception.


  
