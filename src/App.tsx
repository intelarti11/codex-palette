import {
  Check,
  ChevronDown,
  Flame,
  GripVertical,
  Minus,
  Moon,
  RotateCcw,
  Sparkles,
  Sprout,
  Sun,
  Waves,
  X,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type MouseEvent, type PointerEvent } from 'react'

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

const models = [
  { name: '5.6 Sol', icon: Sun, tone: 'solar', efforts: [0, 1, 2, 3, 4] },
  { name: '5.6 Terra', icon: Sprout, tone: 'terra', efforts: [0, 1, 2, 3, 4] },
  { name: '5.6 Luna', icon: Moon, tone: 'luna', efforts: [0, 1, 2, 3] },
  { name: '5.5', icon: Waves, tone: 'ocean', efforts: [0, 1, 2, 3] },
  { name: '5.4', icon: Flame, tone: 'ember', efforts: [0, 1, 2, 3] },
  { name: '5.4 Mini', icon: Sparkles, tone: 'rose', efforts: [0, 1, 2, 3] },
] as const

const fallbackEfforts = ['Léger', 'Moyen', 'Élevé', 'Très élevé', 'Ultra']
const storageKey = 'codex-palette-overlay.selection.v1'

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
  const [notice, setNotice] = useState<string | null>(null)
  const [efforts, setEfforts] = useState<string[]>(fallbackEfforts)
  const dragState = useRef<DragState | null>(null)
  const dragFrame = useRef<number | null>(null)
  const lastDragEnd = useRef(0)

  useEffect(() => {
    let active = true
    void window.codexOverlay?.getLabels().then((labels) => {
      if (active && labels.efforts.length === 5) setEfforts(labels.efforts)
    })
    return () => {
      active = false
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
    if (event.button !== 0 || !window.codexOverlay) return
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
      await window.codexOverlay?.apply(next)
      setSelection(next)
      localStorage.setItem(storageKey, JSON.stringify(next))
      setNotice(`${models[next.modelIndex].name} · ${efforts[next.effortIndex]}`)
      await collapse()
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Le sélecteur natif n’a pas répondu.')
    } finally {
      setBusy(false)
    }
  }, [collapse, efforts])

  const resetPosition = useCallback(async () => {
    await window.codexOverlay?.resetPosition()
    setNotice(null)
  }, [])

  const selectedModel = models[selection.modelIndex] ?? models[0]

  if (!open) {
    return (
      <main className="overlay-closed" {...dragHandlers}>
        <div className="drag-handle">
          <GripVertical size={15} />
        </div>
        <button className={`native-trigger tone-${selectedModel.tone}`} type="button" onClick={() => void toggle()}>
          <strong>{selectedModel.name}</strong>
          <span>{efforts[selection.effortIndex]}</span>
          <ChevronDown size={15} />
        </button>
      </main>
    )
  }

  return (
    <main className="overlay-open" {...dragHandlers}>
      <section className="palette-card" aria-label="Codex model palette">
        <header className="palette-header">
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
          {models.map(({ name, icon: Icon, tone }) => (
            <div className={`model-heading tone-${tone}`} key={name}>
              <Icon size={16} />
              <span>{name}</span>
            </div>
          ))}

          {efforts.map((effort, effortIndex) => (
            <div className="matrix-row" key={effort}>
              <div className="effort-label">{effort}</div>
              {models.map((model, modelIndex) => {
                const supported = model.efforts.includes(effortIndex as never)
                const selected = selection.modelIndex === modelIndex && selection.effortIndex === effortIndex
                return (
                  <button
                    className={`effort-cell tone-${model.tone} ${selected ? 'selected' : ''}`}
                    type="button"
                    key={`${model.name}-${effort}`}
                    disabled={!supported || busy}
                    aria-label={`${model.name}, ${effort}`}
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
          {notice ? <span className="notice">{notice}</span> : null}
        </footer>
      </section>

      <div className="open-trigger-row">
        <div className="drag-handle open-grip">
          <GripVertical size={15} />
        </div>
        <button className={`native-trigger tone-${selectedModel.tone}`} type="button" onClick={() => void toggle()}>
          <strong>{selectedModel.name}</strong>
          <span>{efforts[selection.effortIndex]}</span>
          <ChevronDown size={15} />
        </button>
      </div>
    </main>
  )
}
