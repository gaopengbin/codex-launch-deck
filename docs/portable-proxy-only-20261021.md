# Portable enhanced-proxy-only edition, 2026-10-21

The WinForms portable launcher now has one launch mode: enhanced proxy.
Theme selection, store refresh/link, appearance restore, theme downloads,
CodeDrobe execution, debugging-port startup and bundled theme packaging were
removed. The UI no longer has an enhanced-proxy checkbox. Host, port, launch,
browser repair, language selection and notification-area session lifetime
remain. Startup no longer queries the theme store or requires Node.js/npx.

Enhanced routing is mandatory before client activation. Cancelling Windows
or traffic approval leaves the client unstarted. Existing package activation,
WebSocket variables, verified copied-core/browser-helper routing and browser
forwarding maintenance remain in place. The resume handoff now carries only
proxy host, port and language. Pressing Launch again does not tear down an
already-approved proxy session for a running client.

The compact layout was rendered from an isolated form for visual inspection;
its startup handler was removed during rendering, so no client, native engine
or network probes were started. Embedded payload checks confirm no theme
packages are included. Package verification and existing session-lifetime
regressions run against the built executable. A new real client launch was
not performed because the user's currently working proxy session remains
active.
