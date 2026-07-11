import { contextBridge, ipcRenderer } from 'electron'

const bridge = {
  start: () => ipcRenderer.invoke('codex:start'),
  stop: () => ipcRenderer.invoke('codex:stop'),
  send: (payload: unknown) => ipcRenderer.invoke('codex:send', payload),
  version: () => ipcRenderer.invoke('codex:version'),
  selectProject: () => ipcRenderer.invoke('project:select'),
  openExternal: (url: string) => ipcRenderer.invoke('external:open', url),
  onMessage: (callback: (line: string) => void) => {
    const listener = (_event: Electron.IpcRendererEvent, line: string) => callback(line)
    ipcRenderer.on('codex:message', listener)
    return () => ipcRenderer.removeListener('codex:message', listener)
  },
  onLog: (callback: (line: string) => void) => {
    const listener = (_event: Electron.IpcRendererEvent, line: string) => callback(line)
    ipcRenderer.on('codex:log', listener)
    return () => ipcRenderer.removeListener('codex:log', listener)
  },
  onStatus: (callback: (status: unknown) => void) => {
    const listener = (_event: Electron.IpcRendererEvent, status: unknown) => callback(status)
    ipcRenderer.on('codex:status', listener)
    return () => ipcRenderer.removeListener('codex:status', listener)
  },
}

contextBridge.exposeInMainWorld('codexPalette', bridge)
