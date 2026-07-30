import { startTransition, useDeferredValue, useEffect, useEffectEvent, useState } from 'react'
import { invoke } from '@tauri-apps/api/core'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { openUrl } from '@tauri-apps/plugin-opener'
import mikuHero from './assets/miku-hero.png'
import './App.css'

type View = 'discover' | 'installed' | 'settings'
type BusyAction = 'proxy' | 'themes' | 'download' | 'launch' | 'apply' | 'restore' | null

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
}

interface ActionResult {
  message: string
  themePath?: string | null
  warning: boolean
}

const bundledTheme: Theme = {
  id: 'bundled-miku',
  slug: 'miku-future-beats',
  name: {
    zh: '初音未来 · Future Beats',
    en: 'Hatsune Miku · Future Beats',
  },
  version: '1.2.1',
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
}

const initialState: AppState = {
  codexInstalled: false,
  codexRunning: false,
  codedrobeAvailable: false,
  cachedThemes: [],
  cacheDirectory: '',
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
  const [status, setStatus] = useState('正在连接启动台…')
  const [statusTone, setStatusTone] = useState<'idle' | 'success' | 'warning' | 'error'>('idle')
  const deferredQuery = useDeferredValue(query)

  const bootstrap = useEffectEvent(async () => {
    try {
      const [state, page] = await Promise.all([
        invoke<AppState>('get_app_state'),
        invoke<MarketplacePage>('list_themes', { proxy }),
      ])
      startTransition(() => {
        setAppState(state)
        setThemes([bundledTheme, ...page.themes.filter((theme) => theme.slug !== bundledTheme.slug)])
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
        setThemes([bundledTheme, ...page.themes.filter((theme) => theme.slug !== bundledTheme.slug)])
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
    setStatus(`正在安全下载 ${themeName(selectedTheme)}…`)
    setStatusTone('idle')
    try {
      const result = await invoke<ActionResult>('download_theme', {
        proxy,
        theme: themeSelection(selectedTheme),
      })
      const state = await invoke<AppState>('get_app_state')
      setAppState(state)
      setStatus(result.message)
      setStatusTone('success')
    } catch (error) {
      setStatus(formatError(error))
      setStatusTone('error')
    } finally {
      setBusy(null)
    }
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
      <div className="app-frame">
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
            <label className="port-field">
              <span>PORT</span>
              <input type="number" min="1" max="65535" value={proxy.port} onChange={(event) => setProxy({ ...proxy, port: Number(event.target.value) })} />
            </label>
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

        <div className={`status-strip ${statusTone}`}>
          <span className="status-light" />
          <p>{status}</p>
          {busy && <span className="status-progress" />}
        </div>

        {view !== 'settings' ? (
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
        ) : (
          <SettingsPanel
            appState={appState}
            busy={busy}
            onRestore={() => void restore()}
            onOpenStore={() => void openUrl('https://codedrobe.app/download')}
          />
        )}
      </main>

      {view !== 'settings' && (
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
              {selectedTheme.categories.slice(0, 3).map((category) => <span key={category.slug}>{category.name.zh || category.slug}</span>)}
            </div>
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
  return (
    <header
      className="custom-titlebar"
      data-tauri-drag-region
      onMouseDown={(event) => {
        if (event.button === 0 && !(event.target as Element).closest('.window-controls')) {
          void appWindow.startDragging()
        }
      }}
      onDoubleClick={() => void appWindow.toggleMaximize()}
    >
      <div className="titlebar-brand" data-tauri-drag-region>
        <span>01</span>
        <strong data-tauri-drag-region>Codex Proxy Launch Deck</strong>
        <i data-tauri-drag-region>LOCAL</i>
      </div>
      <div className="window-controls">
        <button aria-label="最小化" onClick={() => void appWindow.minimize()}><span className="minimize-icon" /></button>
        <button aria-label="最大化" onClick={() => void appWindow.toggleMaximize()}><span className="maximize-icon" /></button>
        <button className="close-control" aria-label="关闭" onClick={() => void appWindow.close()}><span className="close-icon" /></button>
      </div>
    </header>
  )
}

function ThemeCard({ theme, index, installed, selected, onSelect }: { theme: Theme; index: number; installed: boolean; selected: boolean; onSelect: () => void }) {
  return (
    <button className={`theme-card ${selected ? 'selected' : ''}`} style={{ '--delay': `${Math.min(index, 12) * 35}ms` } as React.CSSProperties} onClick={onSelect}>
      <div className="card-art">
        <img src={theme.coverUrl ?? theme.previewUrl ?? ''} alt="" loading="lazy" onError={hideBrokenImage} />
        <span className="card-number">{String(index + 1).padStart(2, '0')}</span>
        {installed && <span className="installed-pill">LOCAL</span>}
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
          <h3>官方主题管理器</h3>
          <p>登录、收藏、发布和跨应用管理请使用 CodeDrobe Desktop。</p>
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

function formatError(error: unknown) {
  if (typeof error === 'string') return error
  if (error instanceof Error) return error.message
  return '操作失败，请检查代理和 CodeDrobe 环境。'
}

function hideBrokenImage(event: React.SyntheticEvent<HTMLImageElement>) {
  event.currentTarget.style.display = 'none'
}

export default App
