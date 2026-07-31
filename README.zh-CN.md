# Codex Proxy Launch Deck

<p align="center"><a href="README.md">English</a> | <strong>简体中文</strong></p>

<p align="center">
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases/latest"><img src="https://img.shields.io/github/v/release/gaopengbin/chatgpt-proxy-launcher" alt="最新版本"></a>
  <a href="https://github.com/gaopengbin/chatgpt-proxy-launcher/releases"><img src="https://img.shields.io/github/downloads/gaopengbin/chatgpt-proxy-launcher/total" alt="累计下载"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 和 11">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/gaopengbin/chatgpt-proxy-launcher" alt="MIT 许可证"></a>
</p>

只让 Codex 通过进程级 HTTP 代理启动，不修改 Windows 系统代理；同时在一个桌面应用中浏览、安装和应用 CodeDrobe 主题。Launch Deck 还可以复用本机已登录的 Codex CLI，根据文字描述或参考图片生成自定义主题。

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
- CodeDrobe 主题商店浏览、搜索、排序、安装、下载进度、完整性校验与本地缓存
- 离线内置“初音未来 · Future Beats”主题
- 主题卡片显示浅色/深色标签，应用时自动同步主题所需的 Codex 原生外观模式
- 复用本机 Codex 生成自定义主题，支持纯 CSS、上传参考图和 AI 背景三种模式
- 自动生成主题主视觉、可选辅助纹理和主题卡片缩略图
- Codex 运行中即时应用主题，无需重启
- 自动维护主题 watcher，应对渲染器刷新
- 自适应选择本机 CDP 端口，避开残留的 `9335` 端口冲突
- 一键恢复 Codex 原生外观并清理持久化 watcher
- 后台命令隐藏运行，主题和进程操作不会反复闪出黑色控制台窗口
- English / 简体中文界面

## 2.1 新增内容

- 直接使用已登录的 Codex CLI 创建并缓存完整的 `.codedrobe-theme` 包，无需再配置模型供应商或 API Key。
- 可上传本地图片作为视觉参考；生成流程会提取并保留关键构图锚点，而不是把原图简单铺成全屏背景。
- 生成前可指定浅色或深色 Codex 基底；主题卡片会显示要求，应用时通过 Codex 原生外观动作自动切换。
- 在 Launch Deck 内查看 AI 生成与商店下载进度，错误提示更清晰；代理端口也改为无原生 HTML 微调按钮的自定义控件。

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

如果 Codex 已经通过 CDP 端口运行，可以直接点击“即时应用主题”。Launch Deck
会读取主题声明的浅色/深色基底并自动同步当前 Codex 窗口；恢复原生外观时会还原
受管理的 Codex 设置并移除已注入主题。

Launch Deck 只给新启动的进程树设置大小写形式的 `HTTP_PROXY`、
`HTTPS_PROXY` 和 `NO_PROXY`，不会修改 Windows 系统代理。

主题功能依赖 [CodeDrobe](https://codedrobe.app)。Launch Deck 会优先使用
稳定的全局 `codedrobe` 命令，也可以回退到 `npx`。

> 如果代理软件提供的是 SOCKS 端口，请改用它的 HTTP/mixed 端口；
> 本工具传递的是 HTTP 代理 URL。

## 使用 Codex 创建自定义主题

该流程复用电脑上已经安装并登录的 Codex CLI，不需要单独填写 API Key，也不在
Launch Deck 内嵌另一套模型服务。

1. 在 Launch Deck 中打开 **AI 创作**。
2. 输入视觉描述，并选择主题需要的 **浅色** 或 **深色** 基底。
3. 选择一种视觉模式：
   - **纯 CSS**：使用渐变、玻璃、边框、阴影和动效，不生成位图背景。
   - **上传参考图**：基于本地图片重新生成协调的主题样式与视觉素材。
   - **AI 生成图**：由 Codex 根据文字描述生成主题主视觉。
4. 开始生成并查看分阶段进度。完成后，Launch Deck 会校验主题包、生成或补全
   卡片缩略图，并将主题加入本地主题库。

主题创作需要 CodeDrobe 主题 Skill。Launch Deck 会检测该能力，也可以为当前用户
安装。生成的主题包保存在 `%LOCALAPPDATA%\CodexProxyLaunchDeck\themes`，使用方式与
商店主题相同。

## 通过 Cockpit Tools 启动并应用主题

如果 Codex 由 [Cockpit Tools](https://github.com/jlcodes99/cockpit-tools)
启动，Launch Deck 仍可应用和切换主题；但启动时必须开启本机 Chromium
DevTools 调试端口，否则 CodeDrobe 无法在 Codex 已运行后连接到渲染器。

1. 打开 Cockpit Tools，进入 **Codex**，点击 **应用多开**。
2. 找到 **默认实例**，点击编辑。
3. 在 **自定义启动参数** 中填写：

   ```text
   --remote-debugging-port=9335
   ```

4. 保存后，完全退出所有 Codex 窗口和后台进程，再通过 Cockpit Tools
   启动默认实例。
5. 打开 Launch Deck，点击 **即时应用主题**，即可应用或切换主题。

所选主题需要的原生浅色/深色基底会自动同步，无需再进入 Codex 外观设置手动切换。

请不要把该参数填到 Cockpit Tools 的 **设置 > Codex 启动路径**：该字段只
用于填写可执行文件路径。若 `9335` 已被占用，请换成空闲端口，并保持
`--remote-debugging-port=<端口>` 的格式；Launch Deck 会从 Codex 启动命令
中自动识别端口。

## 构建

构建推荐的 Tauri 桌面版：

```powershell
cd desktop
npm ci
npm run lint
npm run build
npm run tauri build
```

Rust 检查：

```powershell
cd desktop\src-tauri
cargo test
cargo clippy --all-targets --all-features -- -D warnings
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

启动器不转发或读取网络流量，只检查指定代理端点是否可连接，并把代理环境变量传给新启动的 Codex 进程。AI 主题生成时，用户选中的参考图片只会传给明确启动的 Codex 生成任务；Launch Deck 不会把图片上传到自己的服务。

## 许可证

MIT
