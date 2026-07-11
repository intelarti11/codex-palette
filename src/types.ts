export interface NativeLabels {
  efforts: string[]
}

export interface OverlaySelection {
  modelIndex: number
  effortIndex: number
}

export interface CodexOverlayBridge {
  setOpen: (open: boolean) => Promise<void>
  getLabels: () => Promise<NativeLabels>
  beginDrag: () => Promise<{ x: number; y: number }>
  dragTo: (position: { x: number; y: number }) => void
  endDrag: () => Promise<void>
  apply: (selection: OverlaySelection) => Promise<{ ok: boolean; selection?: string }>
  resetPosition: () => Promise<void>
  quit: () => Promise<void>
}

declare global {
  interface Window {
    codexOverlay?: CodexOverlayBridge
  }
}
