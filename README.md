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
application. Launch Deck can also ask your locally authenticated Codex CLI to
create a custom theme from a written brief or reference image.

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
- CodeDrobe marketplace browsing, search, sorting, installation, download
  progress, integrity checking, and local cache
- Bundled `Hatsune Miku · Future Beats` theme for offline use
- Light/dark labels on theme cards and automatic synchronization of the native
  Codex appearance mode required by the selected theme
- Local Codex-powered custom theme generation with CSS-only, uploaded-reference,
  and AI-background modes
- Generated hero artwork, optional supporting textures, and theme-card covers
- Live theme application to a running Codex window without restarting it
- Managed theme watcher for renderer reloads
- Adaptive loopback CDP port selection to avoid stale `9335` conflicts
- Native appearance restoration and persistent watcher cleanup
- Hidden background commands, so theme and process operations do not flash
  console windows
- English and Simplified Chinese interface

## What's new in 2.1

- Create and cache complete `.codedrobe-theme` packages through the signed-in
  Codex CLI without configuring another model provider or API key.
- Use a local image as a visual reference; the generation workflow preserves
  important composition anchors instead of treating the file as a raw overlay.
- Choose the intended light or dark Codex base before generation. Theme cards
  display that requirement, and applying the theme switches Codex through its
  native appearance action.
- Follow generation and marketplace-download progress inside Launch Deck, with
  clearer failures and no native HTML number spinner in the proxy-port control.

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

If Codex is already running with a CDP port, select **Apply theme live** instead.
Launch Deck reads the theme's declared light/dark base and synchronizes the
running Codex window automatically. Restoring the native appearance returns the
managed Codex settings and removes the injected theme.

Launch Deck sets uppercase and lowercase forms of `HTTP_PROXY`, `HTTPS_PROXY`,
and `NO_PROXY` only for the launched process tree. It never changes the Windows
system proxy.

Theme features require [CodeDrobe](https://codedrobe.app). Launch Deck uses a
stable global `codedrobe` command when available and can fall back to `npx`.

> If your proxy application exposes a SOCKS port, use its HTTP or mixed port.
> This launcher supplies an HTTP proxy URL.

## Create a custom theme with Codex

This workflow reuses the Codex CLI already installed and signed in on the
computer. It does not require a separate API key or an embedded model service.

1. Open **AI custom theme** in Launch Deck.
2. Enter a visual brief and choose the required **Light** or **Dark** base.
3. Choose one visual mode:
   - **CSS only** for gradients, glass, borders, shadows, and motion without a
     bitmap background.
   - **Upload reference** to generate a coordinated theme and artwork from a
     local image.
   - **AI background** to let Codex create the hero artwork from the brief.
4. Start generation and follow the staged progress. Launch Deck validates the
   package, generates or hydrates its card cover, and adds it to the local theme
   library when complete.

The CodeDrobe theme skill is required for authoring. Launch Deck detects the
skill and can install it for the current user. Generated packages are stored in
`%LOCALAPPDATA%\CodexProxyLaunchDeck\themes` and can be applied like marketplace
themes.

## Using themes with Cockpit Tools

Launch Deck can apply or switch a theme for Codex started by
[Cockpit Tools](https://github.com/jlcodes99/cockpit-tools). The Codex process
must be started with a loopback Chromium DevTools port; otherwise CodeDrobe
cannot attach to its renderer after it is already running.

1. In Cockpit Tools, open **Codex** and select **Application Instances**.
2. Edit the **Default Instance**.
3. In **Custom launch arguments**, enter:

   ```text
   --remote-debugging-port=9335
   ```

4. Save the instance, fully exit every Codex window/process, then launch the
   default instance through Cockpit Tools.
5. Open Launch Deck and select **Apply theme live** to apply or switch themes.

The selected theme's native light/dark base is synchronized automatically; you
do not need to change Codex appearance settings by hand.

Do not put the argument in Cockpit Tools' **Settings > Codex startup path**:
that field is only for the executable path. If port `9335` is already in use,
choose a free port and use the same `--remote-debugging-port=<port>` form;
Launch Deck detects the port from the launched Codex process.

## Build

Build the recommended Tauri application:

```powershell
cd desktop
npm ci
npm run lint
npm run build
npm run tauri build
```

Rust checks:

```powershell
cd desktop\src-tauri
cargo test
cargo clippy --all-targets --all-features -- -D warnings
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
variables to the new Codex process. A reference image selected for AI theme
generation is passed only to the explicitly started Codex generation job; Launch
Deck does not upload it to its own service.

## License

MIT
