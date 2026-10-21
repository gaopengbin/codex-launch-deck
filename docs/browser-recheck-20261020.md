# Browser recurrence and recovery, 2026-10-20

The installed client changed to Windows package 26.1013.4894.0 and browser
plugin 26.1013.40934, using runtime 78fb63cda1a9e2b2. The configured executable
paths exist. The disabled Tab.ax setting is still present. The copied helper
matches the package's helper SHA256, so unknown-binary approval was not relaxed.

Before the restart, ordinary Earth reads and screenshots each took about
21.4 seconds. Some official helper processes had remote TCP 443 connections
in SYN_SENT state. The tool-startup path error in the user's other chat was
not reproduced here. Existing host diagnostics did not include helper-rule
counts, so exact active helper coverage could not be reconstructed.

Adding proxy forwarding names to the plugin file did not fix the active
process. The user subsequently restarted after the client stopped responding.
After that restart the generated file again lacked those forwarding names,
but ordinary Earth reading returned in 361 ms and its full screenshot in
34 ms. This recovery happened using the existing launcher/proxy session,
before the new build. Thus the live recovery must not be attributed to the
new configuration reconciler; stale session state remains a contributing
candidate, not a fully established root cause. The client hang was not
attributed to the build or watcher, which had not started in the launcher.

The added launcher protection maintains only missing proxy forwarding keys
through subsequent configuration regeneration, using the existing validated
backup/atomic repair implementation. Tests cover startup, regeneration,
healthy no-op, shutdown and error retention. The host diagnostic now records
the helper count. Fresh-launch acceptance of this added protection is still
required; the current recovered browser is evidence of recovery, not of
that new code running.

## Local delivery

52 configuration/reconciliation checks, 114 managed proxy/lifecycle assertions
and 50 portable packaging checks passed after the restart. The final local
single-EXE build is in `output/portable-browser-forwarding-20261020-r3`.
The desktop `LaunchDeck.exe` was absent, so the new executable was copied there
without replacing another file or changing existing shortcuts. Its embedded
payload verification returned exit code 0.

SHA256: `C2E82B64126D97391208D0FF604359E21C7FCDE8F031ECA0E10F908B808EBEB5`.

The active launcher was not stopped or replaced in place. The added
reconciliation code will run on the next launch through the new desktop EXE;
its fresh-launch acceptance remains pending. No commit, push or release was
performed for this local follow-up.
