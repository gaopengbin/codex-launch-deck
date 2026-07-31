use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use encoding_rs::GBK;
use image::{imageops::FilterType, DynamicImage, ImageBuffer, Rgb};
use reqwest::{Client, Url};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashMap,
    env,
    ffi::OsStr,
    fs,
    io::{BufRead, BufReader, Write},
    net::{TcpListener, TcpStream, ToSocketAddrs},
    path::{Path, PathBuf},
    process::{Child, Command, Output, Stdio},
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};
use tauri::{AppHandle, Manager, State};
use tungstenite::{connect as connect_websocket, Message};

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

const CODEDROBE_BASE: &str = "https://codedrobe.app";
const CODEDROBE_PORT: u16 = 9335;
const BUNDLED_THEME_FILE: &str = "miku-future-beats-1.2.1.codedrobe-theme";
const AI_THEME_COMPONENT_COVERAGE_REFERENCE: &str = r#"

/* Launch Deck coverage contract for Codex 26.721+.
   Preserve and redesign these semantic mounts instead of removing them. */
:root.codedrobe-host-codex {
  --theme-overlay: var(--theme-surface-strong);
  --theme-overlay-border: var(--theme-border);
  --color-token-bg-tertiary: var(--theme-surface-soft) !important;
  --color-token-text-tertiary: var(--theme-muted) !important;
  --color-token-description-foreground: var(--theme-muted) !important;
  --color-token-icon-foreground: var(--theme-text) !important;
  --color-token-border-default: var(--theme-overlay-border) !important;
  --color-token-border-heavy: var(--theme-overlay-border) !important;
  --color-token-toolbar-hover-background: var(--theme-accent-soft) !important;
  --color-token-dropdown-background: var(--theme-overlay) !important;
  --color-token-dropdown-foreground: var(--theme-text) !important;
  --color-token-menu-background: var(--theme-overlay) !important;
  --color-token-menu-border: var(--theme-overlay-border) !important;
  --color-token-conversation-summary-leading: var(--theme-muted) !important;
  --color-token-conversation-summary-trailing: var(--theme-muted) !important;
}

/* Right-side output/sources summary panel. */
html.codedrobe-host-codex .top-\(--thread-floating-content-top-inset\) .bg-token-dropdown-background {
  border: 1px solid var(--theme-overlay-border) !important;
  background: var(--theme-overlay) !important;
  color: var(--theme-text) !important;
  box-shadow: 0 18px 44px var(--theme-shadow) !important;
  backdrop-filter: blur(20px) saturate(1.08);
}

html.codedrobe-host-codex .group\/summary-panel-item {
  color: var(--theme-text) !important;
}

/* Radix portals used by tooltips, menus, select popovers, and context menus. */
html.codedrobe-host-codex [data-radix-popper-content-wrapper] > [data-side],
html.codedrobe-host-codex :is([role="tooltip"], [role="menu"], [role="listbox"]) {
  border: 1px solid var(--theme-overlay-border) !important;
  background: var(--theme-overlay) !important;
  color: var(--theme-text) !important;
  box-shadow: 0 12px 34px var(--theme-shadow) !important;
  backdrop-filter: blur(18px) saturate(1.08);
}

html.codedrobe-host-codex :is([role="menuitem"], [role="option"]):is(:hover, [data-highlighted]) {
  background: var(--theme-accent-soft) !important;
  color: var(--theme-text) !important;
}

/* Modal and non-modal dialogs must share the same surface language. */
html.codedrobe-host-codex [role="dialog"] {
  border-color: var(--theme-overlay-border) !important;
  background-color: var(--theme-overlay) !important;
  color: var(--theme-text) !important;
  box-shadow: 0 22px 60px var(--theme-shadow) !important;
}
"#;
#[cfg(target_os = "windows")]
const CREATE_NO_WINDOW: u32 = 0x08000000;

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
    theme_appearances: HashMap<String, String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ActionResult {
    message: String,
    theme_path: Option<String>,
    warning: bool,
}

#[derive(Clone, Default)]
struct AiThemeState {
    jobs: Arc<Mutex<HashMap<String, AiThemeJob>>>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct AiThemeCapability {
    codex_cli_available: bool,
    skill_installed: bool,
    skill_path: Option<String>,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AiThemeRequest {
    prompt: String,
    appearance: String,
    visual_mode: String,
    image_path: Option<String>,
    proxy: ProxyConfig,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct AiThemeJob {
    id: String,
    status: String,
    phase: String,
    progress: u8,
    logs: Vec<String>,
    error: Option<String>,
    theme: Option<MarketplaceTheme>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct GeneratedThemeRecord {
    id: String,
    slug: String,
    display_name: String,
    version: String,
    #[serde(default)]
    tagline: String,
    file_name: String,
    #[serde(default)]
    appearance_mode: String,
    #[serde(default)]
    cover_data_url: String,
}

#[derive(Clone, Default)]
struct WatcherState {
    child: Arc<Mutex<Option<Child>>>,
    port: Arc<Mutex<Option<u16>>>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct WatcherRecord {
    pid: u32,
    port: u16,
    theme_path: String,
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
    #[serde(default)]
    generated: bool,
    #[serde(default)]
    appearance_mode: Option<String>,
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
            theme_appearances: cached_theme_appearances(&cache),
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
async fn get_ai_theme_capability() -> Result<AiThemeCapability, String> {
    Ok(ai_theme_capability())
}

#[tauri::command]
async fn install_ai_theme_skill() -> Result<AiThemeCapability, String> {
    tauri::async_runtime::spawn_blocking(|| {
        if ai_theme_capability().skill_installed {
            return Ok(ai_theme_capability());
        }
        let npx = find_on_path("npx.cmd")
            .ok_or_else(|| "安装主题能力需要 Node.js / npx。".to_string())?;
        let output = background_command(npx)
            .args([
                "skills",
                "add",
                "CodeDrobe/skills",
                "--skill",
                "codedrobe-theme",
                "--global",
                "--agent",
                "codex",
                "--yes",
            ])
            .output()
            .map_err(|error| format!("无法启动 Skill 安装器：{error}"))?;
        if !output.status.success() {
            return Err(format!(
                "主题能力安装失败：{}",
                decode_command_output(&output.stderr).trim()
            ));
        }
        let capability = ai_theme_capability();
        if !capability.skill_installed {
            return Err("安装命令已结束，但没有找到 codedrobe-theme Skill。".to_string());
        }
        Ok(capability)
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn list_generated_themes() -> Result<Vec<MarketplaceTheme>, String> {
    tauri::async_runtime::spawn_blocking(|| {
        Ok(load_generated_theme_records()?
            .into_iter()
            .map(generated_record_to_theme)
            .collect())
    })
    .await
    .map_err(|error| error.to_string())?
}

#[tauri::command]
async fn start_ai_theme_generation(
    jobs: State<'_, AiThemeState>,
    request: AiThemeRequest,
) -> Result<AiThemeJob, String> {
    let prompt = request.prompt.trim().to_string();
    if prompt.chars().count() < 8 {
        return Err("请再详细描述一下想要的颜色、氛围或视觉风格。".to_string());
    }
    if prompt.chars().count() > 4000 {
        return Err("主题描述不能超过 4000 个字符。".to_string());
    }
    let appearance = request.appearance.trim().to_ascii_lowercase();
    if !matches!(appearance.as_str(), "light" | "dark") {
        return Err("请选择浅色基底或深色基底。".to_string());
    }
    let visual_mode = request.visual_mode.trim().to_ascii_lowercase();
    if !matches!(visual_mode.as_str(), "css" | "upload" | "ai") {
        return Err("请选择纯 CSS、本地图片或 AI 背景模式。".to_string());
    }
    let image_path = if visual_mode == "upload" {
        Some(validate_reference_image_path(
            request
                .image_path
                .as_deref()
                .ok_or_else(|| "请选择一张本地背景图片。".to_string())?,
        )?)
    } else {
        None
    };
    let capability = ai_theme_capability();
    if !capability.codex_cli_available {
        return Err("未找到 Codex CLI，请先安装并登录 Codex。".to_string());
    }
    if !capability.skill_installed {
        return Err("请先安装 CodeDrobe 主题创作能力。".to_string());
    }
    let proxy = verify_proxy(&request.proxy)
        .is_ok()
        .then_some(request.proxy);

    let id = format!(
        "{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_err(|error| error.to_string())?
            .as_millis()
    );
    let job = AiThemeJob {
        id: id.clone(),
        status: "running".to_string(),
        phase: "准备独立创作空间".to_string(),
        progress: 4,
        logs: vec!["任务已创建，正在连接本机 Codex…".to_string()],
        error: None,
        theme: None,
    };
    jobs.jobs
        .lock()
        .map_err(|_| "AI 任务状态不可用。".to_string())?
        .insert(id.clone(), job.clone());
    let shared_jobs = jobs.jobs.clone();
    thread::spawn(move || {
        run_ai_theme_job(
            shared_jobs,
            id,
            prompt,
            appearance,
            visual_mode,
            image_path,
            proxy,
        )
    });
    Ok(job)
}

#[tauri::command]
async fn get_ai_theme_job(jobs: State<'_, AiThemeState>, id: String) -> Result<AiThemeJob, String> {
    jobs.jobs
        .lock()
        .map_err(|_| "AI 任务状态不可用。".to_string())?
        .get(&id)
        .cloned()
        .ok_or_else(|| "没有找到这个 AI 创作任务。".to_string())
}

fn validate_reference_image_path(value: &str) -> Result<PathBuf, String> {
    let path = fs::canonicalize(value).map_err(|error| format!("无法读取所选背景图片：{error}"))?;
    let metadata = fs::metadata(&path).map_err(|error| format!("无法读取图片信息：{error}"))?;
    if !metadata.is_file() {
        return Err("所选背景图片不是文件。".to_string());
    }
    if metadata.len() > 30 * 1024 * 1024 {
        return Err("背景图片不能超过 30 MB。".to_string());
    }
    let extension = path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase();
    if !matches!(extension.as_str(), "png" | "jpg" | "jpeg" | "webp" | "gif") {
        return Err("仅支持 PNG、JPG、WebP 或 GIF 背景图片。".to_string());
    }
    Ok(path)
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
        let appearance = theme_package_appearance(&theme_path);
        let selected_port = select_cdp_port()?;
        set_active_port(watcher, selected_port)?;
        let port = selected_port.to_string();
        let path = theme_path.to_string_lossy().into_owned();
        let apply_args = ["apply", "--app", "codex", "--port", &port, "--theme", &path];
        let retry_args = [
            "apply",
            "--app",
            "codex",
            "--port",
            &port,
            "--theme",
            &path,
            "--no-launch",
        ];
        if let Err(error) = run_codedrobe_apply_with_retry(
            &apply_args,
            &retry_args,
            Some(&request.proxy),
            selected_port,
        ) {
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
        verify_codex_appearance(appearance.as_deref())?;

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
        if let Err(error) = set_watcher(watcher, child, selected_port, &path) {
            return Ok(ActionResult {
                message: format!("Codex 与主题已启动，但无法管理主题守护进程：{error}"),
                theme_path: Some(path),
                warning: true,
            });
        }
        let runtime_warning = appearance
            .as_deref()
            .and_then(|expected| sync_codex_runtime_appearance(selected_port, expected).err());
        Ok(ActionResult {
            message: if let Some(error) = runtime_warning.as_deref() {
                format!(
                    "Codex 与主题 {} 已启动，但原生深浅基底未能自动同步：{error}。请在外观设置中手动切换一次。",
                    theme.slug
                )
            } else {
                match appearance.as_deref() {
                    Some("dark") => format!(
                        "Codex 已通过代理启动，并应用主题 {}（深色基底 · CDP {selected_port}）",
                        theme.slug
                    ),
                    Some("light") => format!(
                        "Codex 已通过代理启动，并应用主题 {}（浅色基底 · CDP {selected_port}）",
                        theme.slug
                    ),
                    _ => format!(
                        "Codex 已通过代理启动，并应用主题 {}（CDP {selected_port}）",
                        theme.slug
                    ),
                }
            },
            theme_path: Some(path),
            warning: runtime_warning.is_some(),
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
    let appearance = theme_package_appearance(&theme_path);
    let selected_port = active_cdp_port(watcher)?;
    let port = selected_port.to_string();

    stop_watcher(watcher)?;
    let apply_args = [
        "apply",
        "--app",
        "codex",
        "--port",
        &port,
        "--theme",
        &path,
        "--no-launch",
    ];
    run_codedrobe_apply_with_retry(
        &apply_args,
        &apply_args,
        Some(proxy),
        selected_port,
    )
    .map_err(|error| {
        format!(
            "{error}。如果 Codex 不是由本启动器启动，请退出一次并通过启动器重新启动；之后即可无重启切换主题。"
        )
    })?;
    verify_codex_appearance(appearance.as_deref())?;
    start_watcher(watcher, selected_port, &path, proxy)?;
    let runtime_warning = appearance
        .as_deref()
        .and_then(|expected| sync_codex_runtime_appearance(selected_port, expected).err());

    Ok(ActionResult {
        message: if let Some(error) = runtime_warning.as_deref() {
            format!(
                "主题 {} 已注入，但原生深浅基底未能自动同步：{error}。请在外观设置中手动切换一次。",
                theme.slug
            )
        } else {
            match appearance.as_deref() {
                Some("dark") => {
                    format!("主题 {} 已即时应用，Codex 已切换为深色基底", theme.slug)
                }
                Some("light") => {
                    format!("主题 {} 已即时应用，Codex 已切换为浅色基底", theme.slug)
                }
                _ => format!("主题 {} 已即时应用，无需重启 Codex", theme.slug),
            }
        },
        theme_path: Some(path),
        warning: runtime_warning.is_some(),
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
    let output = background_command("powershell.exe")
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
    set_watcher(watcher, child, selected_port, theme_path)
}

fn set_watcher(
    watcher: &WatcherState,
    mut child: Child,
    port: u16,
    theme_path: &str,
) -> Result<(), String> {
    let record = WatcherRecord {
        pid: child.id(),
        port,
        theme_path: theme_path.to_string(),
    };
    if let Err(error) = save_watcher_record(&record) {
        let _ = terminate_watcher_process(&mut child);
        return Err(error);
    }
    let mut active = match watcher.child.lock() {
        Ok(active) => active,
        Err(_) => {
            let _ = terminate_watcher_process(&mut child);
            let _ = clear_watcher_record();
            return Err("CodeDrobe watcher 状态不可用。".to_string());
        }
    };
    *active = Some(child);
    Ok(())
}

fn stop_watcher(watcher: &WatcherState) -> Result<(), String> {
    let mut active = watcher
        .child
        .lock()
        .map_err(|_| "CodeDrobe watcher 状态不可用。".to_string())?;
    let mut stopped_in_memory = false;
    if let Some(mut child) = active.take() {
        stopped_in_memory = true;
        match child.try_wait() {
            Ok(Some(_)) => {}
            Ok(None) => {
                terminate_watcher_process(&mut child)?;
            }
            Err(error) => return Err(format!("无法检查 CodeDrobe watcher：{error}")),
        }
    }
    drop(active);
    if stopped_in_memory {
        clear_watcher_record()?;
    } else {
        terminate_persisted_watcher()?;
    }
    Ok(())
}

fn watcher_record_path() -> Result<PathBuf, String> {
    Ok(app_data_directory()?.join("codedrobe-watcher.json"))
}

fn save_watcher_record(record: &WatcherRecord) -> Result<(), String> {
    let path = watcher_record_path()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)
            .map_err(|error| format!("无法创建 watcher 状态目录：{error}"))?;
    }
    let bytes = serde_json::to_vec_pretty(record)
        .map_err(|error| format!("无法保存 watcher 状态：{error}"))?;
    fs::write(path, bytes).map_err(|error| format!("无法保存 watcher 状态：{error}"))
}

fn clear_watcher_record() -> Result<(), String> {
    let path = watcher_record_path()?;
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(format!("无法清理 watcher 状态：{error}")),
    }
}

fn terminate_persisted_watcher() -> Result<(), String> {
    let path = watcher_record_path()?;
    if !path.is_file() {
        return Ok(());
    }
    let bytes = fs::read(&path).map_err(|error| format!("无法读取 watcher 状态：{error}"))?;
    let record: WatcherRecord =
        serde_json::from_slice(&bytes).map_err(|error| format!("watcher 状态文件无效：{error}"))?;
    let Some(command_line) = watcher_process_command_line(record.pid) else {
        return clear_watcher_record();
    };
    if !is_expected_watcher_command(&command_line, &record) {
        clear_watcher_record()?;
        return Err(format!(
            "旧 watcher PID {} 已被其他程序占用，已跳过终止。",
            record.pid
        ));
    }
    terminate_process_tree(record.pid)?;
    clear_watcher_record()
}

#[cfg(target_os = "windows")]
fn watcher_process_command_line(pid: u32) -> Option<String> {
    let script = format!(
        "$p=Get-CimInstance Win32_Process -Filter \"ProcessId = {pid}\" -ErrorAction SilentlyContinue;if($p){{[Console]::Out.Write($p.CommandLine)}}"
    );
    let output = background_command("powershell.exe")
        .args([
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            &script,
        ])
        .output()
        .ok()?;
    output
        .status
        .success()
        .then(|| decode_command_output(&output.stdout))
        .filter(|value| !value.trim().is_empty())
}

#[cfg(not(target_os = "windows"))]
fn watcher_process_command_line(_pid: u32) -> Option<String> {
    None
}

fn is_expected_watcher_command(command_line: &str, record: &WatcherRecord) -> bool {
    let command_line = command_line.to_ascii_lowercase();
    let theme_path = record.theme_path.to_ascii_lowercase();
    command_line.contains("codedrobe")
        && command_line.contains("apply")
        && command_line.contains("--app codex")
        && command_line.contains("--watch")
        && command_line.contains(&format!("--port {}", record.port))
        && command_line.contains(&theme_path)
}

#[cfg(target_os = "windows")]
fn terminate_process_tree(pid: u32) -> Result<(), String> {
    let output = background_command("taskkill.exe")
        .args(["/PID", &pid.to_string(), "/T", "/F"])
        .output()
        .map_err(|error| format!("无法停止旧的 CodeDrobe watcher：{error}"))?;
    if output.status.success() {
        Ok(())
    } else {
        Err(format!(
            "无法停止旧的 CodeDrobe watcher：{}",
            decode_command_output(&output.stderr).trim()
        ))
    }
}

#[cfg(not(target_os = "windows"))]
fn terminate_process_tree(_pid: u32) -> Result<(), String> {
    Err("当前平台不支持回收遗留的 CodeDrobe watcher。".to_string())
}

fn terminate_watcher_process(child: &mut Child) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        if let Err(error) = terminate_process_tree(child.id()) {
            if child.try_wait().ok().flatten().is_none() {
                return Err(error);
            }
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

fn app_data_directory() -> Result<PathBuf, String> {
    env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .map(|path| path.join("CodexProxyLaunchDeck"))
        .ok_or_else(|| "无法定位 LOCALAPPDATA。".to_string())
}

fn ai_theme_capability() -> AiThemeCapability {
    let skill_path = ai_theme_skill_path();
    AiThemeCapability {
        codex_cli_available: find_codex_cli().is_some(),
        skill_installed: skill_path.is_some(),
        skill_path: skill_path.map(|path| path.to_string_lossy().into_owned()),
    }
}

fn ai_theme_skill_path() -> Option<PathBuf> {
    let user_profile = env::var_os("USERPROFILE").map(PathBuf::from)?;
    [
        user_profile
            .join(".codex")
            .join("skills")
            .join("codedrobe-theme")
            .join("SKILL.md"),
        user_profile
            .join(".agents")
            .join("skills")
            .join("codedrobe-theme")
            .join("SKILL.md"),
    ]
    .into_iter()
    .find(|path| path.is_file())
}

fn update_ai_job(
    jobs: &Arc<Mutex<HashMap<String, AiThemeJob>>>,
    id: &str,
    update: impl FnOnce(&mut AiThemeJob),
) {
    if let Ok(mut jobs) = jobs.lock() {
        if let Some(job) = jobs.get_mut(id) {
            update(job);
            if job.logs.len() > 80 {
                job.logs.drain(..job.logs.len() - 80);
            }
        }
    }
}

fn fail_ai_job(jobs: &Arc<Mutex<HashMap<String, AiThemeJob>>>, id: &str, error: String) {
    update_ai_job(jobs, id, |job| {
        job.status = "failed".to_string();
        job.phase = "创作失败".to_string();
        job.error = Some(error.clone());
        job.logs.push(error);
    });
}

fn run_ai_theme_job(
    jobs: Arc<Mutex<HashMap<String, AiThemeJob>>>,
    id: String,
    user_prompt: String,
    appearance: String,
    visual_mode: String,
    image_path: Option<PathBuf>,
    proxy: Option<ProxyConfig>,
) {
    if let Err(error) = run_ai_theme_job_inner(
        &jobs,
        &id,
        &user_prompt,
        &appearance,
        &visual_mode,
        image_path.as_deref(),
        proxy.as_ref(),
    ) {
        fail_ai_job(&jobs, &id, error);
    }
}

fn run_ai_theme_job_inner(
    jobs: &Arc<Mutex<HashMap<String, AiThemeJob>>>,
    id: &str,
    user_prompt: &str,
    appearance: &str,
    visual_mode: &str,
    image_path: Option<&Path>,
    proxy: Option<&ProxyConfig>,
) -> Result<(), String> {
    let work_dir = app_data_directory()?.join("ai-themes").join(id);
    fs::create_dir_all(&work_dir).map_err(|error| format!("无法创建 AI 创作目录：{error}"))?;
    let source_dir = work_dir.join("generated-theme");
    fs::create_dir_all(&source_dir).map_err(|error| format!("无法创建主题源码目录：{error}"))?;
    let authoring_reference = stage_ai_authoring_reference(&work_dir)?;
    let manifest_path = source_dir.join("theme.json");
    let output_path = work_dir.join("generated.codedrobe-theme");
    let last_message_path = work_dir.join("codex-last-message.txt");
    let staged_image = if let Some(source) = image_path {
        let extension = source
            .extension()
            .and_then(|value| value.to_str())
            .unwrap_or("png");
        let destination = work_dir.join(format!("reference-background.{extension}"));
        fs::copy(source, &destination)
            .map_err(|error| format!("无法复制背景图片到创作目录：{error}"))?;
        Some(destination)
    } else {
        None
    };
    let instruction = build_ai_theme_prompt(
        user_prompt,
        appearance,
        visual_mode,
        staged_image.as_deref(),
        &authoring_reference,
        &manifest_path,
    );
    let codex = find_codex_cli().ok_or_else(|| {
        "未找到可执行的 Codex CLI。Microsoft Store 内置副本不能由外部程序直接启动，请安装 Codex CLI 或 OpenAI VS Code 扩展。"
            .to_string()
    })?;

    update_ai_job(jobs, id, |job| {
        job.phase = "Codex 正在设计主题".to_string();
        job.progress = 12;
        job.logs
            .push("已启用 workspace-write 沙箱，开始生成主题。".to_string());
    });

    let mut command = codex_cli_command(&codex);
    if let Some(proxy) = proxy {
        apply_proxy_environment(&mut command, proxy);
        update_ai_job(jobs, id, |job| {
            job.logs
                .push("已复用启动器代理连接 Codex 服务。".to_string());
        });
    }
    let mut child = command
        .args([
            "exec",
            "--json",
            "--color",
            "never",
            "--sandbox",
            "workspace-write",
            "--skip-git-repo-check",
            "--ephemeral",
            "-C",
        ])
        .arg(&work_dir)
        .args(["--output-last-message"])
        .arg(&last_message_path)
        .arg("-")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| format!("无法启动 Codex CLI：{error}"))?;

    child
        .stdin
        .take()
        .ok_or_else(|| "无法写入 Codex 指令。".to_string())?
        .write_all(instruction.as_bytes())
        .map_err(|error| format!("无法写入 Codex 指令：{error}"))?;

    if let Some(stderr) = child.stderr.take() {
        thread::spawn(move || {
            for _ in BufReader::new(stderr).lines().map_while(Result::ok) {
                // Codex JSONL stdout carries actionable events. PowerShell and
                // sandbox retry stacks on stderr are intentionally drained but
                // not shown as user-facing progress.
            }
        });
    }

    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| "无法读取 Codex 进度。".to_string())?;
    for line in BufReader::new(stdout).lines() {
        let line = line.map_err(|error| format!("读取 Codex 进度失败：{error}"))?;
        if let Some(message) = codex_event_summary(&line) {
            update_ai_job(jobs, id, |job| {
                job.progress = job.progress.saturating_add(3).min(82);
                job.logs.push(message);
            });
        }
    }
    let status = child
        .wait()
        .map_err(|error| format!("等待 Codex 完成时出错：{error}"))?;
    if !status.success() {
        return Err(format!("Codex 生成未完成（退出码 {status}）。"));
    }
    if !manifest_path.is_file() {
        return Err(
            "Codex 已结束，但没有生成约定的 theme.json 源码。请换一种描述后重试。".to_string(),
        );
    }
    if visual_mode == "upload" {
        validate_generated_reference_brief(&source_dir.join("reference-brief.json"), appearance)?;
    }

    update_ai_job(jobs, id, |job| {
        job.phase = "打包并校验主题".to_string();
        job.progress = 84;
        job.logs
            .push("主题源码已生成，Launch Deck 正在统一打包…".to_string());
    });
    let manifest_text = manifest_path.to_string_lossy().into_owned();
    let output_text = output_path.to_string_lossy().into_owned();
    run_codedrobe(
        &["theme", "pack", &manifest_text, "--output", &output_text],
        None,
    )?;
    update_ai_job(jobs, id, |job| {
        job.progress = 90;
        job.logs
            .push("CodeDrobe 打包完成，正在安全校验…".to_string());
    });
    let record = inspect_generated_theme(
        &output_path,
        appearance,
        visual_mode,
        staged_image.as_deref(),
    )?;
    let cache = theme_cache_directory()?;
    fs::create_dir_all(&cache).map_err(|error| format!("无法创建主题缓存：{error}"))?;
    let file_name = format!(
        "{}-{}.codedrobe-theme",
        record.slug,
        safe_file_part(&record.version)
    );
    fs::copy(&output_path, cache.join(&file_name))
        .map_err(|error| format!("无法导入生成的主题：{error}"))?;
    let mut record = record;
    record.file_name = file_name;
    save_generated_theme_record(record.clone())?;
    let theme = generated_record_to_theme(record);
    update_ai_job(jobs, id, |job| {
        job.status = "completed".to_string();
        job.phase = "已加入主题库".to_string();
        job.progress = 100;
        job.logs.push("校验通过，主题已加入“已安装”。".to_string());
        job.theme = Some(theme);
    });
    Ok(())
}

fn stage_ai_authoring_reference(work_dir: &Path) -> Result<PathBuf, String> {
    let skill =
        ai_theme_skill_path().ok_or_else(|| "未找到 CodeDrobe 主题创作 Skill。".to_string())?;
    let source = skill
        .parent()
        .ok_or_else(|| "CodeDrobe Skill 路径无效。".to_string())?
        .join("assets")
        .join("theme-starter")
        .join("codex.css");
    if !source.is_file() {
        return Err("CodeDrobe Skill 缺少 Codex 主题模板，请重新安装主题创作能力。".to_string());
    }
    let reference_dir = work_dir.join("authoring-reference");
    fs::create_dir_all(&reference_dir).map_err(|error| format!("无法创建主题模板目录：{error}"))?;
    let destination = reference_dir.join("codex.css");
    fs::copy(&source, &destination).map_err(|error| format!("无法准备 Codex 主题模板：{error}"))?;
    fs::OpenOptions::new()
        .append(true)
        .open(&destination)
        .and_then(|mut file| file.write_all(AI_THEME_COMPONENT_COVERAGE_REFERENCE.as_bytes()))
        .map_err(|error| format!("无法追加 Codex 组件覆盖模板：{error}"))?;
    Ok(destination)
}

fn build_ai_theme_prompt(
    user_prompt: &str,
    appearance: &str,
    visual_mode: &str,
    staged_image: Option<&Path>,
    authoring_reference: &Path,
    manifest_path: &Path,
) -> String {
    let appearance_label = if appearance == "dark" {
        "深色（dark）"
    } else {
        "浅色（light）"
    };
    let visual_instruction = match visual_mode {
        "upload" => format!(
            "这张图片只作为 AI 重新创作的参考图：{}。先用 view_image 查看它，并在 generated-theme/reference-brief.json 写入：appearance（必须是用户选择的基底）、anchors（至少 5 个具体视觉锚点）、palette、composition，以及初始为空的 preservedAnchors。锚点必须覆盖参考图的主要主体/人物数量与层级、构图位置、标志性造型或物件、色彩分区和光影气氛，不能只写抽象的‘赛博朋克’或‘蓝紫色’。再调用 $imagegen 重新生成一张适合桌面工作区的 16:9 主视觉背景；除可读文字、Logo、水印和伪 UI 外，至少保留 3 个锚点，参考图有明确主体时不得擅自改成无人物空景。生成后必须用 view_image 检查 hero，并把实际保留的锚点写入 preservedAnchors；若不足 3 个或深浅基底不匹配，必须针对缺失项再生成一次后复查。不要直接复制、打包或原样使用参考图。将主视觉保存到 generated-theme/assets/hero.png，在 theme.json 中命名为 hero，并在 Codex CSS 中通过 var(--codedrobe-image-hero) 引用。若该风格适合在侧栏、卡片或输入区延续细节，可再次调用 $imagegen 生成一张低对比度、无主体、可平铺的方形纹理 generated-theme/assets/texture.png，以 texture 命名并通过 var(--codedrobe-image-texture) 引用；不适合时不要为了凑数量生成纹理。所有生成图均不得包含可读文字、Logo、水印、边框或仿造交互控件。",
            staged_image
                .map(|path| path.to_string_lossy().into_owned())
                .unwrap_or_default()
        ),
        "ai" => "先根据用户描述整理配色、材质、光影和可复用视觉母题，再使用 $imagegen Skill 生成一张适合桌面工作区的 16:9 主视觉背景，保存为 generated-theme/assets/hero.png；在 theme.json 中以 hero 命名，并在 Codex CSS 中通过 var(--codedrobe-image-hero) 引用。若该风格适合在侧栏、卡片或输入区延续细节，可再次调用 $imagegen 生成一张低对比度、无主体、可平铺的方形纹理 generated-theme/assets/texture.png，以 texture 命名并通过 var(--codedrobe-image-texture) 引用；不适合时不要为了凑数量生成纹理。所有生成图均不得包含可读文字、Logo、水印、边框或仿造交互控件。".to_string(),
        _ => "使用纯 CSS 创作背景，只允许渐变、纹理、光影与几何装饰；不要在主题包中嵌入图片。".to_string(),
    };
    format!(
        r#"请使用 $codedrobe-theme Skill 为 Codex Desktop 创作一个可用的自定义主题。

用户的设计要求：
{user_prompt}

硬性要求：
- 目标应用只能是 codex；创建 declaration-only 主题，不接入任何模型或外部服务。
- 先完整阅读这个本地 Codex CSS 创作模板：{}。以其中已经验证过的语义选择器、Token 覆盖、响应式和可访问性结构为骨架重新设计，不要从空白 CSS 猜测 DOM。可以彻底更换配色、材质、圆角、阴影和装饰，但要保留语义挂载方式。
- 用户选择的 Codex 基底是：{appearance_label}。`targets.codex.options.baseTheme.mode` 必须精确为 `{appearance}`，CSS 的 `color-scheme` 也必须为 `{appearance}`；所有背景、文字、边框、代码块和原生控件都要按该基底保证对比度。
- 图片也必须原生适配基底：light 要生成高亮、浅色占主导、有大面积明亮留白的 hero，禁止生成暗夜图后依赖白色蒙层强行漂白；dark 要生成低亮度、深色占主导但主体清晰的 hero。CSS 蒙层只能辅助可读性，不能扭转图片本身的明暗方向。
- 背景素材模式：{visual_instruction}
- 不要只替换背景。把主视觉中的配色、材质和视觉母题延展为完整的界面语言：用 CSS 变量统一页面、侧栏、标题栏、卡片、按钮、输入区、消息、代码块、边框、阴影和焦点状态；用渐变、伪元素、边框、阴影或轻量动画制作小装饰细节。仅当图片确实比 CSS 更适合时才增加命名图片素材。
- 必须覆盖模板末尾的完整组件清单：右侧输出/来源摘要面板（thread floating content 与 summary-panel-item）、Tooltip、Popover、Dropdown、Menu、Select/Listbox、Dialog，以及它们的 hover、open、highlighted 和 focus-visible 状态。浮层不能继续使用与主题不一致的系统灰色，也不能只改文字不改表面、边框和阴影。
- 图片只能作为背景、纹理或非交互装饰，不能作为全窗口 UI 截图覆盖应用。所有装饰层必须 `pointer-events: none`，窄窗口要有响应式降级，并为动画提供 `prefers-reduced-motion` 处理。
- 图片模式下，hero 必须直接挂载到 `main.main-surface`、它的伪元素或模板中的首页 hero 节点，确保不会被 Codex 的不透明主表面遮住；body 可以有纹理，但不能是 hero 的唯一挂载点。
- 禁止用 `:where(aside)`、`:where(header)`、`:where(button)`、`[class*="card"]` 等全局宽泛选择器批量覆盖原生组件。侧栏、主工作区、标题栏和输入区至少分别使用 `aside.app-shell-left-panel`、`main.main-surface`、`header.app-header-tint` 和 `.composer-surface-chrome` 定向设计。
- 只在当前工作目录内创建或修改文件，不读取无关的用户文件。
- 不要启动、关闭或重启 Codex，不要应用主题，不连接 CDP 端口。
- 只生成 CodeDrobe 主题源码，不要运行 PowerShell、npx、npm、codedrobe 或任何打包/检查命令；Launch Deck 会在沙箱外统一打包和校验。
- 如果 targets.codex.options.rendererProfile 使用 codex-theme-v1，Codex CSS 必须包含独立规则 `#codedrobe-codex-skin-chrome {{ pointer-events: none !important; }}`；即使不展示 profile 装饰层也不能省略，这是防止装饰层拦截点击的安全契约。
- theme.json 必须精确写到这个绝对路径：{}；CSS 和图片都放在它所在的 generated-theme 目录内，并使用相对路径引用。
- 完成前只需检查源码文件存在且 JSON 语法有效，不要自行打包。
- 最后的回复简短说明主题名称和源码路径。
"#,
        authoring_reference.to_string_lossy(),
        manifest_path.to_string_lossy()
    )
}

fn codex_event_summary(line: &str) -> Option<String> {
    let value = serde_json::from_str::<serde_json::Value>(line).ok()?;
    let event_type = value.get("type")?.as_str()?;
    match event_type {
        "thread.started" => Some("Codex 会话已建立。".to_string()),
        "turn.started" => Some("正在理解设计要求并规划主题结构…".to_string()),
        "turn.completed" => Some("Codex 已完成主题创作步骤。".to_string()),
        "item.completed" => {
            let item = value.get("item")?;
            match item.get("type").and_then(|value| value.as_str()) {
                Some("agent_message") => Some("已完成一轮主题设计。".to_string()),
                Some("command_execution") => Some("已完成一个本地创作步骤。".to_string()),
                Some("file_change") => Some("已写入主题文件。".to_string()),
                _ => None,
            }
        }
        "error" => value
            .get("message")
            .and_then(|value| value.as_str())
            .map(|message| format!("Codex：{message}")),
        _ => None,
    }
}

fn inspect_generated_theme(
    path: &Path,
    expected_appearance: &str,
    visual_mode: &str,
    reference_image: Option<&Path>,
) -> Result<GeneratedThemeRecord, String> {
    let package_bytes = fs::read(path).map_err(|error| format!("无法读取生成的主题包：{error}"))?;
    let package: serde_json::Value = serde_json::from_slice(&package_bytes)
        .map_err(|error| format!("生成的主题包不是有效 JSON：{error}"))?;
    validate_generated_codex_contract(&package, expected_appearance)?;
    let reference_bytes = reference_image.and_then(|path| fs::read(path).ok());
    validate_generated_visual_contract(
        &package,
        expected_appearance,
        visual_mode,
        reference_bytes.as_deref(),
    )?;
    let path_text = path.to_string_lossy().into_owned();
    let output = run_codedrobe(&["theme", "inspect", &path_text, "--json"], None)?;
    let value: serde_json::Value = serde_json::from_slice(&output.stdout)
        .map_err(|error| format!("CodeDrobe 校验结果无法解析：{error}"))?;
    let theme = value
        .get("theme")
        .and_then(|value| value.as_object())
        .ok_or_else(|| "主题包缺少 theme 元数据。".to_string())?;
    let targets = value
        .get("targets")
        .and_then(|value| value.as_array())
        .ok_or_else(|| "主题包缺少 targets。".to_string())?;
    if !targets
        .iter()
        .any(|target| target.as_str() == Some("codex"))
    {
        return Err("生成的主题包不支持 Codex。".to_string());
    }
    let slug = theme
        .get("id")
        .and_then(|value| value.as_str())
        .ok_or_else(|| "主题包缺少 theme.id。".to_string())?
        .to_string();
    validate_slug(&slug)?;
    let display_name = theme
        .get("displayName")
        .and_then(|value| value.as_str())
        .filter(|value| !value.trim().is_empty())
        .unwrap_or(&slug)
        .to_string();
    let version = theme
        .get("version")
        .and_then(|value| value.as_str())
        .filter(|value| !value.trim().is_empty())
        .unwrap_or("0.1.0")
        .to_string();
    let tagline = theme
        .get("copy")
        .and_then(|value| value.get("tagline"))
        .and_then(|value| value.as_str())
        .unwrap_or("由 Codex 为你创作的本地主题。")
        .to_string();
    let cover_data_url = generated_cover_data_url(&package).unwrap_or_default();
    Ok(GeneratedThemeRecord {
        id: format!("generated-{slug}"),
        slug,
        display_name,
        version,
        tagline,
        file_name: String::new(),
        appearance_mode: expected_appearance.to_string(),
        cover_data_url,
    })
}

fn validate_generated_visual_contract(
    package: &serde_json::Value,
    expected_appearance: &str,
    visual_mode: &str,
    reference_bytes: Option<&[u8]>,
) -> Result<(), String> {
    let css = package
        .get("targets")
        .and_then(|value| value.get("codex"))
        .and_then(|value| value.get("css"))
        .and_then(|value| value.as_str())
        .unwrap_or_default();
    validate_generated_css_quality_contract(css, visual_mode)?;
    if visual_mode == "css" {
        return Ok(());
    }
    let images = package
        .get("assets")
        .and_then(|value| value.get("images"))
        .and_then(|value| value.as_object());
    if images.map(serde_json::Map::is_empty).unwrap_or(true) {
        return Err("选择了图片背景，但生成的主题包没有嵌入任何图片。".to_string());
    }
    let hero_bytes = images
        .and_then(|images| images.get("hero"))
        .and_then(|asset| asset.get("base64"))
        .and_then(|value| value.as_str())
        .and_then(|value| BASE64.decode(value).ok())
        .ok_or_else(|| "图片主题必须把有效主视觉以 hero 命名并嵌入主题包。".to_string())?;
    validate_generated_hero_appearance(&hero_bytes, expected_appearance)?;
    if !css.contains("var(--codedrobe-image-hero") {
        return Err(
            "主题包包含 hero，但 Codex CSS 没有通过 --codedrobe-image-hero 引用它。".to_string(),
        );
    }
    if !selector_block_references(css, "main.main-surface", "--codedrobe-image-hero")
        && !selector_block_references(css, "[role=\"main\"]", "--codedrobe-image-hero")
        && !selector_block_references(css, "[role='main']", "--codedrobe-image-hero")
    {
        return Err(
            "主视觉只被挂在不可见的外层：请把 --codedrobe-image-hero 直接用于 main.main-surface、它的伪元素或首页主内容节点。"
                .to_string(),
        );
    }
    if visual_mode == "upload" {
        if let Some(reference) = reference_bytes {
            if hero_bytes == reference {
                return Err("上传图片只能作为 AI 参考图，不能原样作为最终主题背景。".to_string());
            }
        }
    }
    Ok(())
}

fn validate_generated_hero_appearance(
    bytes: &[u8],
    expected_appearance: &str,
) -> Result<(), String> {
    let image = image::load_from_memory(bytes)
        .map_err(|error| format!("无法读取 AI 生成的 hero 图片：{error}"))?
        .thumbnail(128, 128)
        .to_rgb8();
    let pixel_count = image.width() as f64 * image.height() as f64;
    if pixel_count == 0.0 {
        return Err("AI 生成的 hero 图片没有有效像素。".to_string());
    }
    let luminance = image
        .pixels()
        .map(|pixel| {
            (0.2126 * f64::from(pixel[0])
                + 0.7152 * f64::from(pixel[1])
                + 0.0722 * f64::from(pixel[2]))
                / 255.0
        })
        .sum::<f64>()
        / pixel_count;
    if expected_appearance == "light" && luminance < 0.55 {
        return Err(format!(
            "hero 平均亮度仅为 {luminance:.2}，不适合浅色基底；请重新生成高亮浅色背景，不能依赖白色蒙层漂白。"
        ));
    }
    if expected_appearance == "dark" && luminance > 0.72 {
        return Err(format!(
            "hero 平均亮度为 {luminance:.2}，不适合深色基底；请重新生成深色占主导的背景。"
        ));
    }
    Ok(())
}

fn validate_generated_reference_brief(
    path: &Path,
    expected_appearance: &str,
) -> Result<(), String> {
    let bytes = fs::read(path).map_err(|_| {
        "上传参考图模式缺少 reference-brief.json，无法确认生成图保留了哪些视觉锚点。".to_string()
    })?;
    let brief: serde_json::Value = serde_json::from_slice(&bytes)
        .map_err(|error| format!("reference-brief.json 不是有效 JSON：{error}"))?;
    validate_generated_reference_brief_value(&brief, expected_appearance)
}

fn validate_generated_reference_brief_value(
    brief: &serde_json::Value,
    expected_appearance: &str,
) -> Result<(), String> {
    if brief.get("appearance").and_then(|value| value.as_str()) != Some(expected_appearance) {
        return Err("reference-brief.json 的 appearance 与用户选择的深浅基底不一致。".to_string());
    }
    let anchors = brief
        .get("anchors")
        .and_then(|value| value.as_array())
        .map(Vec::len)
        .unwrap_or(0);
    let preserved = brief
        .get("preservedAnchors")
        .and_then(|value| value.as_array())
        .map(Vec::len)
        .unwrap_or(0);
    if anchors < 5 || preserved < 3 {
        return Err(format!(
            "参考图视觉锚点不足：需要至少 5 个分析锚点并在 hero 中实际保留 3 个，当前为 {anchors}/{preserved}。"
        ));
    }
    Ok(())
}

fn validate_generated_css_quality_contract(css: &str, visual_mode: &str) -> Result<(), String> {
    for required in [
        "aside.app-shell-left-panel",
        "main.main-surface",
        "header.app-header-tint",
        ".composer-surface-chrome",
        "--color-token-dropdown-background",
        "--color-token-menu-background",
        "--thread-floating-content-top-inset",
        "group\\/summary-panel-item",
        "data-radix-popper-content-wrapper",
        "[role=\"tooltip\"]",
        "[role=\"dialog\"]",
    ] {
        if !css.contains(required) {
            return Err(format!(
                "生成的 CSS 缺少语义化 Codex 挂载点 {required}，无法保证主题完整显示。"
            ));
        }
    }
    for broad in [
        ":where(aside",
        ":where(header",
        ":where(button",
        ":where(main",
        "[class*=\"card\"]",
        "[class*='card']",
    ] {
        if css.contains(broad) {
            return Err(format!(
                "生成的 CSS 使用了过宽选择器 {broad}，可能污染原生控件，请改用模板中的语义选择器。"
            ));
        }
    }
    if visual_mode != "css" && !css.contains("--codedrobe-image-hero") {
        return Err("图片主题缺少可见的 hero 主视觉引用。".to_string());
    }
    Ok(())
}

fn selector_block_references(css: &str, selector: &str, reference: &str) -> bool {
    let mut remaining = css;
    while let Some(position) = remaining.find(selector) {
        let candidate = &remaining[position..];
        let Some(open) = candidate.find('{') else {
            return false;
        };
        if open > 512 || candidate[..open].contains('}') {
            remaining = &candidate[selector.len()..];
            continue;
        }
        if let Some(close) = candidate[open + 1..].find('}') {
            if candidate[open + 1..open + 1 + close].contains(reference) {
                return true;
            }
        }
        remaining = &candidate[selector.len()..];
    }
    false
}

fn generated_cover_data_url(package: &serde_json::Value) -> Option<String> {
    let embedded = package
        .get("assets")
        .and_then(|value| value.get("images"))
        .and_then(|value| value.as_object())
        .and_then(|images| {
            ["cover", "hero", "background"]
                .iter()
                .find_map(|name| images.get(*name))
                .or_else(|| images.values().next())
        })
        .and_then(|asset| asset.get("base64"))
        .and_then(|value| value.as_str())
        .and_then(|value| BASE64.decode(value).ok())
        .and_then(|bytes| image::load_from_memory(&bytes).ok());

    let preview = embedded
        .map(|image| {
            image
                .resize_to_fill(640, 360, FilterType::Lanczos3)
                .to_rgb8()
        })
        .unwrap_or_else(|| generated_palette_preview(package));
    let mut jpeg = Vec::new();
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut jpeg, 84)
        .encode_image(&DynamicImage::ImageRgb8(preview))
        .ok()?;
    Some(format!("data:image/jpeg;base64,{}", BASE64.encode(jpeg)))
}

fn generated_palette_preview(package: &serde_json::Value) -> ImageBuffer<Rgb<u8>, Vec<u8>> {
    let base = package
        .get("targets")
        .and_then(|value| value.get("codex"))
        .and_then(|value| value.get("options"))
        .and_then(|value| value.get("baseTheme"));
    let dark = base
        .and_then(|value| value.get("mode"))
        .and_then(|value| value.as_str())
        == Some("dark");
    let surface = theme_hex_color(base, "surface").unwrap_or(if dark {
        [19, 28, 26]
    } else {
        [246, 248, 241]
    });
    let accent = theme_hex_color(base, "accent").unwrap_or([25, 194, 181]);
    let ink =
        theme_hex_color(base, "ink").unwrap_or(if dark { [230, 242, 237] } else { [16, 33, 29] });
    ImageBuffer::from_fn(640, 360, |x, y| {
        let diagonal = ((x + y * 2) % 150) < 4;
        let glow = ((x as f32 - 510.0).powi(2) + (y as f32 - 70.0).powi(2)).sqrt();
        let accent_mix = ((1.0 - glow / 420.0).max(0.0) * 0.5) + if diagonal { 0.16 } else { 0.0 };
        let ink_mix = if y > 285 && ((x / 70) % 2 == 0) {
            0.06
        } else {
            0.0
        };
        Rgb(std::array::from_fn(|index| {
            let value = surface[index] as f32 * (1.0 - accent_mix - ink_mix)
                + accent[index] as f32 * accent_mix
                + ink[index] as f32 * ink_mix;
            value.clamp(0.0, 255.0) as u8
        }))
    })
}

fn theme_hex_color(base: Option<&serde_json::Value>, key: &str) -> Option<[u8; 3]> {
    let value = base?.get(key)?.as_str()?.trim().trim_start_matches('#');
    if value.len() != 6 {
        return None;
    }
    Some([
        u8::from_str_radix(&value[0..2], 16).ok()?,
        u8::from_str_radix(&value[2..4], 16).ok()?,
        u8::from_str_radix(&value[4..6], 16).ok()?,
    ])
}

fn validate_generated_codex_contract(
    package: &serde_json::Value,
    expected_appearance: &str,
) -> Result<(), String> {
    let codex = package
        .get("targets")
        .and_then(|value| value.get("codex"))
        .ok_or_else(|| "生成的主题包缺少 Codex target。".to_string())?;
    let profile = codex
        .get("options")
        .and_then(|value| value.get("rendererProfile"))
        .and_then(|value| value.as_str());
    let mode = codex
        .get("options")
        .and_then(|value| value.get("baseTheme"))
        .and_then(|value| value.get("mode"))
        .and_then(|value| value.as_str());
    if mode != Some(expected_appearance) {
        return Err(format!(
            "主题基底不匹配：要求 {expected_appearance}，但主题声明为 {}。",
            mode.unwrap_or("未声明")
        ));
    }
    let css = codex
        .get("css")
        .and_then(|value| value.as_str())
        .ok_or_else(|| "生成的 Codex target 缺少 CSS。".to_string())?;
    if !css_contains_declaration(css, "color-scheme", expected_appearance) {
        return Err(format!(
            "主题 CSS 缺少 color-scheme: {expected_appearance}，可能导致原生控件明暗错配。"
        ));
    }
    if profile != Some("codex-theme-v1") {
        return Ok(());
    }
    if !css_rule_has_declaration(
        css,
        "#codedrobe-codex-skin-chrome",
        "pointer-events",
        "none",
    ) {
        return Err(
            "主题使用 codex-theme-v1，但缺少 #codedrobe-codex-skin-chrome { pointer-events: none } 安全规则。"
                .to_string(),
        );
    }
    Ok(())
}

fn css_contains_declaration(css: &str, property: &str, value: &str) -> bool {
    let normalized = css
        .to_ascii_lowercase()
        .chars()
        .filter(|character| !character.is_whitespace())
        .collect::<String>();
    normalized.contains(&format!("{property}:{value}"))
}

fn css_rule_has_declaration(css: &str, selector: &str, property: &str, value: &str) -> bool {
    let css = css.to_ascii_lowercase();
    let selector = selector.to_ascii_lowercase();
    css.match_indices(&selector).any(|(index, _)| {
        let after_selector = &css[index + selector.len()..];
        let after_selector = after_selector.trim_start();
        if !after_selector.starts_with('{') {
            return false;
        }
        let Some(end) = after_selector.find('}') else {
            return false;
        };
        let block = after_selector[1..end]
            .chars()
            .filter(|character| !character.is_whitespace())
            .collect::<String>();
        block.contains(&format!("{property}:{value}"))
    })
}

fn generated_themes_file() -> Result<PathBuf, String> {
    Ok(app_data_directory()?.join("generated-themes.json"))
}

fn load_generated_theme_records() -> Result<Vec<GeneratedThemeRecord>, String> {
    let path = generated_themes_file()?;
    if !path.is_file() {
        return Ok(Vec::new());
    }
    let bytes = fs::read(path).map_err(|error| format!("无法读取 AI 主题索引：{error}"))?;
    serde_json::from_slice(&bytes).map_err(|error| format!("AI 主题索引无效：{error}"))
}

fn save_generated_theme_record(record: GeneratedThemeRecord) -> Result<(), String> {
    let mut records = load_generated_theme_records()?;
    records.retain(|item| !(item.slug == record.slug && item.version == record.version));
    records.insert(0, record);
    let path = generated_themes_file()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|error| format!("无法创建应用数据目录：{error}"))?;
    }
    let bytes = serde_json::to_vec_pretty(&records)
        .map_err(|error| format!("无法保存 AI 主题索引：{error}"))?;
    fs::write(path, bytes).map_err(|error| format!("无法保存 AI 主题索引：{error}"))
}

fn generated_record_to_theme(record: GeneratedThemeRecord) -> MarketplaceTheme {
    let cache_directory = theme_cache_directory().ok();
    generated_record_to_theme_with_cache(record, cache_directory.as_deref())
}

fn generated_record_to_theme_with_cache(
    record: GeneratedThemeRecord,
    cache_directory: Option<&Path>,
) -> MarketplaceTheme {
    let cover_url = if record.cover_data_url.is_empty() {
        cache_directory
            .and_then(|directory| fs::read(directory.join(&record.file_name)).ok())
            .and_then(|bytes| serde_json::from_slice::<serde_json::Value>(&bytes).ok())
            .and_then(|package| generated_cover_data_url(&package))
    } else {
        Some(record.cover_data_url.clone())
    };
    MarketplaceTheme {
        id: record.id,
        slug: record.slug,
        name: LocalizedText {
            zh: record.display_name.clone(),
            en: record.display_name,
        },
        version: record.version,
        description: Some(LocalizedDescription {
            zh: Some(record.tagline.clone()),
            en: Some(record.tagline),
        }),
        categories: vec![MarketplaceCategory {
            slug: "ai-created".to_string(),
            name: LocalizedText {
                zh: "AI 创作".to_string(),
                en: "AI Created".to_string(),
            },
            primary: true,
        }],
        preview_url: cover_url.clone(),
        cover_url,
        published_at: String::new(),
        supported_apps: vec!["codex".to_string()],
        author: Some(MarketplaceAuthor {
            handle: "local-codex".to_string(),
            display_name: "Codex · 本地创作".to_string(),
            avatar_url: None,
        }),
        price: Some(MarketplacePrice { free: true }),
        like_count: 0,
        download_count: 0,
        generated: true,
        appearance_mode: (!record.appearance_mode.is_empty()).then_some(record.appearance_mode),
    }
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

fn cached_theme_appearances(directory: &Path) -> HashMap<String, String> {
    fs::read_dir(directory)
        .ok()
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .filter_map(|entry| {
            let value = fs::read(entry.path()).ok()?;
            let package = serde_json::from_slice::<serde_json::Value>(&value).ok()?;
            let theme = package.get("theme")?;
            let id = theme.get("id")?.as_str()?;
            let version = theme.get("version")?.as_str()?;
            let appearance = theme_package_appearance_value(&package)?;
            Some((format!("{id}@{version}"), appearance))
        })
        .collect()
}

fn theme_package_appearance(path: &Path) -> Option<String> {
    let value = fs::read(path).ok()?;
    let package = serde_json::from_slice::<serde_json::Value>(&value).ok()?;
    theme_package_appearance_value(&package)
}

fn theme_package_appearance_value(package: &serde_json::Value) -> Option<String> {
    let codex = package.get("targets")?.get("codex")?;
    let declared = codex
        .get("options")
        .and_then(|value| value.get("baseTheme"))
        .and_then(|value| value.get("mode"))
        .and_then(|value| value.as_str());
    if matches!(declared, Some("light" | "dark")) {
        return declared.map(str::to_string);
    }
    let css = codex.get("css")?.as_str()?;
    if css_contains_declaration(css, "color-scheme", "dark") {
        Some("dark".to_string())
    } else if css_contains_declaration(css, "color-scheme", "light") {
        Some("light".to_string())
    } else {
        None
    }
}

fn verify_codex_appearance(expected: Option<&str>) -> Result<(), String> {
    let Some(expected) = expected else {
        return Ok(());
    };
    let actual = codex_config_appearance().ok_or_else(|| {
        "主题已注入，但无法确认 Codex 原生深浅基底；请检查 CodeDrobe 对 config.toml 的事务更新。"
            .to_string()
    })?;
    if actual != expected {
        return Err(format!(
            "主题 CSS 已注入，但 Codex 原生基底仍是 {actual}，主题要求 {expected}；已停止报告切换成功。"
        ));
    }
    Ok(())
}

fn codex_config_appearance() -> Option<String> {
    let path = env::var_os("USERPROFILE")
        .map(PathBuf::from)?
        .join(".codex")
        .join("config.toml");
    let text = fs::read_to_string(path).ok()?;
    let mut in_desktop = false;
    for raw_line in text.lines() {
        let line = raw_line.trim();
        if line.starts_with('[') {
            in_desktop = line == "[desktop]";
            continue;
        }
        if !in_desktop {
            continue;
        }
        let Some((key, value)) = line.split_once('=') else {
            continue;
        };
        if key.trim() != "appearanceTheme" {
            continue;
        }
        let value = value.trim().trim_matches('"').to_ascii_lowercase();
        return matches!(value.as_str(), "light" | "dark").then_some(value);
    }
    None
}

fn sync_codex_runtime_appearance(port: u16, expected: &str) -> Result<(), String> {
    if !matches!(expected, "light" | "dark") {
        return Err(format!("不支持的外观模式 {expected}"));
    }
    if codex_runtime_appearance(port).as_deref() == Some(expected) {
        return Ok(());
    }

    set_codex_native_appearance(port, expected)?;
    for _ in 0..24 {
        thread::sleep(Duration::from_millis(250));
        if codex_runtime_appearance(port).as_deref() == Some(expected) {
            return Ok(());
        }
    }
    let actual = codex_runtime_appearance(port).unwrap_or_else(|| "未知".to_string());
    Err(format!(
        "Codex 已接受原生外观切换动作，但运行中的界面仍为 {actual}，主题要求 {expected}"
    ))
}

fn set_codex_native_appearance(port: u16, expected: &str) -> Result<(), String> {
    let expression = codex_native_appearance_expression(expected)?;
    let response = cdp_request(
        port,
        "Runtime.evaluate",
        serde_json::json!({
            "expression": expression,
            "awaitPromise": true,
            "returnByValue": true
        }),
    )?;
    parse_codex_native_appearance_response(&response, expected)
}

fn codex_native_appearance_expression(expected: &str) -> Result<String, String> {
    if !matches!(expected, "light" | "dark") {
        return Err(format!("不支持的外观模式 {expected}"));
    }
    let mode = serde_json::to_string(expected)
        .map_err(|error| format!("无法编码 Codex 外观模式：{error}"))?;
    Ok(CODEX_NATIVE_APPEARANCE_EXPRESSION.replace("__CODEX_APPEARANCE_MODE__", &mode))
}

const CODEX_NATIVE_APPEARANCE_EXPRESSION: &str = r#"
(async () => {
  const mode = __CODEX_APPEARANCE_MODE__;
  try {
    const candidates = [
      ...Array.from(document.scripts, (script) => script.src),
      ...Array.from(
        document.querySelectorAll('link[rel="modulepreload"]'),
        (link) => link.href
      ),
      ...performance.getEntriesByType('resource').map((entry) => entry.name),
    ];
    const rpcUrl = candidates.find((url) =>
      /\/assets\/rpc-[^/?]+\.js(?:\?|$)/u.test(url)
    );
    if (!rpcUrl) {
      throw new Error('找不到 Codex 原生 RPC 模块');
    }

    const rpc = await import(rpcUrl);
    const appActions = rpc.appServices?.appActions;
    if (typeof appActions?.runInPrimaryWindow !== 'function') {
      throw new Error('当前 Codex 未提供 app.appearance.set_mode');
    }

    const result = await appActions.runInPrimaryWindow({
      action: { type: 'app.appearance.set_mode', mode },
    });
    return { ok: true, mode: result?.mode ?? mode };
  } catch (error) {
    return {
      ok: false,
      error: error instanceof Error ? error.message : String(error),
    };
  }
})()
"#;

fn parse_codex_native_appearance_response(
    response: &serde_json::Value,
    expected: &str,
) -> Result<(), String> {
    let evaluation = response
        .get("result")
        .ok_or_else(|| "Codex 原生外观动作缺少 Runtime.evaluate 结果。".to_string())?;
    if let Some(exception) = evaluation.get("exceptionDetails") {
        return Err(format!("Codex 原生外观动作执行异常：{exception}"));
    }
    let value = evaluation
        .get("result")
        .and_then(|value| value.get("value"))
        .ok_or_else(|| "Codex 原生外观动作没有返回结果。".to_string())?;
    if value.get("ok").and_then(|value| value.as_bool()) != Some(true) {
        let detail = value
            .get("error")
            .and_then(|value| value.as_str())
            .unwrap_or("未知错误");
        return Err(format!("Codex 原生外观动作失败：{detail}"));
    }
    let actual = value
        .get("mode")
        .and_then(|value| value.as_str())
        .unwrap_or_default();
    if actual != expected {
        return Err(format!(
            "Codex 原生外观动作返回了 {actual}，主题要求 {expected}"
        ));
    }
    Ok(())
}

fn codex_runtime_appearance(port: u16) -> Option<String> {
    let result = cdp_request(
        port,
        "Runtime.evaluate",
        serde_json::json!({
            "expression": "document.documentElement.classList.contains('electron-dark') ? 'dark' : (document.documentElement.classList.contains('electron-light') ? 'light' : 'system')",
            "returnByValue": true
        }),
    )
    .ok()?;
    result
        .get("result")
        .and_then(|value| value.get("result"))
        .and_then(|value| value.get("value"))
        .and_then(|value| value.as_str())
        .map(str::to_string)
}

fn cdp_request(
    port: u16,
    method: &str,
    params: serde_json::Value,
) -> Result<serde_json::Value, String> {
    let targets = cdp_targets(port)?;
    let websocket_url = cdp_main_websocket_url(&targets)
        .ok_or_else(|| "没有找到 Codex 主渲染窗口。".to_string())?;
    let (mut socket, _) = connect_websocket(websocket_url.as_str())
        .map_err(|error| format!("无法连接 Codex CDP：{error}"))?;
    let request = serde_json::json!({ "id": 1, "method": method, "params": params });
    socket
        .send(Message::Text(request.to_string()))
        .map_err(|error| format!("无法发送 Codex CDP 请求：{error}"))?;
    for _ in 0..32 {
        let message = socket
            .read()
            .map_err(|error| format!("无法读取 Codex CDP 响应：{error}"))?;
        let Message::Text(text) = message else {
            continue;
        };
        let value: serde_json::Value =
            serde_json::from_str(&text).map_err(|error| format!("Codex CDP 响应无效：{error}"))?;
        if value.get("id").and_then(|value| value.as_u64()) != Some(1) {
            continue;
        }
        if let Some(error) = value.get("error") {
            return Err(format!("Codex CDP 请求失败：{error}"));
        }
        return Ok(value);
    }
    Err("Codex CDP 没有返回对应响应。".to_string())
}

fn cdp_targets(port: u16) -> Result<Vec<serde_json::Value>, String> {
    let mut stream = TcpStream::connect(("127.0.0.1", port))
        .map_err(|error| format!("无法连接 Codex CDP 端口 {port}：{error}"))?;
    stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .map_err(|error| format!("无法设置 Codex CDP 读取超时：{error}"))?;
    stream
        .write_all(
            format!("GET /json HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n")
                .as_bytes(),
        )
        .map_err(|error| format!("无法请求 Codex CDP 目标：{error}"))?;
    read_cdp_targets_response(&mut BufReader::new(stream))
}

fn read_cdp_targets_response<R: BufRead>(reader: &mut R) -> Result<Vec<serde_json::Value>, String> {
    let mut status = String::new();
    reader
        .read_line(&mut status)
        .map_err(|error| format!("无法读取 Codex CDP HTTP 状态：{error}"))?;
    if !status.starts_with("HTTP/1.1 200") && !status.starts_with("HTTP/1.0 200") {
        return Err(format!("Codex CDP HTTP 请求失败：{}", status.trim()));
    }

    let mut content_length = None;
    let mut chunked = false;
    loop {
        let mut header = String::new();
        reader
            .read_line(&mut header)
            .map_err(|error| format!("无法读取 Codex CDP HTTP 头：{error}"))?;
        if header == "\r\n" || header == "\n" {
            break;
        }
        let Some((name, value)) = header.split_once(':') else {
            continue;
        };
        if name.trim().eq_ignore_ascii_case("content-length") {
            content_length = value.trim().parse::<usize>().ok();
        }
        if name.trim().eq_ignore_ascii_case("transfer-encoding")
            && value.trim().eq_ignore_ascii_case("chunked")
        {
            chunked = true;
        }
    }

    let body = if let Some(length) = content_length {
        let mut body = vec![0; length];
        reader
            .read_exact(&mut body)
            .map_err(|error| format!("无法读取完整的 Codex CDP 目标：{error}"))?;
        body
    } else if chunked {
        read_chunked_http_body(reader)?
    } else {
        return Err("Codex CDP HTTP 响应缺少 Content-Length。".to_string());
    };
    serde_json::from_slice(&body).map_err(|error| format!("Codex CDP 目标列表无效：{error}"))
}

fn read_chunked_http_body<R: BufRead>(reader: &mut R) -> Result<Vec<u8>, String> {
    let mut body = Vec::new();
    loop {
        let mut size_line = String::new();
        reader
            .read_line(&mut size_line)
            .map_err(|error| format!("无法读取 Codex CDP 分块长度：{error}"))?;
        let size_text = size_line.trim().split(';').next().unwrap_or_default();
        let size = usize::from_str_radix(size_text, 16)
            .map_err(|error| format!("Codex CDP 分块长度无效：{error}"))?;
        if size == 0 {
            break;
        }
        let start = body.len();
        body.resize(start + size, 0);
        reader
            .read_exact(&mut body[start..])
            .map_err(|error| format!("无法读取 Codex CDP 分块内容：{error}"))?;
        let mut terminator = [0; 2];
        reader
            .read_exact(&mut terminator)
            .map_err(|error| format!("无法读取 Codex CDP 分块结尾：{error}"))?;
        if terminator != *b"\r\n" {
            return Err("Codex CDP 分块结尾无效。".to_string());
        }
    }
    Ok(body)
}

fn cdp_main_websocket_url(targets: &[serde_json::Value]) -> Option<String> {
    targets
        .iter()
        .filter(|target| target.get("type").and_then(|value| value.as_str()) == Some("page"))
        .filter(|target| {
            target
                .get("url")
                .and_then(|value| value.as_str())
                .is_some_and(|url| {
                    url.starts_with("app://-/index.html") && !url.contains("initialRoute=")
                })
        })
        .find_map(|target| {
            target
                .get("webSocketDebuggerUrl")
                .and_then(|value| value.as_str())
                .map(str::to_string)
        })
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

fn run_codedrobe_apply_with_retry(
    initial_args: &[&str],
    retry_args: &[&str],
    proxy: Option<&ProxyConfig>,
    port: u16,
) -> Result<Output, String> {
    match run_codedrobe(initial_args, proxy) {
        Ok(output) => Ok(output),
        Err(initial_error) if is_transient_codedrobe_preflight_error(&initial_error) => {
            wait_for_codex_theme_surface(port, Duration::from_secs(15)).map_err(|wait_error| {
                format!("{initial_error}；自动重试前等待 Codex 主窗口失败：{wait_error}")
            })?;
            run_codedrobe(retry_args, proxy)
                .map_err(|retry_error| format!("{retry_error}（Codex 主窗口稳定后自动重试仍失败）"))
        }
        Err(error) => Err(error),
    }
}

fn is_transient_codedrobe_preflight_error(error: &str) -> bool {
    error.contains("DOM preflight failed")
        || error.contains("No OpenAI Codex renderer target")
        || error.contains("CODEDROBE_TARGET_TIMEOUT")
}

fn wait_for_codex_theme_surface(port: u16, timeout: Duration) -> Result<(), String> {
    let started = Instant::now();
    while started.elapsed() < timeout {
        let ready = cdp_request(
            port,
            "Runtime.evaluate",
            serde_json::json!({
                "expression": "Boolean(document.querySelector('main.main-surface'))",
                "returnByValue": true
            }),
        )
        .ok()
        .and_then(|response| {
            response
                .get("result")
                .and_then(|value| value.get("result"))
                .and_then(|value| value.get("value"))
                .and_then(|value| value.as_bool())
        })
        .unwrap_or(false);
        if ready {
            // Let the shell finish mounting route-specific nodes before Core probes it.
            thread::sleep(Duration::from_millis(500));
            return Ok(());
        }
        thread::sleep(Duration::from_millis(250));
    }
    Err(format!(
        "在 {} 秒内没有检测到 main.main-surface（CDP {port}）",
        timeout.as_secs()
    ))
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
        let mut command = background_command("cmd.exe");
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
            let mut command = background_command(node);
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

fn find_codex_cli() -> Option<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(path) = find_on_path("codex.cmd") {
        candidates.push(path);
    }
    if let Some(path) = find_on_path("codex.exe") {
        candidates.push(path);
    }

    if let Some(user_profile) = env::var_os("USERPROFILE").map(PathBuf::from) {
        let extensions = user_profile.join(".vscode").join("extensions");
        let mut vscode_candidates = fs::read_dir(extensions)
            .ok()
            .into_iter()
            .flatten()
            .filter_map(Result::ok)
            .filter(|entry| {
                entry
                    .file_name()
                    .to_string_lossy()
                    .starts_with("openai.chatgpt-")
            })
            .map(|entry| {
                entry
                    .path()
                    .join("bin")
                    .join("windows-x86_64")
                    .join("codex.exe")
            })
            .collect::<Vec<_>>();
        vscode_candidates.sort_by(|left, right| right.cmp(left));
        candidates.extend(vscode_candidates);
        candidates.push(
            user_profile
                .join(".codex")
                .join("plugins")
                .join(".plugin-appserver")
                .join("codex.exe"),
        );
        candidates.push(
            user_profile
                .join(".codex")
                .join(".sandbox-bin")
                .join("codex.exe"),
        );
    }

    candidates.into_iter().find(|candidate| {
        candidate.is_file()
            && !is_windowsapps_path(candidate)
            && codex_cli_command(candidate)
                .arg("--version")
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .status()
                .is_ok_and(|status| status.success())
    })
}

fn codex_cli_command(path: &Path) -> Command {
    if path
        .extension()
        .and_then(|value| value.to_str())
        .is_some_and(|value| value.eq_ignore_ascii_case("cmd"))
    {
        let mut command = background_command("cmd.exe");
        command.args(["/d", "/s", "/c"]).arg(path);
        command
    } else {
        background_command(path)
    }
}

fn is_windowsapps_path(path: &Path) -> bool {
    path.components().any(|component| {
        component
            .as_os_str()
            .to_string_lossy()
            .eq_ignore_ascii_case("WindowsApps")
    })
}

fn find_codex_executable() -> Option<PathBuf> {
    let script = "$p=@(Get-AppxPackage -Name 'OpenAI.Codex';Get-AppxPackage -Name 'OpenAI.ChatGPT-Desktop')|Sort-Object Version -Descending|Select-Object -First 1;if($p){$p.InstallLocation}";
    let output = background_command("powershell.exe")
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
    background_command("powershell.exe")
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

fn background_command(program: impl AsRef<OsStr>) -> Command {
    let mut command = Command::new(program);
    #[cfg(target_os = "windows")]
    command.creation_flags(CREATE_NO_WINDOW);
    command
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
        .manage(AiThemeState::default())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
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
            get_ai_theme_capability,
            install_ai_theme_skill,
            list_generated_themes,
            start_ai_theme_generation,
            get_ai_theme_job,
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
    use std::io::Cursor;

    fn test_png(color: [u8; 3]) -> (String, Vec<u8>) {
        let image = DynamicImage::ImageRgb8(ImageBuffer::from_pixel(4, 4, Rgb(color)));
        let mut cursor = Cursor::new(Vec::new());
        image
            .write_to(&mut cursor, image::ImageOutputFormat::Png)
            .expect("test PNG should encode");
        let bytes = cursor.into_inner();
        (BASE64.encode(&bytes), bytes)
    }

    #[test]
    fn validates_marketplace_slugs() {
        assert!(validate_slug("pastel-morning").is_ok());
        assert!(validate_slug("../theme").is_err());
        assert!(validate_slug("Theme Name").is_err());
    }

    #[test]
    fn retries_only_transient_codedrobe_startup_failures() {
        assert!(is_transient_codedrobe_preflight_error(
            "[codedrobe] OpenAI Codex DOM preflight failed for 2 of 2 renderer target(s)"
        ));
        assert!(is_transient_codedrobe_preflight_error(
            "No OpenAI Codex renderer target on 127.0.0.1:9335"
        ));
        assert!(!is_transient_codedrobe_preflight_error(
            "Theme package checksum mismatch"
        ));
    }

    #[test]
    fn rejects_store_managed_codex_cli_path() {
        assert!(is_windowsapps_path(Path::new(
            r"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0_x64\app\resources\codex.exe"
        )));
        assert!(!is_windowsapps_path(Path::new(
            r"C:\Users\user\.vscode\extensions\openai.chatgpt-1.0.0\bin\windows-x86_64\codex.exe"
        )));
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
    fn validates_persisted_watcher_command_before_termination() {
        let record = WatcherRecord {
            pid: 42,
            port: 58383,
            theme_path: r"C:\Themes\demo-1.0.0.codedrobe-theme".to_string(),
        };
        assert!(is_expected_watcher_command(
            r#"node npx-cli.js @codedrobe/core apply --app codex --port 58383 --theme C:\Themes\demo-1.0.0.codedrobe-theme --no-launch --watch"#,
            &record
        ));
        assert!(!is_expected_watcher_command(
            r#"node another-tool.js --port 58383 --watch C:\Themes\demo-1.0.0.codedrobe-theme"#,
            &record
        ));
        assert!(!is_expected_watcher_command(
            r#"node npx-cli.js @codedrobe/core apply --app codex --port 9335 --theme C:\Themes\demo-1.0.0.codedrobe-theme --watch"#,
            &record
        ));
    }

    #[test]
    fn requires_noninteractive_codex_profile_chrome() {
        assert!(css_rule_has_declaration(
            "html.codedrobe-host-codex #codedrobe-codex-skin-chrome { pointer-events: none !important; }",
            "#codedrobe-codex-skin-chrome",
            "pointer-events",
            "none"
        ));
        assert!(!css_rule_has_declaration(
            "/* #codedrobe-codex-skin-chrome */ body { pointer-events: none; }",
            "#codedrobe-codex-skin-chrome",
            "pointer-events",
            "none"
        ));
    }

    #[test]
    fn validates_generated_theme_appearance_contract() {
        let package = serde_json::json!({
            "targets": {
                "codex": {
                    "css": ":root.codedrobe-host-codex { color-scheme: dark; }",
                    "options": { "baseTheme": { "mode": "dark" } }
                }
            }
        });
        assert!(validate_generated_codex_contract(&package, "dark").is_ok());
        assert!(validate_generated_codex_contract(&package, "light").is_err());
    }

    #[test]
    fn reads_declared_or_css_theme_appearance() {
        let declared = serde_json::json!({
            "targets": { "codex": {
                "css": ":root { color-scheme: light; }",
                "options": { "baseTheme": { "mode": "dark" } }
            }}
        });
        assert_eq!(
            theme_package_appearance_value(&declared).as_deref(),
            Some("dark")
        );

        let css_only = serde_json::json!({
            "targets": { "codex": { "css": ":root { color-scheme: light; }" }}
        });
        assert_eq!(
            theme_package_appearance_value(&css_only).as_deref(),
            Some("light")
        );
    }

    #[test]
    fn builds_version_independent_codex_native_appearance_action() {
        let expression =
            codex_native_appearance_expression("dark").expect("dark mode should be supported");
        assert!(expression.contains("const mode = \"dark\";"));
        assert!(expression.contains("\\/assets\\/rpc-"));
        assert!(expression.contains("app.appearance.set_mode"));
        assert!(expression.contains("link[rel=\"modulepreload\"]"));
        assert!(expression.contains("await import(rpcUrl)"));
        assert!(expression.contains("runInPrimaryWindow"));
        assert!(!expression.contains("rpc-7JhFtYTP.js"));
        assert!(!expression.contains("Page.reload"));
        assert!(codex_native_appearance_expression("system").is_err());
    }

    #[test]
    fn parses_codex_native_appearance_action_result() {
        let success = serde_json::json!({
            "result": {
                "result": {
                    "type": "object",
                    "value": { "ok": true, "mode": "dark" }
                }
            }
        });
        assert!(parse_codex_native_appearance_response(&success, "dark").is_ok());
        assert!(parse_codex_native_appearance_response(&success, "light").is_err());

        let failure = serde_json::json!({
            "result": {
                "result": {
                    "type": "object",
                    "value": { "ok": false, "error": "动作不可用" }
                }
            }
        });
        let error = parse_codex_native_appearance_response(&failure, "dark")
            .expect_err("failed native action should be reported");
        assert!(error.contains("动作不可用"));
    }

    #[test]
    fn selects_only_the_main_codex_cdp_renderer() {
        let targets = serde_json::json!([
            {
                "type": "page",
                "url": "app://-/index.html?initialRoute=%2Favatar-overlay",
                "webSocketDebuggerUrl": "ws://127.0.0.1:58383/avatar"
            },
            {
                "type": "page",
                "url": "app://-/index.html",
                "webSocketDebuggerUrl": "ws://127.0.0.1:58383/main"
            },
            {
                "type": "worker",
                "url": "app://-/index.html",
                "webSocketDebuggerUrl": "ws://127.0.0.1:58383/worker"
            }
        ]);
        assert_eq!(
            cdp_main_websocket_url(targets.as_array().expect("targets should be an array"))
                .as_deref(),
            Some("ws://127.0.0.1:58383/main")
        );
    }

    #[test]
    fn reads_cdp_targets_without_waiting_for_connection_close() {
        let body = r#"[{"type":"page","url":"app://-/index.html","webSocketDebuggerUrl":"ws://127.0.0.1/main"}]"#;
        let response = format!(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: keep-alive\r\n\r\n{}trailing-bytes-that-must-not-be-read",
            body.len(),
            body
        );
        let mut reader = Cursor::new(response.into_bytes());
        let targets = read_cdp_targets_response(&mut reader).expect("CDP response should parse");
        assert_eq!(targets.len(), 1);
        assert_eq!(
            cdp_main_websocket_url(&targets).as_deref(),
            Some("ws://127.0.0.1/main")
        );
    }

    #[test]
    fn reads_chunked_cdp_targets_response() {
        let body = r#"[{"type":"page","url":"app://-/index.html","webSocketDebuggerUrl":"ws://127.0.0.1/main"}]"#;
        let response = format!(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{:X}\r\n{}\r\n0\r\n\r\n",
            body.len(),
            body
        );
        let mut reader = Cursor::new(response.into_bytes());
        let targets =
            read_cdp_targets_response(&mut reader).expect("chunked response should parse");
        assert_eq!(targets.len(), 1);
    }

    #[test]
    fn requires_embedded_image_for_image_visual_modes() {
        let valid_css = [r#"
            html.codedrobe-host-codex aside.app-shell-left-panel { color: white; }
            html.codedrobe-host-codex main.main-surface { background: var(--codedrobe-image-hero); }
            html.codedrobe-host-codex header.app-header-tint { color: white; }
            html.codedrobe-host-codex .composer-surface-chrome { color: white; }
        "#, AI_THEME_COMPONENT_COVERAGE_REFERENCE].concat();
        let (white_png, white_bytes) = test_png([245, 248, 252]);
        let with_image = serde_json::json!({
            "assets": { "images": { "hero": { "base64": white_png } } },
            "targets": { "codex": { "css": valid_css } }
        });
        assert!(validate_generated_visual_contract(&with_image, "light", "upload", None).is_ok());
        assert!(validate_generated_visual_contract(&with_image, "light", "ai", None).is_ok());
        assert!(validate_generated_visual_contract(
            &with_image,
            "light",
            "upload",
            Some(&white_bytes)
        )
        .is_err());

        let without_image = serde_json::json!({
            "targets": { "codex": { "css": valid_css.replace("var(--codedrobe-image-hero)", "linear-gradient(red, blue)") } }
        });
        assert!(validate_generated_visual_contract(&without_image, "light", "css", None).is_ok());
        assert!(
            validate_generated_visual_contract(&without_image, "light", "upload", None).is_err()
        );
    }

    #[test]
    fn deserializes_ai_theme_request_from_frontend_camel_case() {
        let request: AiThemeRequest = serde_json::from_value(serde_json::json!({
            "prompt": "深海夜航主题",
            "appearance": "dark",
            "visualMode": "upload",
            "imagePath": "C:\\Images\\background.png",
            "proxy": { "host": "127.0.0.1", "port": 10808 }
        }))
        .expect("frontend request should deserialize");
        assert_eq!(request.visual_mode, "upload");
        assert_eq!(
            request.image_path.as_deref(),
            Some(r"C:\Images\background.png")
        );
        assert_eq!(request.proxy.port, 10808);
    }

    #[test]
    fn ai_prompt_delegates_packaging_to_launcher() {
        let prompt = build_ai_theme_prompt(
            "安静的森林主题",
            "dark",
            "css",
            None,
            Path::new(r"C:\job\authoring-reference\codex.css"),
            Path::new(r"C:\job\generated-theme\theme.json"),
        );
        assert!(prompt.contains("不要运行 PowerShell、npx、npm、codedrobe"));
        assert!(prompt.contains(r"C:\job\generated-theme\theme.json"));
        assert!(!prompt.contains("最终成品必须精确写到"));
    }

    #[test]
    fn ai_prompt_requests_layered_visual_system_for_image_modes() {
        let upload_prompt = build_ai_theme_prompt(
            "未来音乐工作台",
            "light",
            "upload",
            Some(Path::new(r"C:\job\reference.png")),
            Path::new(r"C:\job\authoring-reference\codex.css"),
            Path::new(r"C:\job\generated-theme\theme.json"),
        );
        assert!(upload_prompt.contains("generated-theme/assets/hero.png"));
        assert!(upload_prompt.contains("generated-theme/assets/texture.png"));
        assert!(upload_prompt.contains("不适合时不要为了凑数量生成纹理"));
        assert!(upload_prompt.contains("不要只替换背景"));
        assert!(upload_prompt.contains("卡片、按钮、输入区、消息、代码块"));
        assert!(upload_prompt.contains("右侧输出/来源摘要面板"));
        assert!(upload_prompt.contains("Tooltip、Popover、Dropdown"));
        assert!(upload_prompt.contains("prefers-reduced-motion"));
        assert!(upload_prompt.contains("preservedAnchors"));
        assert!(upload_prompt.contains("不得擅自改成无人物空景"));
        assert!(upload_prompt.contains("禁止生成暗夜图后依赖白色蒙层强行漂白"));

        let generated_prompt = build_ai_theme_prompt(
            "未来音乐工作台",
            "dark",
            "ai",
            None,
            Path::new(r"C:\job\authoring-reference\codex.css"),
            Path::new(r"C:\job\generated-theme\theme.json"),
        );
        assert!(generated_prompt.contains("var(--codedrobe-image-hero)"));
        assert!(generated_prompt.contains("var(--codedrobe-image-texture)"));
        assert!(generated_prompt.contains("低对比度、无主体、可平铺"));
        assert!(generated_prompt.contains(r"C:\job\authoring-reference\codex.css"));
        assert!(generated_prompt.contains("body 可以有纹理，但不能是 hero 的唯一挂载点"));
    }

    #[test]
    fn rejects_hidden_hero_and_overbroad_generated_css() {
        let body_only = r#"
            html.codedrobe-host-codex body { background: var(--codedrobe-image-hero); }
            html.codedrobe-host-codex aside.app-shell-left-panel { color: white; }
            html.codedrobe-host-codex main.main-surface { background: #111; }
            html.codedrobe-host-codex header.app-header-tint { color: white; }
            html.codedrobe-host-codex .composer-surface-chrome { color: white; }
        "#;
        let (white_png, _) = test_png([245, 248, 252]);
        let package = serde_json::json!({
            "assets": { "images": { "hero": { "base64": white_png } } },
            "targets": { "codex": { "css": body_only } }
        });
        assert!(validate_generated_visual_contract(&package, "light", "ai", None).is_err());

        let broad = body_only.replace(
            "html.codedrobe-host-codex body",
            "html.codedrobe-host-codex :where(button)",
        );
        assert!(validate_generated_css_quality_contract(&broad, "css").is_err());
    }

    #[test]
    fn requires_generated_theme_component_coverage() {
        let complete = format!(
            r#"
            html.codedrobe-host-codex aside.app-shell-left-panel {{ color: white; }}
            html.codedrobe-host-codex main.main-surface {{ color: white; }}
            html.codedrobe-host-codex header.app-header-tint {{ color: white; }}
            html.codedrobe-host-codex .composer-surface-chrome {{ color: white; }}
            {}"#,
            AI_THEME_COMPONENT_COVERAGE_REFERENCE
        );
        assert!(validate_generated_css_quality_contract(&complete, "css").is_ok());
        assert!(validate_generated_css_quality_contract(
            &complete.replace("[role=\"tooltip\"]", "[data-missing-tooltip]"),
            "css"
        )
        .is_err());
    }

    #[test]
    fn validates_generated_hero_brightness_for_selected_base() {
        let (_, light) = test_png([240, 245, 250]);
        let (_, dark) = test_png([12, 20, 32]);
        assert!(validate_generated_hero_appearance(&light, "light").is_ok());
        assert!(validate_generated_hero_appearance(&light, "dark").is_err());
        assert!(validate_generated_hero_appearance(&dark, "dark").is_ok());
        assert!(validate_generated_hero_appearance(&dark, "light").is_err());
    }

    #[test]
    fn requires_reference_anchor_self_review() {
        let valid = serde_json::json!({
            "appearance": "light",
            "anchors": ["group", "center", "left", "right", "halo"],
            "preservedAnchors": ["group", "center", "halo"]
        });
        assert!(validate_generated_reference_brief_value(&valid, "light").is_ok());
        assert!(validate_generated_reference_brief_value(&valid, "dark").is_err());

        let weak = serde_json::json!({
            "appearance": "light",
            "anchors": ["cyberpunk"],
            "preservedAnchors": []
        });
        assert!(validate_generated_reference_brief_value(&weak, "light").is_err());
    }

    #[test]
    fn builds_local_card_cover_for_css_theme() {
        let package = serde_json::json!({
            "targets": { "codex": { "options": { "baseTheme": {
                "mode": "dark", "surface": "#10211D", "accent": "#19C2B5", "ink": "#E6F2ED"
            }}}}
        });
        let cover = generated_cover_data_url(&package).expect("cover should render");
        assert!(cover.starts_with("data:image/jpeg;base64,"));
        assert!(cover.len() > 1_000);
    }

    #[test]
    fn hydrates_missing_generated_theme_cover_from_cached_package() {
        let (hero, _) = test_png([24, 14, 56]);
        let package = serde_json::json!({
            "assets": { "images": { "hero": { "base64": hero } } },
            "targets": { "codex": { "options": { "baseTheme": {
                "mode": "dark", "surface": "#08071A", "accent": "#A873FF", "ink": "#F2ECFF"
            }}}}
        });
        let cache = std::env::temp_dir().join(format!(
            "codex-proxy-launch-deck-cover-test-{}",
            std::process::id()
        ));
        fs::create_dir_all(&cache).expect("test cache should be created");
        let file_name = "astral-psychic-sanctuary-1.0.0.codedrobe-theme";
        fs::write(
            cache.join(file_name),
            serde_json::to_vec(&package).expect("package should serialize"),
        )
        .expect("test package should be written");

        let theme = generated_record_to_theme_with_cache(
            GeneratedThemeRecord {
                id: "generated-astral-psychic-sanctuary".to_string(),
                slug: "astral-psychic-sanctuary".to_string(),
                display_name: "星界心灵圣所".to_string(),
                version: "1.0.0".to_string(),
                tagline: "原创星界主题".to_string(),
                file_name: file_name.to_string(),
                appearance_mode: "dark".to_string(),
                cover_data_url: String::new(),
            },
            Some(&cache),
        );

        let cover = theme.cover_url.expect("cached hero should hydrate cover");
        assert!(cover.starts_with("data:image/jpeg;base64,"));
        assert_eq!(theme.preview_url.as_deref(), Some(cover.as_str()));
        let _ = fs::remove_dir_all(cache);
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
