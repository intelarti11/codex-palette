import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('codexOverlay', {
  setOpen: (open: boolean) => ipcRenderer.invoke('overlay:set-open', open),
  getLabels: () => ipcRenderer.invoke('overlay:get-labels'),
  beginDrag: () => ipcRenderer.invoke('overlay:begin-drag'),
  dragTo: (position: { x: number; y: number }) => ipcRenderer.send('overlay:drag-to', position),
  endDrag: () => ipcRenderer.invoke('overlay:end-drag'),
  apply: (selection: { modelIndex: number; effortIndex: number }) =>
    ipcRenderer.invoke('overlay:apply', selection),
  resetPosition: () => ipcRenderer.invoke('overlay:reset-position'),
  quit: () => ipcRenderer.invoke('overlay:quit'),
})
