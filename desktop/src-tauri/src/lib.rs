use encoding_rs::GBK;
use reqwest::{Client, Url};
use serde::{Deserialize, Serialize};
use std::{
    env, fs,
    net::{TcpListener, TcpStream, ToSocketAddrs},
    path::{Path, PathBuf},
    process::{Child, Command, Output, Stdio},
    sync::{Arc, Mutex},
    time::Duration,
};
use tauri::{AppHandle, Manager, State};

const CODEDROBE_BASE: &str = "https://codedrobe.app";
const CODEDROBE_PORT: u16 = 9335;
const BUNDLED_THEME_FILE: &str = "miku-future-beats-1.2.1.codedrobe-theme";

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ProxyConfig {
    host: String,
    port: u16,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct ThemeSelection {
    slug: String,
    version: String,
    bundled: bool,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct LaunchRequest {
    proxy: ProxyConfig,
    theme: Option<ThemeSelection>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct AppState {
    codex_installed: bool,
    codex_running: bool,
    codedrobe_available: bool,
    cached_themes: Vec<String>,
    cache_directory: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ActionResult {
    message: String,
    theme_path: Option<String>,
    warning: bool,
}

#[derive(Clone, Default)]
struct WatcherState {
    child: Arc<Mutex<Option<Child>>>,
    port: Arc<Mutex<Option<u16>>>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct LocalizedText {
    #[serde(default)]
    en: String,
    #[serde(default)]
    zh: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct LocalizedDescription {
    #[serde(default)]
    en: Option<String>,
    #[serde(default)]
    zh: Option<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct MarketplaceCategory {
    slug: String,
    name: LocalizedText,
    #[serde(default)]
    primary: bool,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct MarketplaceAuthor {
    handle: String,
    #[serde(default)]
    display_name: String,
    #[serde(default)]
    avatar_url: Option<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct MarketplacePrice {
    #[serde(default)]
    free: bool,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct MarketplaceTheme {
    id: String,
    slug: String,
    name: LocalizedText,
    version: String,
    #[serde(default)]
    description: Option<LocalizedDescription>,
    #[serde(default)]
    categories: Vec<MarketplaceCategory>,
    #[serde(default)]
    preview_url: Option<String>,
    #[serde(default)]
    cover_url: Option<String>,
    #[serde(default)]
    published_at: String,
    #[serde(default)]
    supported_apps: Vec<String>,
    #[serde(default)]
    author: Option<MarketplaceAuthor>,
    #[serde(default)]
    price: Option<MarketplacePrice>,
    #[serde(default)]
    like_count: u64,
    #[serde(default)]
    download_count: u64,
}

#[derive(Debug, Deserialize)]
struct MarketplaceMeta {
    #[serde(default)]
    total: usize,
}

#[derive(Debug, Deserialize)]
struct MarketplaceResponse {
    data: Vec<MarketplaceTheme>,
    meta: MarketplaceMeta,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct MarketplacePage {
    themes: Vec<MarketplaceTheme>,
    total: usize,
}

#[tauri::command]
async fn get_app_state() -> Result<AppState, String> {
    tauri::async_runtime::spawn_blocking(|| {
        let cache = theme_cache_directory()?;
        Ok(AppState {
            codex_installed: find_codex_executable().is_some(),
            codex_running: is_codex_running(),
            codedrobe_available: find_stable_codedrobe().is_some()
                || find_on_path("npx.cmd").is_some(),
            cached_themes: cached_theme_slugs(&cache),
            cache_directory: cache.to_string_lossy().into_owned(),
        })
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn get_codex_running() -> Result<bool, String> {
    tauri::async_runtime::spawn_blocking(is_codex_running)
        .await
        .map_err(|error| error.to_string())
}

#[tauri::command]
async fn test_proxy(proxy: ProxyConfig) -> Result<ActionResult, String> {
    let address = format!("{}:{}", proxy.host.trim(), proxy.port);
    let mut addresses = tokio::net::lookup_host(&address)
        .await
        .map_err(|error| format!("无法解析代理地址：{error}"))?;
    let target = addresses
        .next()
        .ok_or_else(|| "代理地址没有可用的网络端点。".to_string())?;
    tokio::time::timeout(
        Duration::from_secs(3),
        tokio::net::TcpStream::connect(target),
    )
    .await
    .map_err(|_| "代理连接超时。".to_string())?
    .map_err(|error| format!("无法连接代理：{error}"))?;
    Ok(ActionResult {
        message: format!("代理 {} 连接正常", address),
        theme_path: None,
        warning: false,
    })
}

#[tauri::command]
async fn list_themes(proxy: Option<ProxyConfig>) -> Result<MarketplacePage, String> {
    match fetch_marketplace(proxy.as_ref()).await {
        Ok(page) => Ok(page),
        Err(proxy_error) if proxy.is_some() => fetch_marketplace(None)
            .await
            .map_err(|direct_error| format!("{proxy_error}；直连重试也失败：{direct_error}")),
        Err(error) => Err(error),
    }
}

#[tauri::command]
async fn launch_codex(
    app: AppHandle,
    watcher: State<'_, WatcherState>,
    request: LaunchRequest,
) -> Result<ActionResult, String> {
    let watcher = watcher.inner().clone();
    tauri::async_runtime::spawn_blocking(move || launch_codex_blocking(&app, &watcher, request))
        .await
        .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn download_theme(proxy: ProxyConfig, theme: ThemeSelection) -> Result<ActionResult, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let path = ensure_theme_package(&theme, &proxy)?;
        Ok(ActionResult {
            message: format!("主题 {} 已缓存", theme.slug),
            theme_path: Some(path.to_string_lossy().into_owned()),
            warning: false,
        })
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn apply_theme(
    app: AppHandle,
    watcher: State<'_, WatcherState>,
    proxy: ProxyConfig,
    theme: ThemeSelection,
) -> Result<ActionResult, String> {
    let watcher = watcher.inner().clone();
    tauri::async_runtime::spawn_blocking(move || {
        apply_theme_blocking(&app, &watcher, &proxy, &theme)
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn restore_theme(watcher: State<'_, WatcherState>) -> Result<ActionResult, String> {
    let watcher = watcher.inner().clone();
    tauri::async_runtime::spawn_blocking(move || {
        stop_watcher(&watcher)?;
        let port = active_cdp_port(&watcher)?.to_string();
        run_codedrobe(&["restore", "--app", "codex", "--port", &port], None)?;
        Ok(ActionResult {
            message: "Codex 已恢复原生外观".to_string(),
            theme_path: None,
            warning: false,
        })
    })
    .await
    .map_err(|error| error.to_string())?
}

async fn fetch_marketplace(proxy: Option<&ProxyConfig>) -> Result<MarketplacePage, String> {
    let mut builder = Client::builder()
        .timeout(Duration::from_secs(18))
        .user_agent("Codex-Proxy-Launch-Deck/0.1");
    if let Some(proxy) = proxy {
        builder = builder.proxy(
            reqwest::Proxy::all(proxy_url(proxy))
                .map_err(|error| format!("代理配置无效：{error}"))?,
        );
    }
    let client = builder.build().map_err(|error| error.to_string())?;
    let url = format!("{CODEDROBE_BASE}/api/v1/themes?app=codex&limit=100&sort=downloads");
    let response = client
        .get(url)
        .send()
        .await
        .map_err(|error| format!("主题商店请求失败：{error}"))?;
    if !response.status().is_success() {
        return Err(format!("主题商店返回 HTTP {}", response.status()));
    }
    let mut payload = response
        .json::<MarketplaceResponse>()
        .await
        .map_err(|error| format!("主题商店数据无效：{error}"))?;
    let base = Url::parse(CODEDROBE_BASE).map_err(|error| error.to_string())?;
    for theme in &mut payload.data {
        theme.cover_url = absolute_display_url(&base, theme.cover_url.take());
        theme.preview_url = absolute_display_url(&base, theme.preview_url.take());
        if let Some(author) = &mut theme.author {
            author.avatar_url = absolute_display_url(&base, author.avatar_url.take());
        }
    }
    Ok(MarketplacePage {
        total: payload.meta.total.max(payload.data.len()),
        themes: payload.data,
    })
}

fn absolute_display_url(base: &Url, value: Option<String>) -> Option<String> {
    value
        .filter(|value| !value.is_empty())
        .and_then(|value| base.join(&value).ok())
        .filter(|url| url.scheme() == "https" || url.origin() == base.origin())
        .map(|url| url.to_string())
}

fn launch_codex_blocking(
    app: &AppHandle,
    watcher: &WatcherState,
    request: LaunchRequest,
) -> Result<ActionResult, String> {
    if request.proxy.host.trim().is_empty() {
        return Err("请输入代理主机。".to_string());
    }
    verify_proxy(&request.proxy)?;
    if is_codex_running() {
        return Err("Codex 已在运行。请完全退出后再通过代理重新启动。".to_string());
    }

    if let Some(theme) = request.theme {
        let theme_path = resolve_theme_path(app, &theme, &request.proxy)?;
        let selected_port = select_cdp_port()?;
        set_active_port(watcher, selected_port)?;
        let port = selected_port.to_string();
        let path = theme_path.to_string_lossy().into_owned();
        let apply_args = ["apply", "--app", "codex", "--port", &port, "--theme", &path];
        if let Err(error) = run_codedrobe(&apply_args, Some(&request.proxy)) {
            if is_codex_running() {
                return Ok(ActionResult {
                    message: format!(
                        "Codex 已启动（CDP {selected_port}），但主题应用失败：{error}"
                    ),
                    theme_path: Some(path),
                    warning: true,
                });
            }
            return Err(error);
        }

        let watcher_args = [
            "apply",
            "--app",
            "codex",
            "--port",
            &port,
            "--theme",
            &path,
            "--no-launch",
            "--watch",
        ];
        stop_watcher(watcher)?;
        let child = match spawn_codedrobe(&watcher_args, Some(&request.proxy)) {
            Ok(child) => child,
            Err(error) => {
                return Ok(ActionResult {
                    message: format!("Codex 与主题已启动，但主题守护进程启动失败：{error}"),
                    theme_path: Some(path),
                    warning: true,
                });
            }
        };
        if let Err(error) = set_watcher(watcher, child) {
            return Ok(ActionResult {
                message: format!("Codex 与主题已启动，但无法管理主题守护进程：{error}"),
                theme_path: Some(path),
                warning: true,
            });
        }
        Ok(ActionResult {
            message: format!(
                "Codex 已通过代理启动，并应用主题 {}（CDP {selected_port}）",
                theme.slug
            ),
            theme_path: Some(path),
            warning: false,
        })
    } else {
        let executable = find_codex_executable()
            .ok_or_else(|| "未找到 Codex / ChatGPT Windows 桌面应用。".to_string())?;
        let mut command = Command::new(executable);
        apply_proxy_environment(&mut command, &request.proxy);
        command
            .spawn()
            .map_err(|error| format!("启动 Codex 失败：{error}"))?;
        Ok(ActionResult {
            message: "Codex 已通过代理启动".to_string(),
            theme_path: None,
            warning: false,
        })
    }
}

fn apply_theme_blocking(
    app: &AppHandle,
    watcher: &WatcherState,
    proxy: &ProxyConfig,
    theme: &ThemeSelection,
) -> Result<ActionResult, String> {
    if !is_codex_running() {
        return Err("Codex 尚未运行，请先通过启动器启动。".to_string());
    }

    let theme_path = resolve_theme_path(app, theme, proxy)?;
    let path = theme_path.to_string_lossy().into_owned();
    let selected_port = active_cdp_port(watcher)?;
    let port = selected_port.to_string();

    stop_watcher(watcher)?;
    run_codedrobe(
        &[
            "apply",
            "--app",
            "codex",
            "--port",
            &port,
            "--theme",
            &path,
            "--no-launch",
        ],
        Some(proxy),
    )
    .map_err(|error| {
        format!(
            "{error}。如果 Codex 不是由本启动器启动，请退出一次并通过启动器重新启动；之后即可无重启切换主题。"
        )
    })?;
    start_watcher(watcher, selected_port, &path, proxy)?;

    Ok(ActionResult {
        message: format!("主题 {} 已即时应用，无需重启 Codex", theme.slug),
        theme_path: Some(path),
        warning: false,
    })
}

fn resolve_theme_path(
    app: &AppHandle,
    theme: &ThemeSelection,
    proxy: &ProxyConfig,
) -> Result<PathBuf, String> {
    if theme.bundled {
        bundled_theme_path(app)
    } else {
        ensure_theme_package(theme, proxy)
    }
}

fn select_cdp_port() -> Result<u16, String> {
    (CODEDROBE_PORT..=CODEDROBE_PORT + 10)
        .find(|port| {
            let Ok(ipv4) = TcpListener::bind(("127.0.0.1", *port)) else {
                return false;
            };
            let Ok(ipv6) = TcpListener::bind(("::1", *port)) else {
                return false;
            };
            drop((ipv4, ipv6));
            true
        })
        .ok_or_else(|| {
            format!(
                "CodeDrobe 调试端口 {}-{} 均被占用，请稍后重试。",
                CODEDROBE_PORT,
                CODEDROBE_PORT + 10
            )
        })
}

fn set_active_port(watcher: &WatcherState, port: u16) -> Result<(), String> {
    let mut active_port = watcher
        .port
        .lock()
        .map_err(|_| "CodeDrobe 端口状态不可用。".to_string())?;
    *active_port = Some(port);
    Ok(())
}

fn active_cdp_port(watcher: &WatcherState) -> Result<u16, String> {
    let active_port = watcher
        .port
        .lock()
        .map_err(|_| "CodeDrobe 端口状态不可用。".to_string())?;
    if let Some(port) = *active_port {
        return Ok(port);
    }
    drop(active_port);
    Ok(detect_codex_cdp_port().unwrap_or(CODEDROBE_PORT))
}

fn detect_codex_cdp_port() -> Option<u16> {
    let script = "$p=Get-CimInstance Win32_Process|Where-Object{$_.Name -in 'ChatGPT.exe','Codex.exe' -and $_.ExecutablePath -like '*\\WindowsApps\\OpenAI.Codex_*\\app\\*' -and $_.CommandLine -notlike '*--type=*'}|Select-Object -First 1;if($p.CommandLine -match '--remote-debugging-port=(\\d+)'){[Console]::Out.Write($Matches[1])}";
    let output = Command::new("powershell.exe")
        .args([
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            script,
        ])
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    String::from_utf8_lossy(&output.stdout).trim().parse().ok()
}

fn start_watcher(
    watcher: &WatcherState,
    selected_port: u16,
    theme_path: &str,
    proxy: &ProxyConfig,
) -> Result<(), String> {
    let port = selected_port.to_string();
    let child = spawn_codedrobe(
        &[
            "apply",
            "--app",
            "codex",
            "--port",
            &port,
            "--theme",
            theme_path,
            "--no-launch",
            "--watch",
        ],
        Some(proxy),
    )?;
    set_watcher(watcher, child)
}

fn set_watcher(watcher: &WatcherState, child: Child) -> Result<(), String> {
    let mut active = watcher
        .child
        .lock()
        .map_err(|_| "CodeDrobe watcher 状态不可用。".to_string())?;
    *active = Some(child);
    Ok(())
}

fn stop_watcher(watcher: &WatcherState) -> Result<(), String> {
    let mut active = watcher
        .child
        .lock()
        .map_err(|_| "CodeDrobe watcher 状态不可用。".to_string())?;
    if let Some(mut child) = active.take() {
        match child.try_wait() {
            Ok(Some(_)) => {}
            Ok(None) => {
                terminate_watcher_process(&mut child)?;
            }
            Err(error) => return Err(format!("无法检查 CodeDrobe watcher：{error}")),
        }
    }
    Ok(())
}

fn terminate_watcher_process(child: &mut Child) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let pid = child.id().to_string();
        let output = Command::new("taskkill.exe")
            .args(["/PID", &pid, "/T", "/F"])
            .output()
            .map_err(|error| format!("无法停止旧的 CodeDrobe watcher：{error}"))?;
        if !output.status.success() && child.try_wait().ok().flatten().is_none() {
            return Err(format!(
                "无法停止旧的 CodeDrobe watcher：{}",
                String::from_utf8_lossy(&output.stderr).trim()
            ));
        }
        let _ = child.wait();
        Ok(())
    }

    #[cfg(not(target_os = "windows"))]
    {
        child
            .kill()
            .map_err(|error| format!("无法停止旧的 CodeDrobe watcher：{error}"))?;
        let _ = child.wait();
        Ok(())
    }
}

fn ensure_theme_package(theme: &ThemeSelection, proxy: &ProxyConfig) -> Result<PathBuf, String> {
    validate_slug(&theme.slug)?;
    let cache = theme_cache_directory()?;
    fs::create_dir_all(&cache).map_err(|error| format!("无法创建主题缓存：{error}"))?;
    let file_name = format!(
        "{}-{}.codedrobe-theme",
        theme.slug,
        safe_file_part(&theme.version)
    );
    let destination = cache.join(file_name);
    if destination.exists() {
        return Ok(destination);
    }

    let path = destination.to_string_lossy().into_owned();
    let args = [
        "theme",
        "download",
        theme.slug.as_str(),
        "--output",
        path.as_str(),
        "--json",
    ];
    if let Err(error) = run_codedrobe(&args, Some(proxy)) {
        let _ = fs::remove_file(&destination);
        return Err(error);
    }
    if !destination.exists() {
        return Err("主题下载完成，但缓存文件不存在。".to_string());
    }
    Ok(destination)
}

fn bundled_theme_path(app: &AppHandle) -> Result<PathBuf, String> {
    let development = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("resources")
        .join(BUNDLED_THEME_FILE);
    if development.exists() {
        return Ok(development);
    }
    let packaged = app
        .path()
        .resource_dir()
        .map_err(|error| error.to_string())?
        .join("themes")
        .join(BUNDLED_THEME_FILE);
    packaged
        .exists()
        .then_some(packaged)
        .ok_or_else(|| "内置初音主题包不存在。".to_string())
}

fn verify_proxy(proxy: &ProxyConfig) -> Result<(), String> {
    let address = format!("{}:{}", proxy.host.trim(), proxy.port);
    let addresses = address
        .to_socket_addrs()
        .map_err(|error| format!("无法解析代理地址：{error}"))?;
    for target in addresses {
        if TcpStream::connect_timeout(&target, Duration::from_secs(3)).is_ok() {
            return Ok(());
        }
    }
    Err(format!("无法连接代理 {address}。"))
}

fn theme_cache_directory() -> Result<PathBuf, String> {
    env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .map(|path| path.join("CodexProxyLaunchDeck").join("themes"))
        .ok_or_else(|| "无法定位 LOCALAPPDATA。".to_string())
}

fn cached_theme_slugs(directory: &Path) -> Vec<String> {
    let mut themes = fs::read_dir(directory)
        .ok()
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .filter_map(|entry| entry.file_name().into_string().ok())
        .filter(|name| name.ends_with(".codedrobe-theme"))
        .collect::<Vec<_>>();
    themes.sort();
    themes
}

fn proxy_url(proxy: &ProxyConfig) -> String {
    format!("http://{}:{}", proxy.host.trim(), proxy.port)
}

fn apply_proxy_environment(command: &mut Command, proxy: &ProxyConfig) {
    let proxy = proxy_url(proxy);
    for key in ["HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy"] {
        command.env(key, &proxy);
    }
    command.env("NO_PROXY", "localhost,127.0.0.1,::1");
    command.env("no_proxy", "localhost,127.0.0.1,::1");
}

fn run_codedrobe(args: &[&str], proxy: Option<&ProxyConfig>) -> Result<Output, String> {
    let mut command = codedrobe_command(args)?;
    if let Some(proxy) = proxy {
        apply_proxy_environment(&mut command, proxy);
    }
    let output = command
        .output()
        .map_err(|error| format!("无法启动 CodeDrobe：{error}"))?;
    if output.status.success() {
        Ok(output)
    } else {
        Err(codedrobe_error(&output))
    }
}

fn spawn_codedrobe(args: &[&str], proxy: Option<&ProxyConfig>) -> Result<Child, String> {
    let mut command = codedrobe_command(args)?;
    if let Some(proxy) = proxy {
        apply_proxy_environment(&mut command, proxy);
    }
    command.stdout(Stdio::null()).stderr(Stdio::null());
    command
        .spawn()
        .map_err(|error| format!("无法启动 CodeDrobe watcher：{error}"))
}

fn codedrobe_command(args: &[&str]) -> Result<Command, String> {
    if let Some(runner) = find_stable_codedrobe() {
        let mut parts = vec![quote_cmd(runner.to_string_lossy().as_ref())];
        parts.extend(args.iter().map(|value| quote_cmd(value)));
        let mut command = Command::new("cmd.exe");
        command.args(["/d", "/s", "/c", &format!("\"{}\"", parts.join(" "))]);
        return Ok(command);
    }

    if let Some(npx) = find_on_path("npx.cmd") {
        let node_directory = npx
            .parent()
            .ok_or_else(|| "无法定位 Node.js 目录。".to_string())?;
        let node = node_directory.join("node.exe");
        let npx_cli = node_directory
            .join("node_modules")
            .join("npm")
            .join("bin")
            .join("npx-cli.js");
        if node.is_file() && npx_cli.is_file() {
            let mut command = Command::new(node);
            command
                .arg(npx_cli)
                .args(["--yes", "@codedrobe/core@latest"])
                .args(args);
            return Ok(command);
        }
    }

    Err("CodeDrobe 需要 Node.js / npx。".to_string())
}

fn codedrobe_error(output: &Output) -> String {
    let stderr = decode_command_output(&output.stderr);
    let stdout = decode_command_output(&output.stdout);
    stderr
        .lines()
        .chain(stdout.lines())
        .rev()
        .find(|line| line.trim_start().starts_with("[codedrobe]"))
        .or_else(|| stderr.lines().rev().find(|line| !line.trim().is_empty()))
        .or_else(|| stdout.lines().rev().find(|line| !line.trim().is_empty()))
        .unwrap_or("CodeDrobe 操作失败。")
        .trim()
        .to_string()
}

fn decode_command_output(bytes: &[u8]) -> String {
    if let Ok(text) = std::str::from_utf8(bytes) {
        return text.to_string();
    }
    let (text, _, _) = GBK.decode(bytes);
    text.into_owned()
}

fn find_stable_codedrobe() -> Option<PathBuf> {
    find_on_path("codedrobe.cmd").filter(|path| !is_ephemeral_npx_path(path))
}

fn is_ephemeral_npx_path(path: &Path) -> bool {
    path.components().any(|component| {
        component
            .as_os_str()
            .to_string_lossy()
            .eq_ignore_ascii_case("_npx")
    })
}

fn find_on_path(file_name: &str) -> Option<PathBuf> {
    env::var_os("PATH").and_then(|path| {
        env::split_paths(&path)
            .map(|directory| directory.join(file_name))
            .find(|candidate| candidate.is_file())
    })
}

fn find_codex_executable() -> Option<PathBuf> {
    let script = "$p=@(Get-AppxPackage -Name 'OpenAI.Codex';Get-AppxPackage -Name 'OpenAI.ChatGPT-Desktop')|Sort-Object Version -Descending|Select-Object -First 1;if($p){$p.InstallLocation}";
    let output = Command::new("powershell.exe")
        .args([
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            script,
        ])
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    let location = String::from_utf8_lossy(&output.stdout).trim().to_string();
    ["Codex.exe", "ChatGPT.exe"]
        .iter()
        .map(|name| PathBuf::from(&location).join("app").join(name))
        .find(|candidate| candidate.is_file())
}

fn is_codex_running() -> bool {
    Command::new("powershell.exe")
        .args([
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            "$p=Get-Process -Name Codex,ChatGPT -ErrorAction SilentlyContinue|Where-Object{$_.MainWindowHandle -ne 0 -and $_.Path -like '*\\WindowsApps\\OpenAI.Codex_*\\app\\*'};if($p){exit 0};exit 1",
        ])
        .status()
        .map(|status| status.success())
        .unwrap_or(false)
}

fn validate_slug(value: &str) -> Result<(), String> {
    if !value.is_empty()
        && value.chars().all(|character| {
            character.is_ascii_lowercase() || character.is_ascii_digit() || character == '-'
        })
    {
        Ok(())
    } else {
        Err("主题标识无效。".to_string())
    }
}

fn safe_file_part(value: &str) -> String {
    value
        .chars()
        .map(|character| {
            if character.is_ascii_alphanumeric() || matches!(character, '.' | '-' | '_') {
                character
            } else {
                '_'
            }
        })
        .collect()
}

fn quote_cmd(value: &str) -> String {
    format!("\"{}\"", value.replace('"', "\"\""))
}

pub fn run() {
    let app = tauri::Builder::default()
        .manage(WatcherState::default())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            if cfg!(debug_assertions) {
                app.handle().plugin(
                    tauri_plugin_log::Builder::default()
                        .level(log::LevelFilter::Info)
                        .build(),
                )?;
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_app_state,
            get_codex_running,
            test_proxy,
            list_themes,
            download_theme,
            apply_theme,
            launch_codex,
            restore_theme
        ])
        .build(tauri::generate_context!())
        .expect("error while building Codex Proxy Launch Deck");

    app.run(|app_handle, event| {
        if matches!(event, tauri::RunEvent::Exit) {
            let watcher = app_handle.state::<WatcherState>();
            let _ = stop_watcher(&watcher);
        }
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_marketplace_slugs() {
        assert!(validate_slug("pastel-morning").is_ok());
        assert!(validate_slug("../theme").is_err());
        assert!(validate_slug("Theme Name").is_err());
    }

    #[test]
    fn sanitizes_theme_versions() {
        assert_eq!(safe_file_part("1.2.58-local"), "1.2.58-local");
        assert_eq!(safe_file_part("1.0/preview"), "1.0_preview");
    }

    #[test]
    fn rejects_ephemeral_npx_runner_shims() {
        assert!(is_ephemeral_npx_path(Path::new(
            r"C:\Users\me\AppData\Local\npm-cache\_npx\123\node_modules\.bin\codedrobe.cmd"
        )));
        assert!(!is_ephemeral_npx_path(Path::new(
            r"C:\Users\me\AppData\Roaming\npm\codedrobe.cmd"
        )));
    }

    #[test]
    fn decodes_windows_gbk_errors() {
        let (encoded, _, _) = GBK.encode("系统找不到指定的路径。");
        assert_eq!(decode_command_output(&encoded), "系统找不到指定的路径。");
    }

    #[test]
    #[ignore = "requires Node.js and marketplace network access"]
    fn codedrobe_cli_can_search_the_store() {
        let output = run_codedrobe(&["theme", "search", "qq", "--limit", "1", "--json"], None)
            .expect("CodeDrobe store search should succeed");
        let stdout = String::from_utf8_lossy(&output.stdout);
        assert!(stdout.contains("\"action\": \"theme-search\""));
        assert!(stdout.contains("\"themes\""));
    }
}
