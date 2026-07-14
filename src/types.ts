export interface SelectorPresentation {
  width: number
  height: number
  paddingLeft: string
  paddingRight: string
  gap: string
  border: string
  borderRadius: string
  backgroundColor: string
  hoverBackgroundColor: string
  color: string
  modelColor: string
  fontFamily: string
  fontSize: string
  fontWeight: string
  lineHeight: string
  boxShadow: string
  iconSize: number
}

export interface NativeSelection {
  modelIndex: number
  effortIndex: number
  speedIndex: number
}

export interface NativeLabels {
  models: string[]
  efforts: string[]
  supportedEfforts: number[][]
  speedLabel: string
  speeds: string[]
  speedIndex: number
  currentSelection?: NativeSelection | null
  complete?: boolean
  uiStrings?: { enableSilentMode: string; restartingCodex: string }
  selectorPresentation?: SelectorPresentation
}

export interface OverlaySelection {
  modelIndex: number
  effortIndex: number
}

export interface CodexOverlayBridge {
  setOpen: (open: boolean) => Promise<void>
  showContextMenu: () => void
  enableSilentMode: () => Promise<{ ok: boolean; port?: number }>
  getLabels: () => Promise<NativeLabels>
  onSelectorPresentation: (listener: (presentation: SelectorPresentation) => void) => () => void
  onSelectionChanged: (listener: (selection: NativeSelection) => void) => () => void
  onOpenChanged: (listener: (open: boolean) => void) => () => void
  beginDrag: () => Promise<{ x: number; y: number }>
  dragTo: (position: { x: number; y: number }) => void
  endDrag: () => Promise<void>
  apply: (selection: OverlaySelection) => Promise<{
    ok: boolean
    selection?: string
    inputMode?: 'cdp-internal' | 'uia-silent'
  }>
  applySpeed: (speedIndex: number) => Promise<{ ok: boolean; speedIndex?: number; speed?: string }>
  resetPosition: () => Promise<void>
  quit: () => Promise<void>
}

declare global {
  interface Window {
    codexOverlay?: CodexOverlayBridge
  }
}
