import { app, BrowserWindow, ipcMain, screen } from 'electron'
import { execFile, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { promisify } from 'node:util'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readFile, writeFile } from 'node:fs/promises'

const execFileAsync = promisify(execFile)
const __dirname = dirname(fileURLToPath(import.meta.url))
const APP_ROOT = join(__dirname, '..')
const CLOSED_SIZE = { width: 212, height: 50 }
const OPEN_SIZE = { width: 680, height: 360 }
type Point = { x: number; y: number }
type NativeLabels = {
  efforts: string[]
  speedLabel: string
  speeds: string[]
  speedIndex: number
}

let mainWindow: BrowserWindow | null = null
let paletteOpen = false
let applying = false
let draggingWindow = false
let watcher: ChildProcessWithoutNullStreams | null = null
let anchor = { x: 951, y: 756 }
let selectorAnchor: Point | null = null
let manualOffset: Point | null = null
let nativeLabels: NativeLabels = {
  efforts: ['Léger', 'Moyen', 'Élevé', 'Très élevé', 'Ultra'],
  speedLabel: '',
  speeds: [],
  speedIndex: -1,
}

const helperPath = () =>
  app.isPackaged
    ? join(process.resourcesPath, 'native-overlay.ps1')
    : join(APP_ROOT, 'electron', 'native-overlay.ps1')

const positionPath = () => join(app.getPath('userData'), 'overlay-position.json')

async function loadNativeLabels() {
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
        'labels',
      ],
      { windowsHide: true, timeout: 12_000 },
    )
    const result = JSON.parse(stdout.trim()) as Partial<NativeLabels>
    if (Array.isArray(result.efforts) && result.efforts.length === 5) {
      nativeLabels = {
        efforts: result.efforts.map(String),
        speedLabel: typeof result.speedLabel === 'string' ? result.speedLabel : '',
        speeds: Array.isArray(result.speeds) ? result.speeds.map(String).slice(0, 2) : [],
        speedIndex: Number.isInteger(result.speedIndex) ? Number(result.speedIndex) : -1,
      }
    }
  } catch {
    // Keep the bundled fallback if Codex is not ready yet.
  }
}

function defaultAnchor() {
  const { workArea } = screen.getPrimaryDisplay()
  return {
    x: workArea.x + Math.floor((workArea.width - CLOSED_SIZE.width) / 2),
    y: workArea.y + Math.floor((workArea.height - CLOSED_SIZE.height) / 2),
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
  if (!open) return { ...anchor, ...CLOSED_SIZE }
  return {
    x: anchor.x - (OPEN_SIZE.width - CLOSED_SIZE.width),
    y: anchor.y - (OPEN_SIZE.height - CLOSED_SIZE.height),
    ...OPEN_SIZE,
  }
}

function setPaletteOpen(open: boolean) {
  if (!mainWindow || mainWindow.isDestroyed()) return
  if (!paletteOpen) {
    const [x, y] = mainWindow.getPosition()
    anchor = { x, y }
    void saveAnchor()
  }
  paletteOpen = open
  mainWindow.setBounds(boundsFor(open), true)
}

function createWindow() {
  mainWindow = new BrowserWindow({
    ...boundsFor(false),
    transparent: true,
    frame: false,
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
      if (!line.trim() || !mainWindow || mainWindow.isDestroyed() || applying) continue
      try {
        const state = JSON.parse(line) as {
          visible: boolean
          selector?: { x: number; y: number; width: number; height: number } | null
        }
        if (state.selector) {
          selectorAnchor = {
            x: Math.round(state.selector.x + state.selector.width / 2 - 119),
            y: Math.round(state.selector.y + state.selector.height / 2 - 25),
          }
          if (!draggingWindow) {
            const offset = manualOffset ?? { x: 0, y: 0 }
            const nextAnchor = {
              x: selectorAnchor.x + offset.x,
              y: selectorAnchor.y + offset.y,
            }
            if (nextAnchor.x !== anchor.x || nextAnchor.y !== anchor.y) {
              anchor = nextAnchor
              mainWindow.setBounds(boundsFor(paletteOpen), false)
            }
          }
        }
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
ipcMain.handle('overlay:get-labels', async () => {
  await loadNativeLabels()
  return nativeLabels
})
ipcMain.handle('overlay:begin-drag', () => {
  draggingWindow = true
  if (!mainWindow || mainWindow.isDestroyed()) throw new Error('La surcouche n’est pas prête.')
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
  anchor = paletteOpen
    ? {
        x: x + (OPEN_SIZE.width - CLOSED_SIZE.width),
        y: y + (OPEN_SIZE.height - CLOSED_SIZE.height),
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
    throw new Error('La vitesse demandée est invalide.')
  }
  applying = true
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
    throw new Error(firstLine || 'Codex n’a pas confirmé la vitesse demandée.')
  } finally {
    applying = false
  }
})

ipcMain.handle(
  'overlay:apply',
  async (_event, selection: { modelIndex: number; effortIndex: number }) => {
    if (!mainWindow) throw new Error('La surcouche n’est pas prête.')
    applying = true
    // Keep the overlay visible while UI Automation runs. Native menus can open
    // underneath it, so the operation does not cause cursor or focus flicker.
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
      throw new Error(firstLine || 'Codex n’a pas confirmé la sélection demandée.')
    } finally {
      applying = false
    }
  },
)
ipcMain.handle('overlay:quit', () => app.quit())

app.whenReady().then(async () => {
  await loadAnchor()
  await loadNativeLabels()
  createWindow()
  startWatcher()
})

app.on('before-quit', () => watcher?.kill())
app.on('window-all-closed', () => app.quit())
