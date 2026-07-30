# Codex Proxy Launch Deck

<p align="center"><strong>English</strong> | <a href="README.zh-CN.md">简体中文</a></p>

<p align="center">
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases/latest"><img src="https://img.shields.io/github/v/release/gaopengbin/chatgpt-proxy-launcher" alt="Latest release"></a>
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases"><img src="https://img.shields.io/github/downloads/gaopengbin/chatgpt-proxy-launcher/total" alt="Total downloads"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/gaopengbin/chatgpt-proxy-launcher" alt="MIT license"></a>
</p>

Launch Codex through a process-scoped HTTP proxy without changing the Windows
system proxy, then discover and apply CodeDrobe themes from the same desktop
application.

## Why

- Avoid repeated `Reconnecting... 1/5` to `5/5` delays when Codex needs an
  explicit proxy route on Windows.
- Keep proxy settings isolated to Codex and its child processes.
- Make Codex themes discoverable, installable, reversible, and available
  without hand-written commands.
- Keep a lightweight legacy launcher available for users who only need proxy
  startup.

## Launch Deck features

- Bold frameless desktop interface with working native drag, minimize, maximize,
  and close controls
- Automatic discovery of Microsoft Store/MSIX Codex installations
- Proxy connectivity check and accurate Codex process detection
- CodeDrobe marketplace browsing, search, sorting, installation, and local cache
- Bundled `Hatsune Miku · Future Beats` theme for offline use
- Live theme application to a running Codex window
- Managed theme watcher for renderer reloads
- Adaptive loopback CDP port selection to avoid stale `9335` conflicts
- Native appearance restoration
- English and Simplified Chinese interface

## Download

Download the recommended NSIS installer from the latest
[GitHub Release](https://github.com/gaopengbin/chatgpt-proxy-launcher/releases/latest):

- `Codex Proxy Launch Deck_*_x64-setup.exe`: recommended installer
- `Codex Proxy Launch Deck_*_x64_en-US.msi`: MSI installer
- `ChatGPTProxyLauncher.exe`: legacy lightweight launcher
- `ChatGPTProxyLauncher-Legacy-win-x64.zip`: legacy launcher with the bundled
  theme and shortcut helper

The public binaries are currently unsigned, so Windows SmartScreen may display
a warning.

## Usage

1. Start the HTTP or mixed port of your local proxy.
2. Open Launch Deck and enter the proxy host and port.
3. Select a theme, or leave theme mode disabled.
4. Select **Launch Codex**.

Launch Deck sets uppercase and lowercase forms of `HTTP_PROXY`, `HTTPS_PROXY`,
and `NO_PROXY` only for the launched process tree. It never changes the Windows
system proxy.

Theme features require [CodeDrobe](https://codedrobe.app). Launch Deck uses a
stable global `codedrobe` command when available and can fall back to `npx`.

> If your proxy application exposes a SOCKS port, use its HTTP or mixed port.
> This launcher supplies an HTTP proxy URL.

## Build

Build the recommended Tauri application:

```powershell
cd desktop
npm ci
npm run tauri build
```

Build the legacy .NET Framework launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

See [desktop/README.md](desktop/README.md) for development checks and desktop
requirements.

## Related upstream reports

- [Windows WebSocket transport works with explicit proxy environment variables](https://github.com/openai/codex/issues/29958)
- [Windows mobile remote control requires proxy environment variables](https://github.com/openai/codex/issues/29233)
- [WebSocket failures exhaust retries before HTTP fallback](https://github.com/openai/codex/issues/19821)
- [Windows Codex recovers with an explicit HTTP proxy](https://github.com/openai/codex/issues/20844)

## Privacy and security

The launcher does not forward or inspect network traffic. It only checks whether
the configured proxy endpoint accepts a connection and passes proxy environment
variables to the new Codex process.

## License

MIT
