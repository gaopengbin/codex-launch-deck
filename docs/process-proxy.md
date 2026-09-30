# Launch Deck enhanced process proxy (Windows x64)

The `LaunchDeck-Enhanced-win-x64.zip` release contains the C# Launch Deck with one optional enhanced proxy checkbox. It supplements the existing HTTP/environment proxy setting for the desktop client's Node WebSocket connections, including cloud task navigation. The Tauri NSIS/MSI application does not yet have this checkbox; its regular proxy environment fix is included separately.

## Install and use

1. Download the Enhanced ZIP and its `.sha256` file from the same GitHub release. Compare `Get-FileHash -Algorithm SHA256` with the published checksum, then extract the ZIP. The package includes file checksums, source and licenses.
2. Run `Install-Enhanced.ps1` using 64-bit Windows PowerShell. It requests normal administrator approval and installs only its fixed, hash-pinned components under `C:\Program Files\LaunchDeckProcessProxy\<bundle-id>`. Do not disable execution policy, certificate checks or security controls to install it. A policy, signature, existing WinDivert service, proxy host or relay-port conflict stops installation. An existing version is never overwritten; no driver or client starts during installation. If it stops after creating files, retain them for inspection.
3. Start the installed `ChatGPTProxyLauncher.exe` as your normal user. Use an existing local **HTTP CONNECT** proxy such as `127.0.0.1:10808`; enable the enhanced checkbox and launch your registered ChatGPT/Codex package through Launch Deck. Approve normal UAC and the visible, cancellable scope confirmation. The same proxy endpoint is reused by the ordinary proxy settings and enhanced mode; this release does not support authenticated enhanced upstream proxies.
4. Wait for the enhanced state to become ready before opening the affected cloud task. Waiting approval and failure are distinct states. Do not start a second enhanced instance or repeatedly retry an exit/cleanup failure.

The WinDivert driver is loaded only after approval when an enhanced session starts. Launch Deck itself and the managed host are not claimed to have an Authenticode publisher signature; the bundled official WinDivert driver must validate against its pinned publisher certificate. Some Windows policies may prohibit this package; report that failure rather than bypassing them.

## Actual scope

The host validates the full EXE path against the currently registered OpenAI Windows package. It redirects **that selected executable's remote TCP port 443 connections** through the selected IPv4 loopback HTTP proxy. It does not implement strict domain-only selection, intercept TLS plaintext, edit ASAR, inject into the client, change system proxy/TUN/VPN settings, or configure autostart. Package updates can change the EXE path and require a new session.

WinDivert's packet filter observes TCP 443 and relay-port packets before process ownership and connection matching. Unmatched packets are reinjected unchanged. Other applications, loopback destinations, UDP and DNS are not redirected. The internal exclusive relay uses port 34010. Existing WinDivert/relay conflicts are refused rather than shared or taken over.

## Stop, rollback and limits

Uncheck enhanced mode or close its launcher to request host cleanup. Stopping can interrupt existing connections. A lost control pipe or failed native stop blocks new enhanced sessions; an owned host still running after the first wait remains observed rather than abandoned. Never force-kill another application's service or delete a driver to clear this state.

After host exit and driver/relay cleanup have been independently confirmed, return to the previous retained launcher version. Version directories are immutable and older versions are kept. Manual removal of a protected version needs administrator approval; it is not an automatic uninstall procedure, and this release does not automatically delete driver service registrations.

Real Windows validation confirmed engine readiness, a durable WebSocket connection and successful thread/read and thread/turns/list responses without TUN in the tested session, followed by the user's recovery report. **Real driver stop/unload and rollback have not yet been accepted on the machine** because the successful active connection was preserved. The isolated native tests exercise lifecycle and packet matching without loading WinDivert; they do not substitute for that remaining acceptance test. Newly rebuilt distribution binaries are from the same source but are not claimed to have undergone a new live forwarding test.

This fix addresses Launch Deck's proxy coverage for the desktop client. It does not claim to repair unrelated Windows native tool availability or all cloud permission/routing errors.

## Build and licensing

Run `tests/test-process-proxy.ps1` and `tests/test-native-process-proxy.ps1` before `scripts/build-process-proxy.ps1`. Requirements: Windows x64, .NET Framework compiler, Microsoft C++ x64 tools and Python 3. Build output must be a fresh directory. Builds never load the production DLL/driver. Fixed dependency SHA256 values and driver certificate pins are checked; real hashes of the newly built native DLL are compiled into the launcher and host. The host pins the newly built launcher's hash.

The native MIT source is in `native/proxybridge`, forked from InterceptSuite/ProxyBridge commit `02703a0672a8b94011a4698368a392f7734c10dc`. The official WinDivert 2.2.2-A runtime is dynamically linked under its LGPLv3 option. Full licenses, modified ProxyBridge source and corresponding WinDivert v2.2.2 source accompany the Enhanced ZIP. To rebuild against a modified compatible LGPL library, rebuild the package/pins from source rather than replacing a protected runtime silently. Normal GitHub source archives contain the launcher, host, build scripts and tests.
