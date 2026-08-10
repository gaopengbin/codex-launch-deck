# CodeDrobe compatibility runtime

This directory contains the pinned CodeDrobe Core package used by launcher
theme apply and watcher operations. Keeping this path local removes npm
`@latest` network latency and gives the launcher one verified compatibility
layer across Codex updates.

- Source: https://github.com/gaopengbin/core/commit/aaa9ce6b9932b439f67a96b5d72db0bb11930448
- Upstream fix: https://github.com/CodeDrobe/core/pull/7
- Package: `codedrobe-core-0.7.0-beta.0-launchdeck.9.tgz`
- SHA-256: `21cbdfa5f2495f088d248142cac1756e071bc0345c62ae57b25975a85fa508fb`

The launcher verifies the package hash before execution. The compatibility
runtime preserves CodeDrobe's normal DOM preflight and never patches Codex.
It adds reversible semantic aliases for the current hashed Codex shell,
recognizes both legacy and current home surfaces, and guards the native header
from theme rules that rewrite every direct main child. It also maps the current
Codex 26.803 semantic textbox shell to the reversible `composer-surface-chrome`
alias, so existing themes keep working without per-theme patches. Multi-image
themes are detected across hero, conversation, and sidebar art without letting
the compatibility fallback oscillate against the theme's own background. Old
`[role="main"]` home rules are scoped to the current semantic home surface, so
preview artwork cannot leak into conversations and overlay panels.
