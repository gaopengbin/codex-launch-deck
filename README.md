<p align="center">
  <img src="docs/images/banner.png" alt="Codex Proxy Launch Deck - 一次启动，代理、主题与 AI 创作同时就绪" width="100%">
</p>

<p align="center">
  <strong>简体中文</strong> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/gaopengbin/codex-launch-deck/releases/latest"><img src="https://img.shields.io/github/v/release/gaopengbin/codex-launch-deck?style=flat-square&color=cfff32" alt="最新版本"></a>
  <a href="https://github.com/gaopengbin/codex-launch-deck/releases"><img src="https://img.shields.io/github/downloads/gaopengbin/codex-launch-deck/total?style=flat-square&color=31c9b2" alt="累计下载"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0b7668?style=flat-square" alt="Windows 10 / 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/gaopengbin/codex-launch-deck?style=flat-square&color=173d34" alt="MIT License"></a>
</p>

<p align="center">
  <strong>Windows 上的 Codex 启动与主题工作台。</strong><br>
  通过本地代理启动 Codex，不修改 Windows 系统代理；浏览、安装、即时切换 CodeDrobe 主题，并复用本机已登录的 Codex 创作完整主题。
</p>

<p align="center">
  <a href="https://github.com/gaopengbin/codex-launch-deck/releases/latest"><strong>下载 Windows 安装版</strong></a>
  &nbsp;·&nbsp;
  <a href="https://codedrobe.app">CodeDrobe 主题商店</a>
  &nbsp;·&nbsp;
  <a href="#快速开始">快速开始</a>
</p>

<p align="center">
  <img src="docs/images/theme-gallery.png" alt="Codex Proxy Launch Deck 主题商店" width="92%">
</p>

## 它解决什么

<table>
  <tr>
    <td width="33%" valign="top">
      <strong>01 · 应用代理启动</strong><br><br>
      通过 Windows 应用包入口启动 ChatGPT。启动期间短暂设置当前用户的代理环境变量，进程创建后立即恢复原值；不改系统代理。
    </td>
    <td width="33%" valign="top">
      <strong>02 · 主题商店与即时换肤</strong><br><br>
      搜索、安装和管理 CodeDrobe 主题。Codex 已经运行时也能直接切换，并由 watcher 在渲染器刷新后自动恢复主题。
    </td>
    <td width="33%" valign="top">
      <strong>03 · 本地 Codex AI 创作</strong><br><br>
      复用电脑上已登录的 Codex CLI，根据文字或参考图生成 <code>.codedrobe-theme</code>，无需额外配置模型供应商或 API Key。
    </td>
  </tr>
</table>

## 核心亮点

- **一次启动，同时就绪**：代理连通性检测、Codex 启动、主题校验与应用合并为一条链路。
- **启动后恢复原值**：为让 Windows 应用包继承代理，启动器会短暂设置当前用户的代理环境变量，随后恢复。极短的启动窗口内，其他同时启动的应用也可能读到这些值。
- **运行中即时切换主题**：无需重启 Codex，自动同步主题要求的原生浅色/深色外观。
- **完整组件覆盖**：主题覆盖主界面、右侧输出/来源面板、Tooltip、Popover、Dropdown、Menu、Listbox 与 Dialog。
- **真实主题商店**：支持搜索、排序、下载进度、完整性校验、本地缓存与离线内置主题。
- **AI 主题生成闭环**：支持纯 CSS、上传参考图、AI 背景三种模式，自动校验、打包并加入本地主题库。
- **可靠的运行状态识别**：准确区分残留端口、Codex 主进程和渲染器状态，减少“未启动却被判定为运行中”。
- **托盘后台与安全更新**：关闭窗口后继续在系统托盘守护主题，自动检查签名更新并支持应用内下载安装。
- **随时可恢复**：一键恢复 Codex 原生外观，同时清理持久化主题 watcher。

## 界面与效果

<table>
  <tr>
    <td width="50%" align="center"><strong>复用本地 Codex 创作主题</strong></td>
    <td width="50%" align="center"><strong>主题实际应用到 Codex</strong></td>
  </tr>
  <tr>
    <td><img src="docs/images/ai-theme-creation.png" alt="AI 主题创作流程"></td>
    <td><img src="docs/images/theme-applied-codex.png" alt="主题应用后的 Codex 界面"></td>
  </tr>
</table>

## v2.3.2 更新

- 适配新版 ChatGPT Windows 应用包：通过注册的应用入口启动，修复直接执行 `ChatGPT.exe` 时“该进程没有程序包标识符”的报错。
- 启动时短暂设置当前用户的代理环境变量，并在应用进程创建后恢复原值；同时为 ChatGPT 传递代理参数。
- 新版安装包内置启动辅助程序；旧版启动器请下载 ZIP，保留两个 EXE 在同一目录。

## v2.3.1 更新

- 修复部分 Microsoft Store 安装的 Codex / ChatGPT 启动时报“拒绝访问（错误码 5）”的问题：直接启动失败时，自动尝试应用包身份兼容启动。
- 在应用包上下文中显式传递代理环境变量，不修改 Windows 系统代理。
- 隐藏兼容启动辅助进程，避免启动时短暂闪出 PowerShell 黑框。
- 同步修复新版桌面启动器与旧版轻量启动器，相关反馈见 [#10](https://github.com/gaopengbin/codex-launch-deck/issues/10)。

> 兼容路径依赖 Windows 的 `Invoke-CommandInDesktopPackage` 与 Windows Script Host；企业策略禁用相关组件时仍可能无法使用。

## v2.3.0 更新

- 新增 Windows 系统托盘，关闭主窗口后继续在后台运行，可从托盘打开、检查更新或彻底退出。
- 新增单实例唤醒，重复启动 Launch Deck 不再产生多个后台进程。
- 新增应用内更新、下载进度与 Windows 原生更新通知。
- Release 自动生成签名安装包和 `latest.json`，客户端会在安装前校验更新签名。
- 后台检查失败保持安静，手动检查失败使用简洁中文提示，不再暴露底层网络错误。

## v2.1.1 更新

- AI 主题生成器新增完整组件覆盖契约，避免右侧面板和悬浮层保留系统灰色。
- 自动覆盖 Tooltip、Popover、菜单、下拉列表、对话框及其 hover/highlighted 状态。
- 修复 Microsoft Store 内置 CLI 无法由外部程序执行的问题，自动寻找可用的 Codex CLI。
- AI 创作任务继承启动器代理配置，代理环境下不再因 CLI 拒绝访问而失败。
- Codex 刚启动、DOM 尚未稳定时自动等待并重试主题应用。

## 快速开始

1. 在本地代理软件中开启 **HTTP** 或 **Mixed** 端口。
2. 从 [最新 Release](https://github.com/gaopengbin/codex-launch-deck/releases/latest) 安装并打开 Launch Deck。
3. 填写代理主机和端口，点击检测。
4. 选择主题，点击 **启动 Codex**；如果 Codex 已通过 CDP 运行，可直接点击 **即时应用主题**。

> 如果代理软件提供的是 SOCKS 端口，请改用它的 HTTP/Mixed 端口。Launch Deck 传递的是 HTTP 代理 URL。

## 下载选择

| 文件 | 适合场景 |
| --- | --- |
| `Codex.Proxy.Launch.Deck_*_x64-setup.exe` | 推荐，大多数 Windows 用户使用的 NSIS 安装包 |
| `Codex.Proxy.Launch.Deck_*_x64_en-US.msi` | 适合 MSI 部署与企业安装环境 |
| `ChatGPTProxyLauncher-Legacy-win-x64.zip` | 旧版启动器、必需的启动辅助程序、内置主题与快捷方式脚本合集 |

公开二进制目前尚未进行代码签名，Windows SmartScreen 可能显示安全提示。请始终从本仓库 Release 页面下载。

## AI 主题创作

1. 打开侧栏 **AI 创作**。
2. 输入视觉描述，选择主题需要的 **浅色** 或 **深色** Codex 基底。
3. 选择视觉模式：**纯 CSS**、**上传参考图** 或 **AI 生成图**。
4. 启动创作并查看实时进度。完成后 Launch Deck 会校验主题结构、组件覆盖、背景素材和卡片封面，然后自动加入本地主题库。

生成的主题保存在：

```text
%LOCALAPPDATA%\CodexProxyLaunchDeck\themes
```

主题创作需要 CodeDrobe 主题 Skill。Launch Deck 会自动检测，并可为当前用户安装该能力。

<details>
<summary><strong>与 Cockpit Tools 配合使用</strong></summary>

如果 Codex 由 [Cockpit Tools](https://github.com/jlcodes99/cockpit-tools) 启动，需要在默认实例的 **自定义启动参数** 中加入：

```text
--remote-debugging-port=9335
```

保存后完全退出所有 Codex 窗口和后台进程，再通过 Cockpit Tools 启动。Launch Deck 会从进程命令行自动识别 CDP 端口；如果 `9335` 被占用，可换成其他空闲端口。

请勿把参数填写到 **设置 > Codex 启动路径**，该字段只能填写可执行文件路径。

</details>

<details>
<summary><strong>为什么显式代理能改善 Windows Codex 连接</strong></summary>

- [Windows WebSocket transport works with explicit proxy environment variables](https://github.com/openai/codex/issues/29958)
- [Windows mobile remote control requires proxy environment variables](https://github.com/openai/codex/issues/29233)
- [WebSocket failures exhaust retries before HTTP fallback](https://github.com/openai/codex/issues/19821)
- [Windows Codex recovers with an explicit HTTP proxy](https://github.com/openai/codex/issues/20844)

</details>

## 隐私与安全

Launch Deck 不转发、不读取网络流量，只检查指定代理端点是否可连接。为启动新版 Windows 应用包，它会短暂设置当前用户的代理环境变量，在进程创建后恢复原值；不会修改 Windows 系统代理。AI 主题生成时，用户选择的参考图片只会交给明确启动的本地 Codex 任务；Launch Deck 不会将图片上传到自己的服务。

## 本地构建

```powershell
cd desktop
npm ci
npm run lint
npm run build
npm run tauri build
```

```powershell
cd desktop\src-tauri
cargo test --locked
cargo clippy --locked --all-targets --all-features -- -D warnings
```

构建旧版 .NET Framework 启动器：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

## License

[MIT](LICENSE)

## v2.4.0 enhanced process proxy

The separate `LaunchDeck-Enhanced-win-x64.zip` contains the Windows C# launcher with an optional unified process proxy for the desktop client's TCP 443/WebSocket traffic. The Tauri installers retain the ordinary proxy environment fix; their UI does not yet include enhanced mode. Read [scope, installation, licenses and remaining stop validation](docs/process-proxy.md) before installing. Existing system proxy, TUN and autostart settings are not changed.
