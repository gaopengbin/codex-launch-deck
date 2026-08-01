# CodeDrobe compatibility runtime

This directory contains the pinned CodeDrobe Core package used by launcher
theme apply and watcher operations. Keeping this path local removes npm
`@latest` network latency and gives the launcher one verified compatibility
layer across Codex updates.

- Source: https://github.com/gaopengbin/core/commit/aaa9ce6b9932b439f67a96b5d72db0bb11930448
- Upstream fix: https://github.com/CodeDrobe/core/pull/7
- Package: `codedrobe-core-0.7.0-beta.0.tgz`
- SHA-256: `b9ec7a467ac1e30f5879feff3bd6d35d3e68d3ce63db20e98c4fba379e91a354`

The launcher verifies the package hash before execution. The compatibility
runtime preserves CodeDrobe's normal DOM preflight and never patches Codex.
It adds reversible semantic aliases for the current hashed Codex shell,
recognizes both legacy and current home surfaces, and guards the native header
from theme rules that rewrite every direct main child.
