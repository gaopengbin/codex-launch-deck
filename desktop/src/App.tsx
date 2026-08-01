import { startTransition, useDeferredValue, useEffect, useEffectEvent, useState } from 'react'
import { invoke } from '@tauri-apps/api/core'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { openUrl } from '@tauri-apps/plugin-opener'
import { open } from '@tauri-apps/plugin-dialog'
import mikuHero from './assets/miku-hero.png'
import './App.css'

type View = 'discover' | 'installed' | 'create' | 'settings'
type BusyAction = 'proxy' | 'themes' | 'download' | 'launch' | 'apply' | 'restore' | 'skill' | 'auth' | 'publish' | null

interface ProxyConfig {
  host: string
  port: number
}

interface LocalizedText {
  en: string
  zh: string
}

interface ThemeAuthor {
  handle: string
  displayName: string
  avatarUrl?: string | null
}

interface ThemeCategory {
  slug: string
  name: LocalizedText
  primary: boolean
}

interface Theme {
  id: string
  slug: string
  name: LocalizedText
  version: string
  description?: { en: string | null; zh: string | null } | null
  categories: ThemeCategory[]
  previewUrl?: string | null
  coverUrl?: string | null
  publishedAt: string
  supportedApps: string[]
  author?: ThemeAuthor | null
  likeCount: number
  downloadCount: number
  bundled?: boolean
  generated?: boolean
  appearanceMode?: 'light' | 'dark' | null
}

interface AiThemeCapability {
  codexCliAvailable: boolean
  skillInstalled: boolean
  skillPath?: string | null
}

interface AiThemeJob {
  id: string
  status: 'running' | 'completed' | 'failed'
  phase: string
  progress: number
  logs: string[]
  error?: string | null
  theme?: Theme | null
}

interface MarketplacePage {
  themes: Theme[]
  total: number
}

interface AppState {
  codexInstalled: boolean
  codexRunning: boolean
  codedrobeAvailable: boolean
  cachedThemes: string[]
  cacheDirectory: string
  themeAppearances: Record<string, 'light' | 'dark'>
}

interface ActionResult {
  message: string
  themePath?: string | null
  warning: boolean
}

interface CodeDrobeAuthStatus {
  loggedIn: boolean
  baseUrl: string
  creatorHandle?: string | null
}

interface ThemePublishInfo {
  ready: boolean
  missing: string[]
  categories: string[]
  hasCover: boolean
}

interface ThemePublishResult {
  message: string
  storeUrl?: string | null
  submitted: boolean
  status?: string | null
}

const bundledTheme: Theme = {
  id: 'bundled-miku',
  slug: 'miku-future-beats',
  name: {
    zh: '初音未来 · Future Beats',
    en: 'Hatsune Miku · Future Beats',
  },
  version: '1.2.2',
  description: {
    zh: '冰青、樱粉与未来节拍交织的全幅主题，随启动器离线提供。',
    en: 'An ice-teal and sakura-pink full-window theme bundled for offline use.',
  },
  categories: [{ slug: 'featured', name: { zh: '本地精选', en: 'Featured' }, primary: true }],
  previewUrl: mikuHero,
  coverUrl: mikuHero,
  publishedAt: '',
  supportedApps: ['codex'],
  author: { handle: 'local', displayName: 'Launch Deck' },
  likeCount: 0,
  downloadCount: 0,
  bundled: true,
  appearanceMode: 'light',
}

const initialState: AppState = {
  codexInstalled: false,
  codexRunning: false,
  codedrobeAvailable: false,
  cachedThemes: [],
  cacheDirectory: '',
  themeAppearances: {},
}

const initialAiCapability: AiThemeCapability = {
  codexCliAvailable: false,
  skillInstalled: false,
}

const initialCodeDrobeAuth: CodeDrobeAuthStatus = {
  loggedIn: false,
  baseUrl: 'https://codedrobe.app',
  creatorHandle: null,
}

function Icon({ name }: { name: 'spark' | 'grid' | 'download' | 'settings' | 'search' | 'play' | 'pulse' | 'arrow' | 'refresh' }) {
  const paths = {
    spark: <><path d="M12 2l1.7 5.1L19 9l-5.3 1.9L12 16l-1.7-5.1L5 9l5.3-1.9L12 2Z" /><path d="m19 15 .8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8L19 15Z" /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="2" /><rect x="14" y="3" width="7" height="7" rx="2" /><rect x="3" y="14" width="7" height="7" rx="2" /><rect x="14" y="14" width="7" height="7" rx="2" /></>,
    download: <><path d="M12 3v12" /><path d="m7 10 5 5 5-5" /><path d="M5 21h14" /></>,
    settings: <><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3V2.8h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z" /></>,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    play: <path d="m9 7 8 5-8 5V7Z" />,
    pulse: <path d="M3 12h4l2.2-6 4.2 12 2.3-6H21" />,
    arrow: <><path d="M5 12h14" /><path d="m14 7 5 5-5 5" /></>,
    refresh: <><path d="M20 6v5h-5" /><path d="M4 18v-5h5" /><path d="M6.1 9A7 7 0 0 1 18.6 6L20 8" /><path d="M17.9 15A7 7 0 0 1 5.4 18L4 16" /></>,
  }
  return <svg viewBox="0 0 24 24" aria-hidden="true">{paths[name]}</svg>
}

function App() {
  const [view, setView] = useState<View>('discover')
  const [proxy, setProxy] = useState<ProxyConfig>({ host: '127.0.0.1', port: 10808 })
  const [appState, setAppState] = useState<AppState>(initialState)
  const [themes, setThemes] = useState<Theme[]>([bundledTheme])
  const [selectedTheme, setSelectedTheme] = useState<Theme>(bundledTheme)
  const [themeEnabled, setThemeEnabled] = useState(true)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<'downloads' | 'newest' | 'name'>('downloads')
  const [busy, setBusy] = useState<BusyAction>('themes')
  const [downloadProgress, setDownloadProgress] = useState<number | null>(null)
  const [aiCapability, setAiCapability] = useState<AiThemeCapability>(initialAiCapability)
  const [aiPrompt, setAiPrompt] = useState('')
  const [aiAppearance, setAiAppearance] = useState<'light' | 'dark'>('dark')
  const [aiVisualMode, setAiVisualMode] = useState<'css' | 'upload' | 'ai'>('css')
  const [aiImagePath, setAiImagePath] = useState<string | null>(null)
  const [aiJob, setAiJob] = useState<AiThemeJob | null>(null)
  const [codedrobeAuth, setCodeDrobeAuth] = useState<CodeDrobeAuthStatus>(initialCodeDrobeAuth)
  const [publishInfo, setPublishInfo] = useState<ThemePublishInfo | null>(null)
  const [publishStoreUrl, setPublishStoreUrl] = useState<string | null>(null)
  const [confirmSubmit, setConfirmSubmit] = useState(false)
  const [status, setStatus] = useState('正在连接启动台…')
  const [statusTone, setStatusTone] = useState<'idle' | 'success' | 'warning' | 'error'>('idle')
  const deferredQuery = useDeferredValue(query)
  const aiJobId = aiJob?.id
  const aiJobStatus = aiJob?.status

  const bootstrap = useEffectEvent(async () => {
    try {
      const [state, page] = await Promise.all([
        invoke<AppState>('get_app_state'),
        invoke<MarketplacePage>('list_themes', { proxy }),
      ])
      startTransition(() => {
        setAppState(state)
        setThemes((current) => mergeThemes(current.filter((theme) => theme.generated), page.themes))
      })
      setStatus(`已连接 CodeDrobe 商店，共 ${page.total} 个 Codex 主题`)
      setStatusTone('success')
    } catch (error) {
      setStatus(`商店暂不可用，仍可使用内置主题：${formatError(error)}`)
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  })

  useEffect(() => {
    void bootstrap()
  }, [])

  const loadCodeDrobeAuth = useEffectEvent(async () => {
    try {
      setCodeDrobeAuth(await invoke<CodeDrobeAuthStatus>('get_codedrobe_auth_status', { proxy }))
    } catch {
      // Authentication remains an explicit user action; startup checks stay silent.
    }
  })

  useEffect(() => {
    void loadCodeDrobeAuth()
  }, [])

  const loadAiState = useEffectEvent(async () => {
    try {
      const [generatedThemes, capability] = await Promise.all([
        invoke<Theme[]>('list_generated_themes'),
        invoke<AiThemeCapability>('get_ai_theme_capability'),
      ])
      setAiCapability(capability)
      setThemes((current) => mergeThemes(generatedThemes, current.filter((theme) => !theme.bundled && !theme.generated)))
    } catch {
      // AI capability errors are surfaced when the user opens or starts creation.
    }
  })

  useEffect(() => {
    void loadAiState()
  }, [])

  const selectedPublishKey = selectedTheme.generated
    ? `${selectedTheme.slug}@${selectedTheme.version}`
    : null

  const loadPublishInfo = useEffectEvent(async () => {
    if (!selectedTheme.generated) return
    try {
      setPublishInfo(await invoke<ThemePublishInfo>('get_theme_publish_info', {
        theme: themeSelection(selectedTheme),
      }))
    } catch (error) {
      setPublishInfo({ ready: false, missing: [formatError(error)], categories: [], hasCover: false })
    }
  })

  useEffect(() => {
    setPublishInfo(null)
    setPublishStoreUrl(null)
    setConfirmSubmit(false)
    if (selectedPublishKey) void loadPublishInfo()
  }, [selectedPublishKey])

  const pollAiThemeJob = useEffectEvent(async (id: string) => {
    const nextJob = await invoke<AiThemeJob>('get_ai_theme_job', { id })
    setAiJob(nextJob)
    if (nextJob.status === 'completed' && nextJob.theme) {
      setThemes((current) => mergeThemes([nextJob.theme!], current.filter((theme) => !theme.bundled)))
      setSelectedTheme(nextJob.theme)
      setAppState(await invoke<AppState>('get_app_state'))
      setStatus(`AI 主题「${themeName(nextJob.theme)}」已生成并加入“已安装”`)
      setStatusTone('success')
    } else if (nextJob.status === 'failed') {
      setStatus(nextJob.error || 'AI 主题生成失败。')
      setStatusTone('error')
    }
  })

  useEffect(() => {
    if (!aiJobId || aiJobStatus !== 'running') return
    const jobId = aiJobId
    let disposed = false
    const interval = window.setInterval(async () => {
      try {
        if (!disposed) await pollAiThemeJob(jobId)
      } catch (error) {
        if (!disposed) {
          setStatus(formatError(error))
          setStatusTone('error')
        }
      }
    }, 750)
    return () => {
      disposed = true
      window.clearInterval(interval)
    }
  }, [aiJobId, aiJobStatus])

  useEffect(() => {
    let disposed = false
    const interval = window.setInterval(async () => {
      try {
        const codexRunning = await invoke<boolean>('get_codex_running')
        if (!disposed) {
          setAppState((current) => current.codexRunning === codexRunning
            ? current
            : { ...current, codexRunning })
        }
      } catch {
        // The full action status reports backend errors; polling stays silent.
      }
    }, 2000)
    return () => {
      disposed = true
      window.clearInterval(interval)
    }
  }, [])

  async function refreshThemes() {
    setBusy('themes')
    setStatus('正在刷新 CodeDrobe 主题商店…')
    setStatusTone('idle')
    try {
      const page = await invoke<MarketplacePage>('list_themes', { proxy })
      startTransition(() => {
        setThemes((current) => mergeThemes(current.filter((theme) => theme.generated), page.themes))
      })
      setStatus(`主题商店已刷新，共 ${page.total} 个 Codex 主题`)
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function checkProxy() {
    setBusy('proxy')
    setStatus('正在检查代理连接…')
    setStatusTone('idle')
    try {
      const result = await invoke<ActionResult>('test_proxy', { proxy })
      setStatus(result.message)
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function cacheSelectedTheme() {
    if (selectedTheme.bundled) {
      setStatus('初音未来主题已随启动器内置，无需下载')
      setStatusTone('success')
      return
    }
    setBusy('download')
    setDownloadProgress(8)
    setStatus(`正在安全下载 ${themeName(selectedTheme)}…`)
    setStatusTone('idle')
    const progressTimer = window.setInterval(() => {
      setDownloadProgress((current) => {
        if (current === null) return 8
        if (current >= 92) return current
        return Math.min(92, current + Math.max(1, Math.round((92 - current) * 0.08)))
      })
    }, 220)
    try {
      const result = await invoke<ActionResult>('download_theme', {
        proxy,
        theme: themeSelection(selectedTheme),
      })
      setDownloadProgress(96)
      const state = await invoke<AppState>('get_app_state')
      setAppState(state)
      setDownloadProgress(100)
      setStatus(result.message)
      setStatusTone('success')
      await new Promise((resolve) => window.setTimeout(resolve, 320))
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      window.clearInterval(progressTimer)
      setBusy(null)
      setDownloadProgress(null)
    }
  }

  async function installAiSkill() {
    setBusy('skill')
    setStatus('正在安装 CodeDrobe 主题创作 Skill…')
    setStatusTone('idle')
    try {
      const capability = await invoke<AiThemeCapability>('install_ai_theme_skill')
      setAiCapability(capability)
      setStatus('主题创作能力已安装，可以开始生成。')
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function generateAiTheme() {
    setStatus('正在创建 AI 主题任务…')
    setStatusTone('idle')
    try {
      const job = await invoke<AiThemeJob>('start_ai_theme_generation', {
        request: { prompt: aiPrompt, appearance: aiAppearance, visualMode: aiVisualMode, imagePath: aiImagePath, proxy },
      })
      setAiJob(job)
      setStatus('Codex 已开始创作主题，可以在下方查看实时进度。')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    }
  }

  async function chooseAiBackground() {
    const selected = await open({
      multiple: false,
      directory: false,
      filters: [{ name: '背景图片', extensions: ['png', 'jpg', 'jpeg', 'webp', 'gif'] }],
    })
    if (typeof selected === 'string') setAiImagePath(selected)
  }

  function updateProxyPort(nextPort: number) {
    setProxy({ ...proxy, port: Math.min(65535, Math.max(1, nextPort)) })
  }

  async function launch() {
    setBusy('launch')
    setStatus(themeEnabled ? `正在准备 ${themeName(selectedTheme)}…` : '正在通过代理启动 Codex…')
    setStatusTone('idle')
    try {
      const result = await invoke<ActionResult>('launch_codex', {
        request: {
          proxy,
          theme: themeEnabled ? themeSelection(selectedTheme) : null,
        },
      })
      setStatus(result.message)
      setStatusTone(result.warning ? 'warning' : 'success')
      const state = await invoke<AppState>('get_app_state')
      setAppState(state)
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function applySelectedTheme() {
    setBusy('apply')
    setStatus(`正在即时应用 ${themeName(selectedTheme)}…`)
    setStatusTone('idle')
    try {
      const result = await invoke<ActionResult>('apply_theme', {
        proxy,
        theme: themeSelection(selectedTheme),
      })
      setThemeEnabled(true)
      setStatus(result.message)
      setStatusTone('success')
      const state = await invoke<AppState>('get_app_state')
      setAppState(state)
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function runPrimaryAction() {
    if (appState.codexRunning && themeEnabled) {
      await applySelectedTheme()
      return
    }
    if (appState.codexRunning) {
      setStatus('Codex 已在运行；开启主题后可直接热切换。')
      setStatusTone('error')
      return
    }
    await launch()
  }

  async function restore() {
    setBusy('restore')
    setStatus('正在恢复 Codex 原生外观…')
    setStatusTone('idle')
    try {
      const result = await invoke<ActionResult>('restore_theme')
      setStatus(result.message)
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function loginCodeDrobe() {
    setBusy('auth')
    setStatus('正在打开 CodeDrobe 安全登录…')
    setStatusTone('idle')
    try {
      const auth = await invoke<CodeDrobeAuthStatus>('login_codedrobe', { proxy })
      setCodeDrobeAuth(auth)
      if (auth.creatorHandle) {
        setStatus(`CodeDrobe 创作者 @${auth.creatorHandle} 登录成功，现在可以上传本地 AI 主题。`)
        setStatusTone('success')
      } else {
        setStatus('CodeDrobe 登录成功；请先创建创作者资料，再上传主题。')
        setStatusTone('warning')
      }
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function logoutCodeDrobe() {
    setBusy('auth')
    setStatus('正在退出 CodeDrobe…')
    setStatusTone('idle')
    try {
      const auth = await invoke<CodeDrobeAuthStatus>('logout_codedrobe', { proxy })
      setCodeDrobeAuth(auth)
      setConfirmSubmit(false)
      setStatus('已退出 CodeDrobe，Launch Deck 未保存任何登录凭据。')
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function refreshCodeDrobeProfile() {
    setBusy('auth')
    setStatus('正在重新检查 CodeDrobe 创作者资料…')
    setStatusTone('idle')
    try {
      const auth = await invoke<CodeDrobeAuthStatus>('get_codedrobe_auth_status', { proxy })
      setCodeDrobeAuth(auth)
      if (auth.creatorHandle) {
        setStatus(`CodeDrobe 创作者 @${auth.creatorHandle} 已就绪。`)
        setStatusTone('success')
      } else {
        setStatus('尚未检测到创作者资料，请在 CodeDrobe 账号页完成创建后再试。')
        setStatusTone('warning')
      }
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  async function publishSelectedTheme(submit: boolean) {
    if (!selectedTheme.generated || !publishInfo?.ready) return
    setBusy('publish')
    setStatus(submit ? '正在提交 CodeDrobe 商店审核…' : '正在上传 CodeDrobe 主题草稿…')
    setStatusTone('idle')
    try {
      const result = await invoke<ThemePublishResult>('publish_generated_theme', {
        request: { proxy, theme: themeSelection(selectedTheme), submit },
      })
      setPublishStoreUrl(result.storeUrl ?? null)
      setConfirmSubmit(false)
      setStatus(result.message)
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
  }

  const normalizedQuery = deferredQuery.trim().toLocaleLowerCase()
  let visibleThemes = themes.filter((theme) => {
    if (view === 'installed' && !isInstalled(theme, appState.cachedThemes)) return false
    if (!normalizedQuery) return true
    return [
      themeName(theme),
      theme.name.en,
      theme.slug,
      theme.author?.displayName,
      theme.author?.handle,
    ].some((value) => value?.toLocaleLowerCase().includes(normalizedQuery))
  })
  visibleThemes = [...visibleThemes].sort((left, right) => {
    if (left.bundled) return -1
    if (right.bundled) return 1
    if (sort === 'name') return themeName(left).localeCompare(themeName(right), 'zh-CN')
    if (sort === 'newest') return right.publishedAt.localeCompare(left.publishedAt)
    return right.downloadCount - left.downloadCount
  })

  return (
    <div className="window-shell">
      <TitleBar />
      <div className={`app-frame ${view === 'create' || view === 'settings' ? 'without-detail' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark"><span>01</span></div>
          <div>
            <strong>LAUNCH DECK</strong>
            <small>Codex Proxy Studio</small>
          </div>
        </div>

        <nav>
          <button className={view === 'discover' ? 'active' : ''} onClick={() => setView('discover')}>
            <Icon name="grid" /><span>主题画廊</span><b>{themes.length}</b>
          </button>
          <button className={view === 'installed' ? 'active' : ''} onClick={() => setView('installed')}>
            <Icon name="download" /><span>已安装</span><b>{appState.cachedThemes.length + 1}</b>
          </button>
          <button className={view === 'create' ? 'active' : ''} onClick={() => setView('create')}>
            <Icon name="spark" /><span>AI 创作</span>
          </button>
          <button className={view === 'settings' ? 'active' : ''} onClick={() => setView('settings')}>
            <Icon name="settings" /><span>启动设置</span>
          </button>
        </nav>

        <div className="system-card">
          <div className="system-card-title"><Icon name="pulse" />运行环境</div>
          <StatusRow label="Codex Desktop" ok={appState.codexInstalled} />
          <StatusRow label="CodeDrobe Core" ok={appState.codedrobeAvailable} />
          <StatusRow label="Codex 进程" ok={appState.codexRunning} running />
        </div>

        <button className="official-link" onClick={() => void openUrl('https://codedrobe.app/themes')}>
          <span>CodeDrobe 官方商店</span><Icon name="arrow" />
        </button>
        <div className="sidebar-foot">LOCAL FIRST · REVERSIBLE</div>
      </aside>

      <main>
        <section className="launch-ribbon">
          <div className="launch-copy">
            <span className="eyebrow"><i /> CODEX LAUNCH CONTROL</span>
            <h1>一次启动，<em>代理与主题</em>同时就绪。</h1>
            <p>不改变 Windows 系统代理。主题由 CodeDrobe Core 校验、应用并可随时恢复。</p>
          </div>
          <div className="proxy-console">
            <label>
              <span>PROXY HOST</span>
              <input value={proxy.host} onChange={(event) => setProxy({ ...proxy, host: event.target.value })} />
            </label>
            <div className="port-field">
              <span>PORT</span>
              <div className="port-control">
                <button type="button" onClick={() => updateProxyPort(proxy.port - 1)} aria-label="端口减一">−</button>
                <input
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  aria-label="代理端口"
                  value={proxy.port || ''}
                  onChange={(event) => {
                    const digits = event.target.value.replace(/\D/g, '').slice(0, 5)
                    setProxy({ ...proxy, port: digits ? Math.min(65535, Number(digits)) : 0 })
                  }}
                  onBlur={() => updateProxyPort(proxy.port || 1)}
                />
                <button type="button" onClick={() => updateProxyPort(proxy.port + 1)} aria-label="端口加一">+</button>
              </div>
            </div>
            <button className="probe-button" onClick={() => void checkProxy()} disabled={busy !== null}>
              <Icon name="pulse" />检测
            </button>
            <div className="theme-toggle">
              <button className={themeEnabled ? 'on' : ''} onClick={() => setThemeEnabled(!themeEnabled)} aria-label="切换主题应用">
                <i />
              </button>
              <span>{themeEnabled ? themeName(selectedTheme) : '仅代理启动'}</span>
            </div>
            <button className="launch-button" onClick={() => void runPrimaryAction()} disabled={busy !== null}>
              <span className="launch-button-icon"><Icon name="play" /></span>
              <span>
                <small>{busy === 'apply' ? 'APPLYING' : busy === 'launch' ? 'PREPARING' : 'READY'}</small>
                {appState.codexRunning && themeEnabled ? '即时应用主题' : '启动 CODEX'}
              </span>
              <Icon name="arrow" />
            </button>
          </div>
        </section>

        <div className={`status-strip ${statusTone}`} role="status" aria-live="polite">
          <span className="status-light" />
          <p>{status}</p>
          {busy === 'download' && downloadProgress !== null && <span className="status-percent">{Math.round(downloadProgress)}%</span>}
          {busy && (
            <span
              className={`status-progress ${busy === 'download' ? 'determinate' : ''}`}
              style={busy === 'download' && downloadProgress !== null ? { width: `${downloadProgress}%` } : undefined}
            />
          )}
        </div>

        {(view === 'discover' || view === 'installed') ? (
          <section className="gallery-section">
            <header className="section-header">
              <div>
                <span className="section-kicker">{view === 'discover' ? 'CURATED FOR CODEX' : 'READY OFFLINE'}</span>
                <h2>{view === 'discover' ? '选择今天的工作氛围' : '已安装主题'}</h2>
              </div>
              <div className="gallery-tools">
                <label className="search-box"><Icon name="search" /><input placeholder="搜索主题或作者" value={query} onChange={(event) => setQuery(event.target.value)} /></label>
                <select value={sort} onChange={(event) => setSort(event.target.value as typeof sort)}>
                  <option value="downloads">最多下载</option>
                  <option value="newest">最新发布</option>
                  <option value="name">名称排序</option>
                </select>
                <button className="refresh-button" onClick={() => void refreshThemes()} disabled={busy !== null} aria-label="刷新主题">
                  <Icon name="refresh" />
                </button>
              </div>
            </header>

            <div className="theme-grid">
              {visibleThemes.map((theme, index) => (
                <ThemeCard
                  key={theme.id}
                  theme={theme}
                  index={index}
                  installed={isInstalled(theme, appState.cachedThemes)}
                  appearance={themeAppearance(theme, appState.themeAppearances)}
                  selected={selectedTheme.slug === theme.slug}
                  onSelect={() => setSelectedTheme(theme)}
                />
              ))}
              {visibleThemes.length === 0 && (
                <div className="empty-state">
                  <Icon name="search" />
                  <h3>没有匹配的主题</h3>
                  <p>换个关键词，或者回到主题画廊刷新商店。</p>
                </div>
              )}
            </div>
          </section>
        ) : view === 'create' ? (
          <AiCreatePanel
            capability={aiCapability}
            prompt={aiPrompt}
            appearance={aiAppearance}
            visualMode={aiVisualMode}
            imagePath={aiImagePath}
            job={aiJob}
            installing={busy === 'skill'}
            onPromptChange={setAiPrompt}
            onAppearanceChange={setAiAppearance}
            onVisualModeChange={(mode) => {
              setAiVisualMode(mode)
              if (mode !== 'upload') setAiImagePath(null)
            }}
            onChooseImage={() => void chooseAiBackground()}
            onInstall={() => void installAiSkill()}
            onGenerate={() => void generateAiTheme()}
            onOpenInstalled={() => setView('installed')}
          />
        ) : (
          <SettingsPanel
            appState={appState}
            busy={busy}
            onRestore={() => void restore()}
            onOpenStore={() => void openUrl('https://codedrobe.app/download')}
          />
        )}
      </main>

      {(view === 'discover' || view === 'installed') && (
        <aside className="theme-detail">
          <button className="detail-cover" onClick={() => void cacheSelectedTheme()} disabled={busy !== null}>
            <img src={selectedTheme.coverUrl ?? selectedTheme.previewUrl ?? ''} alt="" onError={hideBrokenImage} />
            <span className="detail-cover-badge">{selectedTheme.bundled ? 'BUNDLED' : isInstalled(selectedTheme, appState.cachedThemes) ? 'INSTALLED' : 'STORE'}</span>
            <span className="detail-cover-action"><Icon name="download" />{selectedTheme.bundled ? '离线可用' : '缓存主题'}</span>
          </button>
          <div className="detail-body">
            <span className="detail-index">SELECTED THEME / {selectedTheme.version}</span>
            <h3>{themeName(selectedTheme)}</h3>
            <p>{selectedTheme.description?.zh || selectedTheme.description?.en || '由 CodeDrobe 社区提供的 Codex 主题。'}</p>
            <div className="theme-meta">
              <span>BY <b>{selectedTheme.author?.displayName || selectedTheme.author?.handle || 'COMMUNITY'}</b></span>
              {!selectedTheme.bundled && <span><b>{selectedTheme.downloadCount}</b> DOWNLOADS</span>}
            </div>
            <div className="tag-row">
              <span>{themeAppearance(selectedTheme, appState.themeAppearances) === 'dark' ? '深色基底' : themeAppearance(selectedTheme, appState.themeAppearances) === 'light' ? '浅色基底' : '基底待识别'}</span>
              {selectedTheme.categories.slice(0, 3).map((category) => <span key={category.slug}>{category.name.zh || category.slug}</span>)}
            </div>
            {busy === 'download' && downloadProgress !== null && (
              <div
                className="download-progress"
                role="progressbar"
                aria-label={`正在下载并校验 ${themeName(selectedTheme)}`}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={Math.round(downloadProgress)}
              >
                <div className="download-progress-copy">
                  <span>下载与安全校验</span>
                  <b>{Math.round(downloadProgress)}%</b>
                </div>
                <div className="download-progress-track"><i style={{ width: `${downloadProgress}%` }} /></div>
              </div>
            )}
            {selectedTheme.generated && isInstalled(selectedTheme, appState.cachedThemes) && (
              <section className="publisher-card">
                <div className="publisher-head">
                  <div>
                    <span>CREATOR PUBLISH</span>
                    <strong>发布到 CodeDrobe</strong>
                  </div>
                  <span className={`auth-chip ${codedrobeAuth.creatorHandle ? 'online' : ''}`}>
                    {!codedrobeAuth.loggedIn ? '未登录' : codedrobeAuth.creatorHandle ? `@${codedrobeAuth.creatorHandle}` : '待完善'}
                  </span>
                </div>
                <p>通过官方 CLI 安全连接。Launch Deck 不读取、不保存你的账号凭据。</p>
                {publishInfo === null ? (
                  <div className="publish-note">正在检查商店资料…</div>
                ) : !publishInfo.ready ? (
                  <div className="publish-note warning">
                    <b>暂不可发布</b>
                    <span>{publishInfo.missing.join('、')}</span>
                  </div>
                ) : (
                  <div className="publish-note ready">
                    <b>商店资料完整</b>
                    <span>{publishInfo.categories.join(' · ') || '分类已就绪'}</span>
                  </div>
                )}
                {publishInfo && !publishInfo.hasCover && (
                  <div className="cover-warning">建议补充 hero 或 cover 图片，商店展示会更完整。</div>
                )}
                {!codedrobeAuth.loggedIn ? (
                  <button className="publisher-login" onClick={() => void loginCodeDrobe()} disabled={busy !== null}>
                    {busy === 'auth' ? '等待浏览器确认…' : '登录 CodeDrobe'}
                  </button>
                ) : !codedrobeAuth.creatorHandle ? (
                  <div className="creator-setup">
                    <div className="publish-note warning">
                      <b>还差一步：创建创作者资料</b>
                      <span>CodeDrobe 要求先设置公开 Handle 和显示名称，之后才能上传主题。</span>
                    </div>
                    <button
                      className="publisher-login"
                      onClick={() => void openUrl(`${codedrobeAuth.baseUrl}/zh/account`)}
                      disabled={busy !== null}
                    >
                      完善创作者资料 →
                    </button>
                    <button className="profile-refresh" onClick={() => void refreshCodeDrobeProfile()} disabled={busy !== null}>
                      {busy === 'auth' ? '检查中…' : '我已完成，重新检查'}
                    </button>
                    <button className="publisher-logout" onClick={() => void logoutCodeDrobe()} disabled={busy !== null}>退出 CodeDrobe</button>
                  </div>
                ) : (
                  <>
                    <div className="publish-actions">
                      <button onClick={() => void publishSelectedTheme(false)} disabled={busy !== null || !publishInfo?.ready}>
                        {busy === 'publish' ? '处理中…' : '上传草稿'}
                      </button>
                      <button className="review" onClick={() => setConfirmSubmit(true)} disabled={busy !== null || !publishInfo?.ready}>
                        提交审核
                      </button>
                    </div>
                    {confirmSubmit && (
                      <div className="submit-confirm">
                        <p>提交后将进入 CodeDrobe 审核队列，确认继续吗？</p>
                        <div>
                          <button onClick={() => void publishSelectedTheme(true)} disabled={busy !== null}>确认提交审核</button>
                          <button onClick={() => setConfirmSubmit(false)} disabled={busy !== null}>取消</button>
                        </div>
                      </div>
                    )}
                    <button className="publisher-logout" onClick={() => void logoutCodeDrobe()} disabled={busy !== null}>退出 CodeDrobe</button>
                  </>
                )}
                {publishStoreUrl && (
                  <button className="store-result" onClick={() => void openUrl(publishStoreUrl)}>查看商店页面 →</button>
                )}
              </section>
            )}
            <button
              className="select-launch"
              onClick={() => {
                setThemeEnabled(true)
                if (appState.codexRunning) void applySelectedTheme()
                else void launch()
              }}
              disabled={busy !== null}
            >
              <Icon name="spark" />
              <span>{appState.codexRunning ? '立即应用到运行中的 Codex' : '应用并通过代理启动'}</span>
              <Icon name="arrow" />
            </button>
          </div>
        </aside>
      )}
      </div>
    </div>
  )
}

function TitleBar() {
  const appWindow = getCurrentWindow()

  const isWindowControl = (target: EventTarget | null) =>
    target instanceof Element && Boolean(target.closest('.window-controls'))

  return (
    <header
      className="custom-titlebar"
      onMouseDownCapture={(event) => {
        if (event.button === 0 && !isWindowControl(event.target)) {
          event.preventDefault()
          void appWindow.startDragging()
        }
      }}
      onDoubleClick={(event) => {
        if (!isWindowControl(event.target)) {
          void appWindow.toggleMaximize()
        }
      }}
    >
      <div className="titlebar-drag-zone">
        <div className="titlebar-brand">
          <span>01</span>
          <strong>Codex Proxy Launch Deck</strong>
          <i>LOCAL</i>
        </div>
      </div>
      <div className="window-controls">
        <button aria-label="最小化" onClick={() => void appWindow.minimize()}><span className="minimize-icon" /></button>
        <button aria-label="最大化" onClick={() => void appWindow.toggleMaximize()}><span className="maximize-icon" /></button>
        <button className="close-control" aria-label="关闭" onClick={() => void appWindow.close()}><span className="close-icon" /></button>
      </div>
    </header>
  )
}

function ThemeCard({ theme, index, installed, appearance, selected, onSelect }: { theme: Theme; index: number; installed: boolean; appearance?: 'light' | 'dark'; selected: boolean; onSelect: () => void }) {
  return (
    <button className={`theme-card ${selected ? 'selected' : ''}`} style={{ '--delay': `${Math.min(index, 12) * 35}ms` } as React.CSSProperties} onClick={onSelect}>
      <div className="card-art">
        <img src={theme.coverUrl ?? theme.previewUrl ?? ''} alt="" loading="lazy" onError={hideBrokenImage} />
        <span className="card-number">{String(index + 1).padStart(2, '0')}</span>
        {installed && <span className="installed-pill">LOCAL</span>}
        <span className={`appearance-pill ${appearance || 'unknown'}`}>{appearance === 'dark' ? 'DARK' : appearance === 'light' ? 'LIGHT' : 'MODE ?'}</span>
      </div>
      <div className="card-copy">
        <div>
          <h3>{themeName(theme)}</h3>
          <p>{theme.author?.displayName || theme.author?.handle || 'CodeDrobe Community'}</p>
        </div>
        <span className="card-arrow"><Icon name="arrow" /></span>
      </div>
    </button>
  )
}

function StatusRow({ label, ok, running = false }: { label: string; ok: boolean; running?: boolean }) {
  return <div className="system-row"><span>{label}</span><i className={ok ? (running ? 'running' : 'ok') : ''}>{ok ? (running ? 'RUNNING' : 'READY') : 'CHECK'}</i></div>
}

function AiCreatePanel({ capability, prompt, appearance, visualMode, imagePath, job, installing, onPromptChange, onAppearanceChange, onVisualModeChange, onChooseImage, onInstall, onGenerate, onOpenInstalled }: {
  capability: AiThemeCapability
  prompt: string
  appearance: 'light' | 'dark'
  visualMode: 'css' | 'upload' | 'ai'
  imagePath: string | null
  job: AiThemeJob | null
  installing: boolean
  onPromptChange: (value: string) => void
  onAppearanceChange: (value: 'light' | 'dark') => void
  onVisualModeChange: (value: 'css' | 'upload' | 'ai') => void
  onChooseImage: () => void
  onInstall: () => void
  onGenerate: () => void
  onOpenInstalled: () => void
}) {
  const ready = capability.codexCliAvailable && capability.skillInstalled
  const running = job?.status === 'running'
  return (
    <section className="ai-create-panel">
      <header className="ai-create-header">
        <div>
          <span className="section-kicker">CREATE WITH LOCAL CODEX</span>
          <h2>用一句描述，创作你的 Codex 主题</h2>
          <p>直接调用你已登录的本机 Codex，不需要配置 API Key。生成过程运行在独立沙箱中，完成后自动校验并加入主题库。</p>
        </div>
        <div className="ai-capability-card">
          <StatusRow label="Codex CLI" ok={capability.codexCliAvailable} />
          <StatusRow label="主题创作 Skill" ok={capability.skillInstalled} />
          {!capability.skillInstalled && (
            <button onClick={onInstall} disabled={installing || !capability.codexCliAvailable}>
              <Icon name="download" />{installing ? '正在安装…' : '一键安装主题能力'}
            </button>
          )}
        </div>
      </header>

      <div className="ai-workbench">
        <div className="ai-prompt-card">
          <label htmlFor="ai-theme-prompt">你想要什么样的主题？</label>
          <div className="appearance-choice" aria-label="Codex 主题基底">
            <span>适配基底</span>
            <div>
              <button className={appearance === 'dark' ? 'active' : ''} onClick={() => onAppearanceChange('dark')} disabled={running}>
                <i className="dark-swatch" />深色主题
              </button>
              <button className={appearance === 'light' ? 'active' : ''} onClick={() => onAppearanceChange('light')} disabled={running}>
                <i className="light-swatch" />浅色主题
              </button>
            </div>
          </div>
          <div className="visual-mode-choice" aria-label="主题背景素材">
            <span>背景素材</span>
            <div>
              <button className={visualMode === 'css' ? 'active' : ''} onClick={() => onVisualModeChange('css')} disabled={running}>纯 CSS</button>
              <button className={visualMode === 'upload' ? 'active' : ''} onClick={() => onVisualModeChange('upload')} disabled={running}>上传参考图</button>
              <button className={visualMode === 'ai' ? 'active' : ''} onClick={() => onVisualModeChange('ai')} disabled={running}>AI 生成图</button>
            </div>
          </div>
          {visualMode === 'upload' && (
            <button className="image-picker" onClick={onChooseImage} disabled={running}>
              <Icon name="download" />
              <span>{imagePath ? imagePath.split(/[\\/]/).pop() : '选择参考图 PNG / JPG / WebP / GIF'}</span>
            </button>
          )}
          {visualMode === 'upload' && imagePath && <p className="visual-mode-note">AI 会提取至少 5 个参考图锚点，生成后复核并保留至少 3 个，同时检查图片是否匹配浅色/深色基底；不会直接打包原图。</p>}
          {visualMode === 'ai' && <p className="visual-mode-note">Codex 会生成 16:9 主背景，按风格决定是否补充纹理素材，并用 CSS 完成卡片、控件与装饰细节。</p>}
          <textarea
            id="ai-theme-prompt"
            value={prompt}
            maxLength={4000}
            onChange={(event) => onPromptChange(event.target.value)}
            placeholder="例如：做一个深海夜航风格的深色主题，主色是墨蓝和荧光青，卡片像半透明潜艇舷窗，文字清晰克制，不要大面积高饱和色。"
          />
          <div className="ai-prompt-foot">
            <span>{prompt.length} / 4000</span>
            <button onClick={onGenerate} disabled={!ready || running || prompt.trim().length < 8 || (visualMode === 'upload' && !imagePath)}>
              <Icon name="spark" />{running ? 'Codex 正在创作…' : '开始 AI 创作'}
            </button>
          </div>
          {!ready && <p className="ai-hint">首次使用需要 Codex CLI 和 CodeDrobe 主题创作 Skill 均为 READY。</p>}
          {ready && <p className="ai-hint">生成包会锁定为{appearance === 'dark' ? '深色' : '浅色'}基底；应用时 CodeDrobe 会同步切换 Codex 原生外观。</p>}
        </div>

        <div className={`ai-job-card ${job?.status || 'idle'}`}>
          <div className="ai-job-head">
            <div>
              <span>GENERATION STATUS</span>
              <h3>{job?.phase || '等待创作任务'}</h3>
            </div>
            <b>{job ? `${job.progress}%` : 'IDLE'}</b>
          </div>
          <div className="ai-progress-track"><i style={{ width: `${job?.progress || 0}%` }} /></div>
          <div className="ai-log" aria-live="polite">
            {job ? job.logs.slice(-8).map((line, index) => <p key={`${index}-${line}`}>{line}</p>) : (
              <p>写下颜色、氛围、材质和布局偏好，Codex 会生成主视觉、可选纹理和完整的界面细节。</p>
            )}
          </div>
          {job?.status === 'completed' && (
            <button className="ai-installed-button" onClick={onOpenInstalled}>
              查看并应用生成的主题 <Icon name="arrow" />
            </button>
          )}
          {job?.status === 'failed' && <p className="ai-error">{job.error}</p>}
        </div>
      </div>
      <p className="ai-safety-note">生成会使用你的 Codex 账户额度。Launch Deck 不会读取或保存 Codex 凭据，也不会在生成期间重启正在运行的 Codex。</p>
    </section>
  )
}

function SettingsPanel({ appState, busy, onRestore, onOpenStore }: { appState: AppState; busy: BusyAction; onRestore: () => void; onOpenStore: () => void }) {
  return (
    <section className="settings-panel">
      <header>
        <span className="section-kicker">SYSTEM & RECOVERY</span>
        <h2>启动设置</h2>
        <p>启动台只为本次 Codex 进程设置代理环境变量，不会修改 Windows 全局代理。</p>
      </header>
      <div className="settings-grid">
        <article>
          <span className="setting-number">01</span>
          <h3>主题缓存</h3>
          <p>线上主题通过 CodeDrobe Core 下载并完成 SHA-256 校验。</p>
          <code>{appState.cacheDirectory || '正在定位缓存目录…'}</code>
        </article>
        <article>
          <span className="setting-number">02</span>
          <h3>恢复原生外观</h3>
          <p>移除 CodeDrobe 注入层与受控外观配置，不影响会话和项目。</p>
          <button onClick={onRestore} disabled={busy !== null}>恢复 Codex 原生外观</button>
        </article>
        <article>
          <span className="setting-number">03</span>
          <h3>CodeDrobe 创作者连接</h3>
          <p>Launch Deck 已支持通过官方 CLI 登录和发布本地 AI 主题；完整商店与跨应用管理仍可使用 CodeDrobe Desktop。</p>
          <button className="secondary" onClick={onOpenStore}>下载 CodeDrobe Desktop</button>
        </article>
      </div>
    </section>
  )
}

function themeName(theme: Theme) {
  return theme.name.zh || theme.name.en || theme.slug
}

function themeSelection(theme: Theme) {
  return { slug: theme.slug, version: theme.version, bundled: Boolean(theme.bundled) }
}

function isInstalled(theme: Theme, cachedThemes: string[]) {
  return Boolean(theme.bundled) || cachedThemes.some((file) => file.startsWith(`${theme.slug}-${theme.version}`))
}

function themeAppearance(theme: Theme, appearances: AppState['themeAppearances']) {
  return theme.appearanceMode || appearances[`${theme.slug}@${theme.version}`]
}

function mergeThemes(generatedThemes: Theme[], marketplaceThemes: Theme[]) {
  // A slug is the stable theme identity. Prefer offline/generated copies over
  // marketplace versions so one theme never renders as multiple cards.
  const seen = new Set<string>([bundledTheme.slug])
  const merged = [bundledTheme]
  for (const theme of [...generatedThemes, ...marketplaceThemes]) {
    if (seen.has(theme.slug)) continue
    seen.add(theme.slug)
    merged.push(theme)
  }
  return merged
}

function formatError(error: unknown) {
  if (typeof error === 'string') return error
  if (error instanceof Error) return error.message
  return '操作失败，请检查代理和 CodeDrobe 环境。'
}

function hideBrokenImage(event: React.SyntheticEvent<HTMLImageElement>) {
  event.currentTarget.style.display = 'none'
}

export default App
