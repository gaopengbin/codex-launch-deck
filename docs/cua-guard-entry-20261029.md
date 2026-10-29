# CUA guarded-entry compatibility, 2026-10-29

The authoritative installed unified-computer-use plugin, version
26.1023.51421, now launches node with three arguments: `--import`, a file URI
to its installed `require-browser-access.mjs`, and the runtime CUA script.
The previous launcher accepted only the single-script layout and reported
`Unexpected CUA launch entry` before inspecting proxy forwarding.

The inspector now accepts the guarded layout only at the exact validated
plugin path, with a local file URI, no query/fragment, the exact import flag,
and the existing constrained runtime script. Missing, substituted or remote
guards are rejected. If a guard file is installed, silently omitting its
import is rejected. The legacy layout remains available when no guard is
installed. Existing path, reparse, manifest, service and concurrent-write
checks remain in force.

Live inspection and forwarding repair succeeded. Only missing HTTPS_PROXY
and NO_PROXY forwarding names were appended, with a rollback record. The
guard file hash stayed
`d314c3f98b63d8c23c9989a3ef9c036e5d0e530d6c4a6a01e4d53da451aba815`;
all three browser access authorization environment names remained present.
The access guard, browser read mode and permission settings were not changed.

Checks: 61 configuration assertions, 49 portable assertions and launcher
activation/lifetime regressions passed. The desktop EXE was backed up outside
the desktop and replaced; payload verification returned zero. Desktop SHA256:
`222256D56F62828104C1D9D20821D747DE6AE0FF3A0F047ED1B6E38D4E302D52`.
The running client and proxy session were not stopped. An already-running
old launcher still uses its old inspector until normally restarted. This
local follow-up has not been published as a new release.
