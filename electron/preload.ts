import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('codexOverlay', {
  setOpen: (open: boolean) => ipcRenderer.invoke('overlay:set-open', open),
  showContextMenu: () => ipcRenderer.send('overlay:show-context-menu'),
  enableSilentMode: () => ipcRenderer.invoke('overlay:enable-silent-mode'),
  getLabels: () => ipcRenderer.invoke('overlay:get-labels'),
  onSelectorPresentation: (listener: (presentation: unknown) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, presentation: unknown) => listener(presentation)
    ipcRenderer.on('overlay:selector-presentation', handler)
    return () => ipcRenderer.removeListener('overlay:selector-presentation', handler)
  },
  onSelectionChanged: (listener: (selection: unknown) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, selection: unknown) => listener(selection)
    ipcRenderer.on('overlay:selection-changed', handler)
    return () => ipcRenderer.removeListener('overlay:selection-changed', handler)
  },
  onOpenChanged: (listener: (open: boolean) => void) => {
    const handler = (_event: Electron.IpcRendererEvent, open: boolean) => listener(open)
    ipcRenderer.on('overlay:open-changed', handler)
    return () => ipcRenderer.removeListener('overlay:open-changed', handler)
  },
  beginDrag: () => ipcRenderer.invoke('overlay:begin-drag'),
  dragTo: (position: { x: number; y: number }) => ipcRenderer.send('overlay:drag-to', position),
  endDrag: () => ipcRenderer.invoke('overlay:end-drag'),
  apply: (selection: { modelIndex: number; effortIndex: number }) =>
    ipcRenderer.invoke('overlay:apply', selection),
  applySpeed: (speedIndex: number) => ipcRenderer.invoke('overlay:apply-speed', speedIndex),
  resetPosition: () => ipcRenderer.invoke('overlay:reset-position'),
  quit: () => ipcRenderer.invoke('overlay:quit'),
})
