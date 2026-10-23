# LaunchDeck portable Windows launcher

Double-click `LaunchDeck.exe`. No installation wizard, shortcut creation,
uninstaller, autostart, or changes to the system proxy are needed. The single
EXE includes the packaged-app activation helper, native components,
licenses and corresponding native sources. It validates its embedded file
checksums before extracting into the current user's local cache. Ordinary
launcher UI runs as the normal user.

This portable edition always uses enhanced proxy; no mode checkbox or basic
proxy branch is shown. It has no themes, theme store, theme restore action,
CodeDrobe invocation or Node.js/npx requirement. Enter the proxy host and port,
then choose **Launch ChatGPT / Codex**. Enhanced proxy covers the client's native
WebSockets and the copied Codex `node_repl.exe` browser helper's remote TCP 443.
Helper rules use full paths under the current user's Codex runtime directory;
each helper must match the SHA256 of the registered package's original helper.
Other Node programs and local connections are excluded. The first use requests normal Windows administrator approval to
copy this version's embedded runtime to an immutable protected directory under
`C:\Program Files\LaunchDeckProcessProxy`. This is a persistent component cache;
the enhanced mode is not zero-footprint. It does not require running a separate
installer. The launcher preserves the selected proxy and language and continues
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

## Startup responsiveness and diagnostics

Slow launch checks and activation-helper waits run away from the UI thread.
The helper drains standard output and error concurrently. Its Windows package
lookup times out after fifteen seconds without terminating the client.

Numeric launch diagnostics are stored under
`%LOCALAPPDATA%\LaunchDeck\Diagnostics\launcher-*.log`: startup stage, UI
heartbeat delay, working/private memory and handle count. Each file is capped
at 1 MiB. Existing logs are preserved; after eight retained session files,
additional session files are not created. No request content, credentials or
proxy destinations are recorded. These logs do not enable v2rayN logging.

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
No vendor
configuration watcher or vendor JavaScript patch is used.

The **Check / repair browser** dialog retains the manual configuration repair
and rollback tools. The launcher does not rewrite the TinySky flag: live
validation showed the application could overwrite that flag later. Restart
the client through this build to inherit the launch policy.

When enhanced proxy is ready, closing the launcher window now hides it in
the notification area instead of stopping the proxy. Double-click its icon
to reopen it. To stop the session, choose **Exit and stop enhanced proxy**
from the icon's menu. Windows shutdown still cleans
up the session. No startup service or scheduled task is installed.

After a proxy launch, while the launcher remains running, it also checks the
authoritatively installed browser tool configuration every two seconds. Only
missing `HTTPS_PROXY` and `NO_PROXY` forwarding names are added; proxy values,
permissions and unrelated configuration bytes are preserved. Each repair uses
a backup, identity/path checks and concurrent-write protection. Healthy
configuration is not rewritten. This handles application regeneration after
startup and updates; it does not change the environment of already-running
tool processes. Explicitly exiting the launcher stops reconciliation. A stale or missing
runtime still requires reconnecting tools or a normal client restart.
The launcher does not modify app ASAR or vendor JS code. New app versions still
need their environment-forwarding and fallback behavior verified.

## Model and WebSocket proxy coverage

The launch environment also sets `WS_PROXY` and `WSS_PROXY` to the selected
HTTP proxy. Their previous registry values and types are backed up and
restored with the other launch variables. The launcher does not replace the
built-in model provider or change its retry count.

Enhanced mode additionally routes copied Codex background executables under
`%LOCALAPPDATA%\OpenAI\Codex\bin\<16-hex-digits>\codex.exe`. Each included
file must match the current registered package's `app\resources\codex.exe`
hash; reparse paths and unrelated executables are rejected. The protected
host verifies them again before approval. Approval shows the number of core
helpers, and diagnostics record that count. Rules only cover remote TCP
443; local proxy and application connections remain direct. If a new runtime
is first copied after activation, its native rule requires a subsequent
launch; explicit WebSocket proxy variables still apply to that launch.

`--verify-payload` checks embedded resources without extraction, driver loading
or starting ChatGPT. Build and isolated tests do not establish authenticated
cloud WebSocket recovery; that needs a real enhanced session on the client.
