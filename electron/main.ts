import { app, BrowserWindow, dialog, ipcMain, shell, type OpenDialogOptions } from 'electron'
import { spawn, execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { ChildProcessWithoutNullStreams } from 'node:child_process'

const execFileAsync = promisify(execFile)
const __dirname = dirname(fileURLToPath(import.meta.url))
const APP_ROOT = join(__dirname, '..')

let mainWindow: BrowserWindow | null = null
let codexProcess: ChildProcessWithoutNullStreams | null = null
let stdoutBuffer = ''

function emit(channel: string, payload: unknown) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send(channel, payload)
  }
}

async function getCodexVersion(): Promise<string> {
  const { stdout } = await execFileAsync('codex', ['--version'], {
    windowsHide: true,
  })
  return stdout.trim()
}

async function startCodexServer() {
  if (codexProcess) {
    return { running: true, version: await getCodexVersion() }
  }

  const version = await getCodexVersion()
  stdoutBuffer = ''

  codexProcess = spawn('codex', ['app-server', '--listen', 'stdio://'], {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  })

  codexProcess.stdout.setEncoding('utf8')
  codexProcess.stderr.setEncoding('utf8')

  codexProcess.stdout.on('data', (chunk: string) => {
    stdoutBuffer += chunk
    const lines = stdoutBuffer.split(/\r?\n/)
    stdoutBuffer = lines.pop() ?? ''

    for (const line of lines) {
      const trimmed = line.trim()
      if (trimmed) emit('codex:message', trimmed)
    }
  })

  codexProcess.stderr.on('data', (chunk: string) => {
    const text = chunk.trim()
    if (text) emit('codex:log', text)
  })

  codexProcess.on('error', (error) => {
    emit('codex:status', { running: false, error: error.message })
    codexProcess = null
  })

  codexProcess.on('exit', (code, signal) => {
    emit('codex:status', { running: false, code, signal })
    codexProcess = null
  })

  emit('codex:status', { running: true, version })
  return { running: true, version }
}

function stopCodexServer() {
  if (!codexProcess) return { running: false }
  codexProcess.kill()
  codexProcess = null
  return { running: false }
}

function sendRpc(payload: unknown) {
  if (!codexProcess || codexProcess.killed) {
    throw new Error('Codex App Server is not running.')
  }

  codexProcess.stdin.write(`${JSON.stringify(payload)}\n`)
  return { sent: true }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1420,
    height: 960,
    minWidth: 980,
    minHeight: 700,
    title: 'Codex Palette',
    backgroundColor: '#f8f8f6',
    show: false,
    webPreferences: {
      preload: join(__dirname, 'preload.mjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  mainWindow.once('ready-to-show', () => mainWindow?.show())

  if (process.env.VITE_DEV_SERVER_URL) {
    void mainWindow.loadURL(process.env.VITE_DEV_SERVER_URL)
  } else {
    void mainWindow.loadFile(join(APP_ROOT, 'dist', 'index.html'))
  }
}

ipcMain.handle('codex:start', startCodexServer)
ipcMain.handle('codex:stop', stopCodexServer)
ipcMain.handle('codex:send', (_event, payload) => sendRpc(payload))
ipcMain.handle('codex:version', getCodexVersion)
ipcMain.handle('project:select', async () => {
  const options: OpenDialogOptions = {
    title: 'Choose a project folder',
    properties: ['openDirectory', 'createDirectory'],
  }
  const result = mainWindow
    ? await dialog.showOpenDialog(mainWindow, options)
    : await dialog.showOpenDialog(options)
  return result.canceled ? null : result.filePaths[0]
})
ipcMain.handle('external:open', (_event, url: string) => shell.openExternal(url))

app.whenReady().then(() => {
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  stopCodexServer()
  if (process.platform !== 'darwin') app.quit()
})
