# Proxy lifetime investigation, 2026-10-21

The current upstream is Xray on loopback port 10808. One bounded request to
chatgpt.com returned HTTP 403 in 1.316 seconds, proving a transport response
only. The previous 7890 port did not respond within a two-second bound. The
current ChatGPT process explicitly uses port 10808, so no stale client port
was established by this check.

The latest protected host log records readiness at 04:05:28 UTC, an explicit
Stop at 04:05:30 UTC, then successful cleanup and exit. The client remained
running. The host had one verified browser helper in its plan. This is not
evidence of a driver crash; the source of the stop is not recorded.

Source inspection found that any exception from post-activation theme/helper
work unconditionally disposed the enhanced proxy, even if the client had
already started. The launch handler now retains an approved ready session
when activation was attempted and the client is still running, and reports
the later-stage failure. Pre-activation failures still clean up. Explicit
user stop and launcher close retain their existing cleanup behavior.

The final portable build passed 50 packaging assertions. Desktop payload
verification passed. The actual activation-error regression still requires a
new real launch; this change has not been credited with live recovery. The
active client and launcher were not terminated. No repeated network polling,
global proxy changes or changes to Codex's internal retry settings were made.

## Store activation handoff follow-up

The next user screenshot showed that proxy retention worked, but activation
still failed when Windows' returned forwarding PID exited. The helper now
resolves the registered package executable and waits up to ten seconds for
a newly started, visible main window at that exact executable path. An old
window, a different executable, and windowless candidates are rejected.
Environment restoration and registered AUMID activation remain in place.

The resolver regression checks and 50 portable packaging checks passed. The
desktop portable payload verification returned zero. The delivered desktop
LaunchDeck.exe SHA256 is
`92CA1543DC0A4AD9B0AE53A57B2C1EA439A01C06538208EC74EA803FABAB21A3`.
The running client was not stopped; real activation acceptance is pending
the next launch using that executable.

## Five reconnect attempts are not fixed

The user also reported `request timed out` and reconnect attempt 5/5.
The current config selects the built-in `openai` provider. Official config
documentation exposes `stream_max_retries` (default five) on custom provider
definitions and says built-in provider IDs cannot be overridden. No retry
override or model route change was written.

Official reference: https://learn.chatgpt.com/docs/config-file/config-reference

The current desktop log separately records repeated remote-control failures
with `Remote environment is offline`. That is not enough to attribute the
model request timeout to remote control. The native proxy log records
successful CONNECT responses during this session, which proves tunnel setup
only, not successful completion of authenticated model streams. The startup
and lifetime changes must not be presented as a verified fix for 5/5 retries.

## Window-close lifetime repair

Further read-only checks established that the active copied Codex app-server
process has established connections to the selected loopback proxy, port
10808. Three bounded, unauthenticated probes through that port returned
401/403 in approximately 0.7, 0.7, and 2.6 seconds. This does not validate
authenticated model streaming or identify the screenshot timeout's cause.

Source inspection found another deterministic disconnect path: ordinary
window close immediately disposed the active enhanced session. User closing
the launcher window now hides it in the notification area while enhanced
proxy is ready. Double-click restores the window; the explicitly labeled
exit menu stops the session. Windows shutdown and inactive close retain
cleanup behavior. No service, autostart task, system proxy change or client
restart is performed by this change.

Four isolated form-event regressions passed: active close retains the
session; explicit exit, Windows shutdown and inactive close clean up. The
tests use no native engine, client process, elevation or network traffic.
The existing process-proxy suite passed all 114 assertions. This closes a
verified lifetime hazard; real model-stream timeout recovery is still not
verified, and there is no evidence yet that window close caused the user's
latest 5/5 screenshot.

## Confirmed open-window reproduction and transport coverage fix

The user confirmed that reconnect 5/5 also occurs while the launcher window
remains open. Window-close behavior does not explain that reproduction.

Read-only queries of the local Codex log database revealed model sampling
timeouts at attempts 1 through 5, followed by `falling back to HTTP`, both
around 13:01-13:02 and 13:33-13:35 China time. A socket snapshot separately
showed a copied Codex background process trying direct remote TCP 443 in
SYN_SENT, while the ordinary HTTP client had loopback proxy connections.
The direct process also logged remote-control WebSocket connect timeouts.
This establishes a transport coverage gap; it does not prove every model
timeout shares that one cause.

The package launcher now backs up, sets and restores WS_PROXY and WSS_PROXY
alongside HTTP_PROXY/HTTPS_PROXY. Enhanced scope now includes copied Codex
cores at the constrained hashed runtime path, verified against the registered
package's resources/codex.exe. Host validation repeats the hash and reparse
checks before approval; approval and bounded diagnostics include core count.
The current installed core was resolved and hash-verified read-only: one
matching runtime. New fake-engine regressions cover core path constraints,
remote-443-only matching, loopback exclusion, rule installation before start
and rollback on rule failure. All 125 process-proxy assertions passed.

No provider override, login change, client ASAR patch, system proxy, DNS or
upstream proxy configuration change was made. Existing processes were not
stopped or modified. Real authenticated streaming with the new helper scope
and launch environment remains pending the next normal launch.

## Live restart verification, 13:53-13:55 China time

The user restarted using the updated portable launcher. The protected host's
new bundle ID is `a2d25fc6d9cc8494-core-websocket-20261021-1`; its plan records
browserHelperCount=1 and coreHelperCount=1, followed by Ready/Success at
13:53:31. The launcher, host and copied Codex processes have fresh start
times. No previous-session readiness was reused for this conclusion.

At 13:54:36 the core log explicitly records successful connection to the
model Responses WebSocket at chatgpt.com/backend-api/codex/responses. Between
13:53:31 and the latest log timestamp 13:55:36, read-only database queries
found zero `codex_core::responses_retry` events and zero HTTP fallback events.
The current conversation continued with multiple tool round trips.

This verifies that the new build loaded and the model WebSocket recovered
in this restart window. It does not establish long-duration stability or
credit native core redirection independently: the native log had no matching
core flow metadata in the sampled window, and explicit WebSocket proxy
variables were also changed in this build. No additional restart or client
process termination was performed during this verification.
