import {
  Check,
  ChevronDown,
  Flame,
  Gauge,
  Minus,
  Moon,
  RotateCcw,
  Sparkles,
  Sprout,
  Sun,
  Waves,
  X,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type CSSProperties, type MouseEvent, type PointerEvent } from 'react'
import type { NativeSelection, SelectorPresentation } from './types'

type Selection = { modelIndex: number; effortIndex: number }
type DragState = {
  pointerId: number
  startX: number
  startY: number
  latestX: number
  latestY: number
  origin: { x: number; y: number } | null
  dragging: boolean
}

const modelVisuals = [
  { icon: Sun, tone: 'solar' }, { icon: Sprout, tone: 'terra' },
  { icon: Moon, tone: 'luna' }, { icon: Waves, tone: 'ocean' },
  { icon: Flame, tone: 'ember' }, { icon: Sparkles, tone: 'rose' },
] as const
const storageKey = 'codex-palette-overlay.selection.v1'
const fallbackSelectorPresentation: SelectorPresentation = {
  width: 136,
  height: 28,
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

export const compactModelName = (name: string) => name.replace(/^GPT-/i, '')

const readSelection = (): Selection => {
  try {
    const value = JSON.parse(localStorage.getItem(storageKey) ?? '{}') as Partial<Selection>
    if (Number.isInteger(value.modelIndex) && Number.isInteger(value.effortIndex)) {
      return { modelIndex: value.modelIndex as number, effortIndex: value.effortIndex as number }
    }
  } catch {
    // Use the currently observed native default.
  }
  return { modelIndex: 0, effortIndex: 3 }
}

export default function App() {
  const [open, setOpen] = useState(false)
  const [selection, setSelection] = useState<Selection>(readSelection)
  const [busy, setBusy] = useState(false)
  const [activating, setActivating] = useState(false)
  const [activationError, setActivationError] = useState('')
  const [uiStrings, setUiStrings] = useState({ enableSilentMode: 'Restart required', restartingCodex: 'Restarting' })
  const [notice, setNotice] = useState<string | null>(null)
  const [modelNames, setModelNames] = useState<string[]>([])
  const [efforts, setEfforts] = useState<string[]>([])
  const [supportedEfforts, setSupportedEfforts] = useState<number[][]>([])
  const [speedLabel, setSpeedLabel] = useState('')
  const [speeds, setSpeeds] = useState<string[]>([])
  const [speedIndex, setSpeedIndex] = useState(-1)
  const [selectorPresentation, setSelectorPresentation] = useState(fallbackSelectorPresentation)
  const dragState = useRef<DragState | null>(null)
  const dragFrame = useRef<number | null>(null)
  const lastDragEnd = useRef(0)

  const acceptNativeSelection = useCallback((current: NativeSelection) => {
    if (
      !Number.isInteger(current.modelIndex)
      || !Number.isInteger(current.effortIndex)
      || current.modelIndex < 0
      || current.effortIndex < 0
    ) return
    const next = { modelIndex: current.modelIndex, effortIndex: current.effortIndex }
    setSelection(next)
    localStorage.setItem(storageKey, JSON.stringify(next))
    if (current.speedIndex >= 0) setSpeedIndex(current.speedIndex)
  }, [])

  useEffect(() => {
    let active = true
    let retryTimer: number | null = null

    const refreshLabels = async () => {
      const labels = await window.codexOverlay?.getLabels()
      if (!active || !labels) return
      if (labels.uiStrings) setUiStrings(labels.uiStrings)
      if (labels.selectorPresentation) setSelectorPresentation(labels.selectorPresentation)
      if (labels.currentSelection) acceptNativeSelection(labels.currentSelection)
      const hasSelectorLabels = labels.models.length > 0 && labels.efforts.length > 0
      if (hasSelectorLabels) {
        setModelNames(labels.models)
        setEfforts(labels.efforts)
        setSupportedEfforts(labels.supportedEfforts)
      }
      if (labels.speeds.length === 2) {
        setSpeedLabel(labels.speedLabel)
        setSpeeds(labels.speeds)
        setSpeedIndex(labels.speedIndex)
      }
      if (!hasSelectorLabels || labels.complete === false) {
        retryTimer = window.setTimeout(() => void refreshLabels(), 1000)
      }
    }

    void refreshLabels()
    return () => {
      active = false
      if (retryTimer !== null) window.clearTimeout(retryTimer)
    }
  }, [])

  const moveWindow = useCallback(() => {
    dragFrame.current = null
    const state = dragState.current
    if (!state?.dragging || !state.origin) return
    window.codexOverlay?.dragTo({
      x: state.origin.x + state.latestX - state.startX,
      y: state.origin.y + state.latestY - state.startY,
    })
  }, [])

  const startWindowDrag = useCallback((event: PointerEvent<HTMLElement>) => {
    if (event.button !== 0 || !window.codexOverlay || (event.target as HTMLElement).closest('button')) return
    const state: DragState = {
      pointerId: event.pointerId,
      startX: event.screenX,
      startY: event.screenY,
      latestX: event.screenX,
      latestY: event.screenY,
      origin: null,
      dragging: false,
    }
    dragState.current = state
    void window.codexOverlay.beginDrag().then((origin) => {
      if (dragState.current !== state) return
      state.origin = origin
      if (state.dragging && dragFrame.current === null) {
        dragFrame.current = requestAnimationFrame(moveWindow)
      }
    })
  }, [moveWindow])

  const continueWindowDrag = useCallback((event: PointerEvent<HTMLElement>) => {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId) return
    state.latestX = event.screenX
    state.latestY = event.screenY
    if (!state.dragging && Math.hypot(state.latestX - state.startX, state.latestY - state.startY) >= 5) {
      state.dragging = true
      event.currentTarget.setPointerCapture(event.pointerId)
    }
    if (state.dragging && state.origin && dragFrame.current === null) {
      dragFrame.current = requestAnimationFrame(moveWindow)
    }
  }, [moveWindow])

  const finishWindowDrag = useCallback((event: PointerEvent<HTMLElement>) => {
    const state = dragState.current
    if (!state || state.pointerId !== event.pointerId) return
    if (dragFrame.current !== null) {
      cancelAnimationFrame(dragFrame.current)
      dragFrame.current = null
      moveWindow()
    }
    if (state.dragging) lastDragEnd.current = performance.now()
    dragState.current = null
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId)
    }
    void window.codexOverlay?.endDrag()
  }, [moveWindow])

  const suppressClickAfterDrag = useCallback((event: MouseEvent<HTMLElement>) => {
    if (performance.now() - lastDragEnd.current >= 250) return
    event.preventDefault()
    event.stopPropagation()
  }, [])

  const dragHandlers = {
    onPointerDownCapture: startWindowDrag,
    onPointerMoveCapture: continueWindowDrag,
    onPointerUpCapture: finishWindowDrag,
    onPointerCancelCapture: finishWindowDrag,
    onClickCapture: suppressClickAfterDrag,
  }

  const showContextMenu = useCallback((event: MouseEvent<HTMLElement>) => {
    event.preventDefault()
    window.codexOverlay?.showContextMenu()
  }, [acceptNativeSelection])

  const collapse = useCallback(async () => {
    setOpen(false)
    await window.codexOverlay?.setOpen(false)
  }, [])

  const toggle = useCallback(async () => {
    const next = !open
    setOpen(next)
    await window.codexOverlay?.setOpen(next)
  }, [open])

  const apply = useCallback(async (next: Selection) => {
    setBusy(true)
    setNotice(null)
    try {
      const result = await window.codexOverlay?.apply(next)
      setSelection(next)
      localStorage.setItem(storageKey, JSON.stringify(next))
      const mode = result?.inputMode === 'cdp-internal' ? ' · Direct' : ''
      setNotice(`${compactModelName(modelNames[next.modelIndex] ?? '')} · ${efforts[next.effortIndex]}${mode}`)
      await collapse()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The native selector did not respond.')
    } finally {
      setBusy(false)
    }
  }, [collapse, efforts, modelNames])

  const applySpeed = useCallback(async (nextSpeedIndex: number) => {
    if (nextSpeedIndex === speedIndex) return
    setBusy(true)
    setNotice(null)
    try {
      await window.codexOverlay?.applySpeed(nextSpeedIndex)
      setSpeedIndex(nextSpeedIndex)
      setNotice(speeds[nextSpeedIndex] ?? null)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The native selector did not respond.')
    } finally {
      setBusy(false)
    }
  }, [speedIndex, speeds])

  const resetPosition = useCallback(async () => {
    await window.codexOverlay?.resetPosition()
    setNotice(null)
  }, [])

  useEffect(() => window.codexOverlay?.onSelectorPresentation(setSelectorPresentation), [])
  useEffect(() => window.codexOverlay?.onSelectionChanged(acceptNativeSelection), [acceptNativeSelection])
  useEffect(() => window.codexOverlay?.onOpenChanged(setOpen), [])

  const enableSilentMode = useCallback(async () => {
    setActivating(true)
    setActivationError('')
    try {
      await window.codexOverlay?.enableSilentMode()
    } catch (error) {
      setActivationError(error instanceof Error ? error.message : 'Could not restart Codex.')
      setActivating(false)
    }
  }, [])

  const selectedModelName = modelNames[selection.modelIndex] ?? ''
  const selectedVisual = modelVisuals[selection.modelIndex] ?? modelVisuals[0]
  const selectedToneClass = selectedModelName ? ` has-model-tone tone-${selectedVisual.tone}` : ''
  const displaySelectedModelName = compactModelName(selectedModelName)
  const selectedEffort = efforts[selection.effortIndex] ?? ''
  const activateClosedTriggerFromPointer = useCallback((event: PointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) return
    event.preventDefault()
    if (selectedModelName) void toggle()
    else void enableSilentMode()
  }, [enableSilentMode, selectedModelName, toggle])
  const activateClosedTriggerFromKeyboard = useCallback((event: MouseEvent<HTMLButtonElement>) => {
    if (event.detail !== 0) return
    if (selectedModelName) void toggle()
    else void enableSilentMode()
  }, [enableSilentMode, selectedModelName, toggle])
  const selectorStyle = {
    '--selector-width': `${selectorPresentation.width}px`,
    '--selector-height': `${selectorPresentation.height}px`,
    '--native-padding-left': selectorPresentation.paddingLeft,
    '--native-padding-right': selectorPresentation.paddingRight,
    '--native-gap': selectorPresentation.gap,
    '--native-border': selectorPresentation.border,
    '--native-radius': selectorPresentation.borderRadius,
    '--native-background': selectorPresentation.backgroundColor,
    '--native-hover-background': selectorPresentation.hoverBackgroundColor,
    '--native-color': selectorPresentation.color,
    '--native-model-color': selectorPresentation.modelColor,
    '--native-font-family': selectorPresentation.fontFamily,
    '--native-font-size': selectorPresentation.fontSize,
    '--native-font-weight': selectorPresentation.fontWeight,
    '--native-line-height': selectorPresentation.lineHeight,
    '--native-shadow': selectorPresentation.boxShadow,
  } as CSSProperties

  if (!open) {
    return (
      <main className="overlay-closed" style={selectorStyle} onContextMenu={showContextMenu}>
        <button
          className={`native-trigger${selectedToneClass}`}
          type="button"
          onPointerDown={activateClosedTriggerFromPointer}
          onClick={activateClosedTriggerFromKeyboard}
          disabled={activating}
          aria-label={displaySelectedModelName
            ? `${displaySelectedModelName} ${selectedEffort}`
            : activating ? `${uiStrings.restartingCodex}…` : uiStrings.enableSilentMode}
        >
          {!selectedModelName ? (
            <>
              <span className="native-model">
                {activating ? `${uiStrings.restartingCodex}…` : uiStrings.enableSilentMode}
              </span>
              {activationError ? <span className="native-effort">{activationError}</span> : null}
              <ChevronDown aria-hidden="true" size={selectorPresentation.iconSize} />
            </>
          ) : null}
        </button>
      </main>
    )
  }

  return (
    <main className="overlay-open" style={selectorStyle} onContextMenu={showContextMenu}>
      <section className="palette-card" aria-label="Codex model palette">
        <header className="palette-header" {...dragHandlers}>
          <div className="header-actions">
            <button type="button" onClick={() => void resetPosition()} aria-label="Reset position">
              <RotateCcw size={14} />
            </button>
            <button type="button" onClick={() => void collapse()} aria-label="Collapse palette">
              <Minus size={15} />
            </button>
            <button type="button" onClick={() => void window.codexOverlay?.quit()} aria-label="Close overlay">
              <X size={15} />
            </button>
          </div>
        </header>

        <div className="matrix">
          <div className="matrix-corner" />
          {modelNames.map((name, modelIndex) => {
            const { icon: Icon, tone } = modelVisuals[modelIndex] ?? modelVisuals[0]
            return (
            <div className={`model-heading tone-${tone}`} key={name}>
              <Icon size={16} />
              <span>{compactModelName(name)}</span>
            </div>
          )})}

          {efforts.map((effort, effortIndex) => (
            <div className="matrix-row" key={effort}>
              <div className="effort-label">{effort}</div>
              {modelNames.map((modelName, modelIndex) => {
                const visual = modelVisuals[modelIndex] ?? modelVisuals[0]
                const supported = supportedEfforts[modelIndex]?.includes(effortIndex) ?? true
                const selected = selection.modelIndex === modelIndex && selection.effortIndex === effortIndex
                return (
                  <button
                    className={`effort-cell tone-${visual.tone} ${selected ? 'selected' : ''}`}
                    type="button"
                    key={`${modelName}-${effort}`}
                    disabled={!supported || busy}
                    aria-label={`${compactModelName(modelName)}, ${effort}`}
                    aria-pressed={selected}
                    style={{ '--level': effortIndex + 1 } as React.CSSProperties}
                    onClick={() => void apply({ modelIndex, effortIndex })}
                  >
                    {selected ? <Check size={17} /> : null}
                  </button>
                )
              })}
            </div>
          ))}
        </div>

        <footer className="palette-footer">
          {speeds.length === 2 ? (
            <div className="speed-control">
              <span className="speed-title">
                <Gauge size={13} />
                {speedLabel ? <span>{speedLabel}</span> : null}
              </span>
              <div className="speed-segments" role="group" aria-label={speedLabel || speeds.join(' / ')}>
                {speeds.map((speed, index) => (
                  <button
                    type="button"
                    key={speed}
                    className={speedIndex === index ? 'active' : ''}
                    aria-pressed={speedIndex === index}
                    disabled={busy}
                    onClick={() => void applySpeed(index)}
                  >
                    {speed}
                  </button>
                ))}
              </div>
            </div>
          ) : <span />}
          {notice ? <span className="notice">{notice}</span> : null}
        </footer>
      </section>

      <div className="open-trigger-row">
        <button
          className={`native-trigger${selectedToneClass}`}
          type="button"
          aria-label={`${displaySelectedModelName} ${selectedEffort}`}
          onClick={() => void toggle()}
        >
        </button>
      </div>
    </main>
  )
}
