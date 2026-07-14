import { app, BrowserWindow, globalShortcut, ipcMain, Menu, screen } from 'electron'
import { execFile, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { promisify } from 'node:util'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readFile, writeFile } from 'node:fs/promises'
import {
  applySelectionThroughCodexRenderer,
  applySpeedThroughCodexRenderer,
  getCurrentSelectionThroughCodexRenderer,
  getLocalizedUiStringsThroughCodexRenderer,
  getModelCatalogThroughCodexRenderer,
  getSelectorPresentationThroughCodexRenderer,
  getSelectorShortcutThroughCodexRenderer,
  type CodexCurrentSelection,
  type CodexSelectorPresentation,
} from './codex-cdp'
import { openBoundsForAnchor, selectorBoundsToDipRectangle } from './overlay-position'
import { labelsFromModelCatalog } from './model-catalog'

const execFileAsync = promisify(execFile)
const __dirname = dirname(fileURLToPath(import.meta.url))
const APP_ROOT = join(__dirname, '..')
const FALLBACK_CLOSED_SIZE = { width: 136, height: 28 }
const PALETTE_SIZE = { width: 640, height: 306 }
const DEFAULT_PALETTE_SHORTCUT = 'Ctrl+Shift+M'
const FALLBACK_SELECTOR_PRESENTATION: CodexSelectorPresentation = {
  ...FALLBACK_CLOSED_SIZE,
  paddingLeft: '8px',
  paddingRight: '8px',
  gap: '4px',
  border: '1px solid rgba(0, 0, 0, 0)',
  borderRadius: '9999px',
  backgroundColor: 'rgba(0, 0, 0, 0)',
  hoverBackgroundColor: 'rgba(26, 28, 31, 0.053)',
  color: 'rgba(26, 28, 31, 0.494)',
  modelColor: 'rgb(26, 28, 31)',
  fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  fontSize: '13px',
  fontWeight: '400',
  lineHeight: '18px',
  boxShadow: 'none',
  iconSize: 14,
}
type Point = { x: number; y: number }
type NativeLabels = {
  models: string[]
  efforts: string[]
  supportedEfforts: number[][]
  speedLabel: string
  speeds: string[]
  speedIndex: number
  uiStrings?: { enableSilentMode: string; restartingCodex: string }
}
type NativeSelection = {
  modelIndex: number
  effortIndex: number
  speedIndex: number
}

let mainWindow: BrowserWindow | null = null
let paletteOpen = false
let applying = false
let draggingWindow = false
let watcher: ChildProcessWithoutNullStreams | null = null
let closedSize = { ...FALLBACK_CLOSED_SIZE }
let anchor = { x: 951, y: 756 }
let selectorAnchor: Point | null = null
let manualOffset: Point | null = null
let nativeLabels: NativeLabels = {
  models: [],
  efforts: [],
  supportedEfforts: [],
  speedLabel: '',
  speeds: [],
  speedIndex: -1,
}
let nativeLabelsLoad: Promise<void> | null = null
let nativeLabelsComplete = false
let nativeLabelsRetryAfter = 0
let fastServiceTierId: string | null = null
let nativeSelectorPresentation = { ...FALLBACK_SELECTOR_PRESENTATION }
let nativeSelection: NativeSelection | null = null
let lastObservedSelectorName = ''
let selectionSyncRevision = 0
let codexVisible = false
let paletteShortcut = DEFAULT_PALETTE_SHORTCUT
let registeredPaletteShortcut = ''

const helperPath = () =>
  app.isPackaged
    ? join(process.resourcesPath, 'native-overlay.ps1')
    : join(APP_ROOT, 'electron', 'native-overlay.ps1')

const positionPath = () => join(app.getPath('userData'), 'overlay-position.json')

function setNativeLabels(result: Partial<NativeLabels>) {
  if (!Array.isArray(result.models) || result.models.length === 0 || !Array.isArray(result.efforts) || result.efforts.length === 0) {
    return false
  }
  nativeLabels = {
    models: result.models.map(String),
    efforts: result.efforts.map(String),
    supportedEfforts: Array.isArray(result.supportedEfforts)
      ? result.supportedEfforts.map((indices) => Array.isArray(indices) ? indices.map(Number) : [])
      : [],
    speedLabel: typeof result.speedLabel === 'string' ? result.speedLabel : '',
    speeds: Array.isArray(result.speeds) ? result.speeds.map(String).slice(0, 2) : [],
    speedIndex: Number.isInteger(result.speedIndex) ? Number(result.speedIndex) : -1,
    uiStrings: result.uiStrings,
  }
  return true
}

function selectorPresentationForRenderer(): CodexSelectorPresentation {
  return { ...nativeSelectorPresentation, ...closedSize }
}

function broadcastSelectorPresentation() {
  if (!mainWindow || mainWindow.isDestroyed() || mainWindow.webContents.isDestroyed()) return
  mainWindow.webContents.send('overlay:selector-presentation', selectorPresentationForRenderer())
}

function unregisterPaletteShortcut() {
  if (!registeredPaletteShortcut) return
  globalShortcut.unregister(registeredPaletteShortcut)
  registeredPaletteShortcut = ''
}

function syncPaletteShortcutRegistration() {
  unregisterPaletteShortcut()
  if (!codexVisible || !paletteShortcut) return
  const registered = globalShortcut.register(paletteShortcut, () => {
    if (!mainWindow || mainWindow.isDestroyed() || !mainWindow.isVisible()) return
    setPaletteOpen(!paletteOpen)
  })
  if (registered) registeredPaletteShortcut = paletteShortcut
  else console.warn(`[codex-palette] Shortcut unavailable: ${paletteShortcut}`)
}

function setPaletteShortcut(shortcut: string) {
  const next = shortcut.trim() || DEFAULT_PALETTE_SHORTCUT
  if (next === paletteShortcut && registeredPaletteShortcut) return
  paletteShortcut = next
  syncPaletteShortcutRegistration()
}

function setCodexVisible(visible: boolean) {
  if (codexVisible === visible) return
  codexVisible = visible
  syncPaletteShortcutRegistration()
}

function normalizeModelLabel(value: string) {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/^gpt[\s-]*/i, '')
    .replace(/[^a-z0-9]+/gi, ' ')
    .trim()
    .toLowerCase()
}

function setNativeSelectionFromCodex(observed: CodexCurrentSelection) {
  const observedLabel = normalizeModelLabel(observed.modelLabel)
  const matchedModelIndex = nativeLabels.models.findIndex(
    (model) => normalizeModelLabel(model) === observedLabel,
  )
  const modelIndex = matchedModelIndex >= 0 ? matchedModelIndex : observed.modelIndex
  if (
    !Number.isInteger(modelIndex)
    || modelIndex < 0
    || (nativeLabels.models.length > 0 && modelIndex >= nativeLabels.models.length)
    || !Number.isInteger(observed.effortIndex)
    || observed.effortIndex < 0
  ) return

  const speedIndex = observed.speedIndex >= 0 ? observed.speedIndex : nativeLabels.speedIndex
  const next = { modelIndex, effortIndex: observed.effortIndex, speedIndex }
  const changed = !nativeSelection
    || nativeSelection.modelIndex !== next.modelIndex
    || nativeSelection.effortIndex !== next.effortIndex
    || nativeSelection.speedIndex !== next.speedIndex
  nativeSelection = next
  if (speedIndex >= 0) nativeLabels.speedIndex = speedIndex
  if (!changed || !mainWindow || mainWindow.isDestroyed() || mainWindow.webContents.isDestroyed()) return
  mainWindow.webContents.send('overlay:selection-changed', next)
}

async function syncNativeSelection() {
  const revision = ++selectionSyncRevision
  try {
    const cdpPort = await getCodexCdpPort()
    if (!cdpPort) return
    const observed = await getCurrentSelectionThroughCodexRenderer(cdpPort)
    if (revision !== selectionSyncRevision) return
    setNativeSelectionFromCodex(observed)
  } catch {
    // The next UI Automation state change will retry without opening the native menu.
  }
}

async function readNativeLabels() {
  const { stdout } = await execFileAsync(
    'powershell.exe',
    [
      '-NoProfile',
      '-NonInteractive',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      helperPath(),
      '-Mode',
      'labels-fast',
    ],
    { windowsHide: true, timeout: 15_000 },
  )
  return JSON.parse(stdout.trim()) as Partial<NativeLabels>
}

function loadNativeLabels() {
  if (nativeLabelsComplete) return Promise.resolve()
  if (nativeLabelsLoad) return nativeLabelsLoad
  if (Date.now() < nativeLabelsRetryAfter) return Promise.resolve()

  nativeLabelsLoad = (async () => {
    try {
      const cdpPort = await getCodexCdpPort()
      if (cdpPort) {
        const catalog = await getModelCatalogThroughCodexRenderer(cdpPort)
        fastServiceTierId = catalog.flatMap((model) => model.serviceTiers).find((tier) => tier.id)?.id ?? null
        const [uiStrings, selectorPresentation, currentSelection, selectorShortcut] = await Promise.all([
          getLocalizedUiStringsThroughCodexRenderer(cdpPort),
          getSelectorPresentationThroughCodexRenderer(cdpPort).catch(() => null),
          getCurrentSelectionThroughCodexRenderer(cdpPort).catch(() => null),
          getSelectorShortcutThroughCodexRenderer(cdpPort).catch(() => DEFAULT_PALETTE_SHORTCUT),
        ])
        setPaletteShortcut(selectorShortcut)
        if (selectorPresentation) {
          nativeSelectorPresentation = selectorPresentation
          if (!selectorAnchor) {
            closedSize = {
              width: Math.max(60, Math.round(selectorPresentation.width)),
              height: Math.max(20, Math.round(selectorPresentation.height)),
            }
          }
          broadcastSelectorPresentation()
        }
        if (setNativeLabels({ ...labelsFromModelCatalog(catalog, app.getLocale()), uiStrings })) {
          if (currentSelection) setNativeSelectionFromCodex(currentSelection)
          nativeLabelsComplete = true
          startWatcher()
          return
        }
      }
      if (process.env.CODEX_UIA_SCAN_FALLBACK === '1' && setNativeLabels(await readNativeLabels())) {
        nativeLabelsComplete = true
        startWatcher()
        return
      }
      nativeLabelsRetryAfter = Date.now() + 5_000
    } catch {
      // Retry the renderer harness later without touching the native menu.
      nativeLabelsRetryAfter = Date.now() + 5_000
    }
  })().finally(() => {
    nativeLabelsLoad = null
  })

  return nativeLabelsLoad
}

async function getCodexCdpPort() {
  const configured = Number(process.env.CODEX_CDP_PORT)
  if (Number.isInteger(configured) && configured > 0 && configured <= 65_535) return configured
  try {
    const { stdout } = await execFileAsync(
      'powershell.exe',
      [
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        helperPath(),
        '-Mode',
        'cdp',
      ],
      { windowsHide: true, timeout: 5_000 },
    )
    const result = JSON.parse(stdout.trim()) as { ok?: boolean; port?: number | null }
    return result.ok && Number.isInteger(result.port) ? result.port as number : null
  } catch {
    return null
  }
}

function defaultAnchor() {
  const { workArea } = screen.getPrimaryDisplay()
  return {
    x: workArea.x + Math.floor((workArea.width - closedSize.width) / 2),
    y: workArea.y + Math.floor((workArea.height - closedSize.height) / 2),
  }
}

async function loadAnchor() {
  try {
    const saved = JSON.parse(await readFile(positionPath(), 'utf8')) as {
      x?: number
      y?: number
      offsetX?: number
      offsetY?: number
    }
    if (Number.isFinite(saved.x) && Number.isFinite(saved.y)) {
      anchor = { x: saved.x as number, y: saved.y as number }
      if (Number.isFinite(saved.offsetX) && Number.isFinite(saved.offsetY)) {
        manualOffset = { x: saved.offsetX as number, y: saved.offsetY as number }
      }
      return
    }
  } catch {
    // First run or malformed preference: use a centered fallback until Codex is detected.
  }
  anchor = defaultAnchor()
}

async function saveAnchor() {
  const preference = manualOffset
    ? { ...anchor, offsetX: manualOffset.x, offsetY: manualOffset.y }
    : anchor
  await writeFile(positionPath(), JSON.stringify(preference), 'utf8')
}

function boundsFor(open: boolean) {
  if (!open) return { ...anchor, ...closedSize }
  const { workArea } = screen.getDisplayNearestPoint(anchor)
  return openBoundsForAnchor(
    anchor,
    closedSize,
    { width: PALETTE_SIZE.width, height: PALETTE_SIZE.height + closedSize.height },
    workArea,
  )
}

function setPaletteOpen(open: boolean) {
  if (!mainWindow || mainWindow.isDestroyed()) return
  if (!paletteOpen) {
    const [x, y] = mainWindow.getPosition()
    anchor = { x, y }
    void saveAnchor()
  }
  if (!open && selectorAnchor) {
    anchor = selectorAnchor
    manualOffset = null
  }
  paletteOpen = open
  mainWindow.setBounds(boundsFor(open), true)
  if (!mainWindow.webContents.isDestroyed()) mainWindow.webContents.send('overlay:open-changed', open)
}

function createWindow() {
  mainWindow = new BrowserWindow({
    ...boundsFor(false),
    transparent: true,
    frame: false,
    thickFrame: false,
    resizable: false,
    maximizable: false,
    minimizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    hasShadow: false,
    show: false,
    backgroundColor: '#00000000',
    webPreferences: {
      preload: join(__dirname, 'preload.mjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  mainWindow.setAlwaysOnTop(true, 'screen-saver', 1)
  mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true })
  mainWindow.on('moved', () => {
    if (!mainWindow || paletteOpen) return
    const [x, y] = mainWindow.getPosition()
    anchor = { x, y }
    void saveAnchor()
  })

  if (process.env.VITE_DEV_SERVER_URL) {
    void mainWindow.loadURL(process.env.VITE_DEV_SERVER_URL)
  } else {
    void mainWindow.loadFile(join(APP_ROOT, 'dist', 'index.html'))
  }
}

function startWatcher() {
  watcher?.kill()
  watcher = spawn(
    'powershell.exe',
    [
      '-NoProfile',
      '-NonInteractive',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      helperPath(),
      '-Mode',
      'watch',
      '-OverlayPid',
      String(process.pid),
      '-ModelNamesJson',
      JSON.stringify(nativeLabels.models),
    ],
    { windowsHide: true },
  )

  watcher.stdout.setEncoding('utf8')
  let buffer = ''
  watcher.stdout.on('data', (chunk: string) => {
    buffer += chunk
    const lines = buffer.split(/\r?\n/)
    buffer = lines.pop() ?? ''
    for (const line of lines) {
      if (!line.trim() || !mainWindow || mainWindow.isDestroyed()) continue
      try {
        const state = JSON.parse(line) as {
          visible: boolean
          selector?: { x: number; y: number; width: number; height: number; name?: string } | null
        }
        if (state.selector) {
          const selectorName = state.selector.name?.trim() ?? ''
          if (selectorName && selectorName !== lastObservedSelectorName) {
            lastObservedSelectorName = selectorName
            void syncNativeSelection()
          }
          const selectorBounds = selectorBoundsToDipRectangle(
            state.selector,
            (point) => screen.screenToDipPoint(point),
          )
          const nextClosedSize = {
            width: Math.max(60, Math.min(320, selectorBounds.width)),
            height: Math.max(20, Math.min(50, selectorBounds.height)),
          }
          const sizeChanged = nextClosedSize.width !== closedSize.width || nextClosedSize.height !== closedSize.height
          closedSize = nextClosedSize
          selectorAnchor = { x: selectorBounds.x, y: selectorBounds.y }
          manualOffset = null
          if (sizeChanged) broadcastSelectorPresentation()
          if (!draggingWindow) {
            const anchorChanged = selectorAnchor.x !== anchor.x || selectorAnchor.y !== anchor.y
            if (anchorChanged || sizeChanged) {
              anchor = selectorAnchor
              mainWindow.setBounds(boundsFor(paletteOpen), false)
            }
          }
        }
        setCodexVisible(state.visible)
        if (state.visible) {
          mainWindow.showInactive()
          mainWindow.moveTop()
        }
        else mainWindow.hide()
      } catch {
        // Ignore partial diagnostic output from PowerShell.
      }
    }
  })
}

ipcMain.handle('overlay:set-open', (_event, open: boolean) => setPaletteOpen(open))
ipcMain.on('overlay:show-context-menu', () => {
  if (!mainWindow || mainWindow.isDestroyed()) return
  Menu.buildFromTemplate([
    { label: 'Fermer la palette', click: () => app.quit() },
  ]).popup({ window: mainWindow })
})
ipcMain.handle('overlay:enable-silent-mode', async () => {
  const { stdout } = await execFileAsync(
    'powershell.exe',
    ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', helperPath(), '-Mode', 'enable-cdp'],
    { windowsHide: true, timeout: 20_000 },
  )
  const result = JSON.parse(stdout.trim()) as { ok?: boolean; port?: number }
  if (!result.ok || !Number.isInteger(result.port)) throw new Error('Codex could not be restarted in silent mode.')
  nativeLabelsComplete = false
  nativeLabelsRetryAfter = 0
  fastServiceTierId = null
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (await getCodexCdpPort()) break
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  await loadNativeLabels()
  return { ok: true, port: result.port }
})
ipcMain.handle('overlay:get-labels', () => {
  void loadNativeLabels()
  return {
    ...nativeLabels,
    currentSelection: nativeSelection,
    complete: nativeLabelsComplete,
    selectorPresentation: selectorPresentationForRenderer(),
  }
})
ipcMain.handle('overlay:begin-drag', () => {
  draggingWindow = true
  if (!mainWindow || mainWindow.isDestroyed()) throw new Error('The overlay is not ready.')
  const [x, y] = mainWindow.getPosition()
  return { x, y }
})
ipcMain.on('overlay:drag-to', (_event, position: { x: number; y: number }) => {
  if (!mainWindow || mainWindow.isDestroyed()) return
  if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) return
  mainWindow.setPosition(Math.round(position.x), Math.round(position.y), false)
})
ipcMain.handle('overlay:end-drag', async () => {
  if (!mainWindow || mainWindow.isDestroyed()) return
  const [x, y] = mainWindow.getPosition()
  const openSize = { width: PALETTE_SIZE.width, height: PALETTE_SIZE.height + closedSize.height }
  anchor = paletteOpen
    ? {
        x: x + (openSize.width - closedSize.width),
        y: y + (openSize.height - closedSize.height),
      }
    : { x, y }
  draggingWindow = false
  if (selectorAnchor) {
    manualOffset = {
      x: anchor.x - selectorAnchor.x,
      y: anchor.y - selectorAnchor.y,
    }
  }
  await saveAnchor()
})
ipcMain.handle('overlay:reset-position', async () => {
  manualOffset = selectorAnchor ? { x: 0, y: 0 } : null
  anchor = selectorAnchor ?? defaultAnchor()
  await saveAnchor()
  mainWindow?.setBounds(boundsFor(paletteOpen), true)
})
ipcMain.handle('overlay:apply-speed', async (_event, speedIndex: number) => {
  if (!Number.isInteger(speedIndex) || speedIndex < 0 || speedIndex > 1) {
    throw new Error('The requested speed is invalid.')
  }
  applying = true
  try {
    const cdpPort = await getCodexCdpPort()
    if (cdpPort && fastServiceTierId) {
      const result = await applySpeedThroughCodexRenderer(cdpPort, speedIndex, fastServiceTierId)
      nativeLabels.speedIndex = speedIndex
      return result
    }
    if (process.env.CODEX_UIA_MODIFIER_FALLBACK !== '1') {
      throw new Error('Codex silent integration is unavailable.')
    }
    const { stdout } = await execFileAsync(
      'powershell.exe',
      [
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        helperPath(),
        '-Mode',
        'speed',
        '-SpeedIndex',
        String(speedIndex),
      ],
      { windowsHide: true, timeout: 20_000 },
    )
    const result = JSON.parse(stdout.trim()) as { ok: boolean; speedIndex?: number; speed?: string }
    if (result.ok) nativeLabels.speedIndex = speedIndex
    return result
  } catch (error) {
    const stderr =
      typeof error === 'object' && error !== null && 'stderr' in error
        ? String((error as { stderr?: unknown }).stderr ?? '')
        : ''
    const firstLine = stderr.split(/\r?\n/).find((line) => line.trim())?.trim()
    throw new Error(firstLine || 'Codex did not confirm the requested speed.')
  } finally {
    applying = false
  }
})

ipcMain.handle(
  'overlay:apply',
  async (_event, selection: { modelIndex: number; effortIndex: number }) => {
    if (!mainWindow) throw new Error('The overlay is not ready.')
    applying = true
    // Keep the overlay stable while Codex's native selector callback settles.
    try {
      const cdpPort = await getCodexCdpPort()
      const modelLabel = nativeLabels.models[selection.modelIndex]
      if (cdpPort && modelLabel) {
        try {
          return await applySelectionThroughCodexRenderer(cdpPort, {
            modelLabel,
            modelIndex: selection.modelIndex,
            effortIndex: selection.effortIndex,
          })
        } catch (error) {
          const message = error instanceof Error ? error.message : String(error)
          console.warn(`[codex-palette] Internal CDP selection unavailable: ${message}`)
        }
      }
      if (process.env.CODEX_UIA_MODIFIER_FALLBACK !== '1') {
        throw new Error('Codex silent integration is unavailable.')
      }
      const { stdout } = await execFileAsync(
        'powershell.exe',
        [
          '-NoProfile',
          '-NonInteractive',
          '-ExecutionPolicy',
          'Bypass',
          '-File',
          helperPath(),
          '-Mode',
          'apply',
          '-ModelIndex',
          String(selection.modelIndex),
          '-EffortIndex',
          String(selection.effortIndex),
        ],
        { windowsHide: true, timeout: 20_000 },
      )
      return JSON.parse(stdout.trim()) as { ok: boolean }
    } catch (error) {
      const stderr =
        typeof error === 'object' && error !== null && 'stderr' in error
          ? String((error as { stderr?: unknown }).stderr ?? '')
          : ''
      const firstLine = stderr.split(/\r?\n/).find((line) => line.trim())?.trim()
      throw new Error(firstLine || 'Codex did not confirm the requested selection.')
    } finally {
      applying = false
    }
  },
)
ipcMain.handle('overlay:quit', () => app.quit())

app.whenReady().then(async () => {
  await loadAnchor()
  createWindow()
  startWatcher()
  void loadNativeLabels()
})

app.on('before-quit', () => {
  unregisterPaletteShortcut()
  watcher?.kill()
})
app.on('window-all-closed', () => app.quit())
