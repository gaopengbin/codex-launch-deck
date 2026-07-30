# Codex Proxy Launch Deck

The Tauri 2 desktop application for ChatGPT Proxy Launcher. It launches Codex
with process-scoped proxy variables and provides a CodeDrobe theme marketplace,
local theme cache, live theme application, and native appearance restoration.

## Development

Requirements:

- Node.js 22 or newer
- Rust stable
- Microsoft C++ Build Tools and WebView2

```powershell
npm ci
npm run tauri dev
```

## Checks

```powershell
npm run lint
npm run build
cargo test --manifest-path .\src-tauri\Cargo.toml
cargo clippy --manifest-path .\src-tauri\Cargo.toml -- -D warnings
```

## Production build

```powershell
npm run tauri build
```

The NSIS and MSI installers are written beneath
`src-tauri/target/release/bundle/`.
