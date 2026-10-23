# Launcher responsiveness investigation, 2026-10-21

The user reported that both the launcher and v2rayN became unresponsive.
The live sample at approximately 15:28 China time recorded working sets of
30 MiB for the launcher, 15–17 MiB for its native proxy host, 101–103 MiB for
v2rayN and 57–58 MiB for Xray. Windows reported 10.8 GiB free physical memory
out of 63.8 GiB. All four processes reported responding in that sample; their
CPU increments over a subsequent three-second interval were zero. This
does not reproduce or disprove a prior hang, but establishes no current
memory-exhaustion or sustained-CPU evidence for these processes.

Source inspection identified UI-thread blocking in package activation,
protected-component checks and initial browser forwarding inspection. These
operations now run asynchronously or on background workers. Activation
stdout and stderr are drained concurrently. The helper's read-only Windows
package lookup has a fifteen-second limit and stops only its own lookup
child on timeout, before any client activation or environment override.

The launcher records numeric process memory, handles, operation stage and
UI heartbeat delay under `%LOCALAPPDATA%\LaunchDeck\Diagnostics\launcher-*.log`.
The background sampler records a heartbeat every minute and stalled UI
heartbeats every five seconds. Each file is bounded to 1 MiB; existing logs
are preserved and new session files stop after eight retained sessions.
No credentials, proxy destinations or request bodies are recorded.
This diagnostic begins on the next launch of the new portable build.

Checks passed: 49 portable assertions, activation handoff/rejection cases,
WebSocket rollback-key coverage, session close/shutdown cases, retained
UI-delay diagnostics, and an owned controlled helper that fills stderr and
waits two seconds without blocking the activation caller. No real client,
native engine or network is started by the UI regression fixture.

The working client's processes were not restarted. v2rayN's hang cause is
not established. Its core log-save option is still false: the attempted
Windows elevation did not start the configuration helper. It must not be
reported as enabled until generated core log paths and actual file writes
are verified. The release remains a draft pending the ongoing investigation.
