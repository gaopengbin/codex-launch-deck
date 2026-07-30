# Codex Proxy Launch Deck

<p align="center"><a href="README.md">English</a> | <strong>简体中文</strong></p>

<p align="center">
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases/latest"><img src="https://img.shields.io/github/v/release/gaopengbin/chatgpt-proxy-launcher" alt="最新版本"></a>
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases"><img src="https://img.shields.io/github/downloads/gaopengbin/chatgpt-proxy-launcher/total" alt="累计下载"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 和 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/gaopengbin/chatgpt-proxy-launcher" alt="MIT 许可证"></a>
</p>

只让 Codex 通过进程级 HTTP 代理启动，不修改 Windows 系统代理；同时在一个桌面应用中浏览、安装和应用 CodeDrobe 主题。

## 为什么做它

- 解决 Windows 上 Codex 因缺少显式代理而反复出现
  `Reconnecting... 1/5` 到 `5/5` 的问题。
- 只代理 Codex 及其子进程，不影响其他 Windows 应用。
- 让 Codex 主题可以发现、安装、恢复，不再依赖手写命令。
- 继续提供轻量旧版，满足只需要代理启动的用户。

## Launch Deck 特性

- 自绘无边框界面，支持窗口拖动、最小化、最大化和关闭
- 自动发现 Microsoft Store/MSIX 安装的 Codex
- 代理连通性检测和准确的 Codex 进程识别
- CodeDrobe 主题商店浏览、搜索、排序、安装与本地缓存
- 离线内置“初音未来 · Future Beats”主题
- Codex 运行中动态应用主题
- 自动维护主题 watcher，应对渲染器刷新
- 自适应选择本机 CDP 端口，避开残留的 `9335` 端口冲突
- 一键恢复 Codex 原生外观
- English / 简体中文界面

## 下载

请从最新 [GitHub Release](https://github.com/gaopengbin/chatgpt-proxy-launcher/releases/latest)
下载，推荐使用 NSIS 安装版：

- `Codex Proxy Launch Deck_*_x64-setup.exe`：推荐安装包
- `Codex Proxy Launch Deck_*_x64_en-US.msi`：MSI 安装包
- `ChatGPTProxyLauncher.exe`：旧版轻量单文件
- `ChatGPTProxyLauncher-Legacy-win-x64.zip`：含内置主题和快捷方式脚本的旧版

目前公开二进制尚未代码签名，Windows SmartScreen 可能显示安全提示。

## 使用

1. 启动本地代理软件的 HTTP 或 mixed 端口。
2. 打开 Launch Deck，填写代理主机和端口。
3. 选择主题，或关闭主题模式。
4. 点击“启动 Codex”。

Launch Deck 只给新启动的进程树设置大小写形式的 `HTTP_PROXY`、
`HTTPS_PROXY` 和 `NO_PROXY`，不会修改 Windows 系统代理。

主题功能依赖 [CodeDrobe](https://codedrobe.app)。Launch Deck 会优先使用
稳定的全局 `codedrobe` 命令，也可以回退到 `npx`。

> 如果代理软件提供的是 SOCKS 端口，请改用它的 HTTP/mixed 端口；
> 本工具传递的是 HTTP 代理 URL。

## 构建

构建推荐的 Tauri 桌面版：

```powershell
cd desktop
npm ci
npm run tauri build
```

构建旧版 .NET Framework 轻量启动器：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

开发环境与检查命令见 [desktop/README.md](desktop/README.md)。

## 相关官方问题

- [Windows WebSocket 设置显式代理环境变量后恢复](https://github.com/openai/codex/issues/29958)
- [Windows 手机远程控制需要代理环境变量](https://github.com/openai/codex/issues/29233)
- [WebSocket 失败后经历完整重试才回退 HTTP](https://github.com/openai/codex/issues/19821)
- [Windows Codex 使用显式 HTTP 代理后恢复](https://github.com/openai/codex/issues/20844)

## 隐私与安全

启动器不转发或读取网络流量，只检查指定代理端点是否可连接，并把代理环境变量传给新启动的 Codex 进程。

## 许可证

MIT
