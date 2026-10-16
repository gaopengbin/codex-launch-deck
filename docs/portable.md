# LaunchDeck portable Windows launcher

Double-click `LaunchDeck.exe`. No installation wizard, shortcut creation,
uninstaller, autostart, or changes to the system proxy are needed. The single
EXE includes the packaged-app activation helper, themes, native components,
licenses and corresponding native sources. It validates its embedded file
checksums before extracting into the current user's local cache. Ordinary
proxy launch runs as the normal user and needs no administrator approval.

Enhanced proxy is optional. It additionally covers the client's native Node
WebSockets and the copied Codex `node_repl.exe` browser helper's remote TCP 443.
Helper rules use full paths under the current user's Codex runtime directory;
each helper must match the SHA256 of the registered package's original helper.
Other Node programs and local connections are excluded. The first use requests normal Windows administrator approval to
copy this version's embedded runtime to an immutable protected directory under
`C:\Program Files\LaunchDeckProcessProxy`. This is a persistent component cache;
the enhanced mode is not zero-footprint. It does not require running a separate
installer. The launcher preserves the selected proxy and theme and continues
the same launch from the protected runtime as the normal user. Each enhanced
session separately requests driver/traffic approval before loading the driver.

The portable bootstrap never edits or removes an existing version, driver
registration, old shortcut or permission setting. A prepared version is reused
only after protection and file identity checks pass. Invalid protected files
stop startup; ordinary users cannot replace an elevated host. Administrator
approval is still required to run the enhanced host. Cancelling any approval
leaves the client unstarted. The existing process-path scope, approval timeout,
pipe peer authentication and cleanup handling remain in force.

The native TLS repair sends an approved public ClientHello hostname to the
HTTP CONNECT proxy instead of a possibly poisoned local DNS address. The
launcher does not modify the client ASAR, system DNS, FlClash, TUN or VPN.
It activates the registered Windows package to preserve package identity.

## Browser read compatibility

This build passes `BROWSER_USE_DISABLE_API_MEMBERS=Tab.ax` with the registered
package's launch environment. Existing user and inherited restrictions are
merged, not replaced. The activation helper backs up and restores the exact
previous registry environment values, as it already does for proxy settings.
The running client retains the setting and forwards it to regenerated browser
tool configurations. Ordinary browser acquisition uses the existing
DOM/screenshot fallback instead of the stalled accessibility path. Native
computer access and website permission settings are not changed.

The browser helper performs network requests before reading page content.
On the diagnosed system these requests were connecting directly and waiting
about 21 seconds. Enhanced mode now routes the verified helper through the
selected proxy as well. This does not disable or bypass any browser checks.
Basic mode alone does not supply this additional process rule. No vendor
configuration watcher or vendor JavaScript patch is used.

The **Check / repair browser** dialog retains the earlier, manual configuration
repair and rollback tools. Automatic startup no longer rewrites that file or
waits ten seconds: live validation showed the application could overwrite the
flag later. Restart the client through this build to inherit the launch policy.
The launcher does not modify app ASAR or vendor JS code. New app versions still
need their environment-forwarding and fallback behavior verified.

`--verify-payload` checks embedded resources without extraction, driver loading
or starting ChatGPT. Build and isolated tests do not establish authenticated
cloud WebSocket recovery; that needs a real enhanced session on the client.
