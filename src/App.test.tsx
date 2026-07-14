import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import type { CodexOverlayBridge } from './types'

const nativeEfforts = ['Ligero', 'Medio', 'Alto', 'Muy alto', 'Ultra']
const nativeModels = ['5.6 Sol', '5.6 Terra', '5.6 Luna', '5.5', '5.4', '5.4 Mini']
const nativeSpeeds = ['Estándar', 'Rápido']

const createBridge = (): CodexOverlayBridge => ({
  setOpen: vi.fn().mockResolvedValue(undefined),
  showContextMenu: vi.fn(),
  enableSilentMode: vi.fn().mockResolvedValue({ ok: true, port: 45000 }),
  getLabels: vi.fn().mockResolvedValue({
    models: nativeModels,
    efforts: nativeEfforts,
    supportedEfforts: [[0, 1, 2, 3, 4], [0, 1, 2, 3, 4], [0, 1, 2, 3], [0, 1, 2, 3], [0, 1, 2, 3], [0, 1, 2, 3]],
    speedLabel: 'Velocidad',
    speeds: nativeSpeeds,
    speedIndex: 0,
  }),
  onSelectorPresentation: vi.fn().mockReturnValue(() => undefined),
  onSelectionChanged: vi.fn().mockReturnValue(() => undefined),
  onOpenChanged: vi.fn().mockReturnValue(() => undefined),
  beginDrag: vi.fn().mockResolvedValue({ x: 0, y: 0 }),
  dragTo: vi.fn(),
  endDrag: vi.fn().mockResolvedValue(undefined),
  apply: vi.fn().mockResolvedValue({ ok: true, inputMode: 'cdp-internal' }),
  applySpeed: vi.fn().mockResolvedValue({ ok: true, speedIndex: 1, speed: 'Rápido' }),
  resetPosition: vi.fn().mockResolvedValue(undefined),
  quit: vi.fn().mockResolvedValue(undefined),
})

describe('Codex Palette Overlay', () => {
  beforeEach(() => {
    window.codexOverlay = createBridge()
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
    cleanup()
    delete window.codexOverlay
  })

  it('retries while native selector labels are still loading', async () => {
    vi.useFakeTimers()
    const bridge = createBridge()
    vi.mocked(bridge.getLabels)
      .mockResolvedValueOnce({
        models: [], efforts: [], supportedEfforts: [],
        speedLabel: '', speeds: [], speedIndex: -1,
      })
      .mockResolvedValue({
        models: nativeModels,
        efforts: nativeEfforts,
        supportedEfforts: [[0, 1, 2, 3, 4], [0, 1, 2, 3, 4], [0, 1, 2, 3], [0, 1, 2, 3], [0, 1, 2, 3], [0, 1, 2, 3]],
        speedLabel: '', speeds: [], speedIndex: -1,
      })
    window.codexOverlay = bridge

    render(<App />)
    await act(async () => undefined)
    expect(screen.getByRole('button', { name: 'Restart required' })).toBeEnabled()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000)
    })
    expect(screen.getByRole('button', { name: /5\.6 Sol/ })).toBeEnabled()
    expect(bridge.getLabels).toHaveBeenCalledTimes(2)
  })

  it('offers to restart Codex when the silent harness is unavailable', async () => {
    const bridge = createBridge()
    vi.mocked(bridge.getLabels).mockResolvedValue({
      models: [], efforts: [], supportedEfforts: [], speedLabel: '', speeds: [], speedIndex: -1,
    })
    window.codexOverlay = bridge
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: 'Restart required' }))

    expect(bridge.enableSilentMode).toHaveBeenCalledOnce()
    expect(await screen.findByRole('button', { name: 'Restarting…' })).toBeDisabled()
  })

  it('uses effort labels read from the native Codex selector', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))

    for (const label of nativeEfforts) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
  })

  it('disables combinations that the native model does not support', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))

    expect(screen.getByRole('button', { name: '5.6 Luna, Ultra' })).toBeDisabled()
  })

  it('collapses from the header button', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Collapse palette' }))

    expect(screen.queryByRole('button', { name: 'Reset position' })).not.toBeInTheDocument()
    expect(window.codexOverlay?.setOpen).toHaveBeenLastCalledWith(false)
  })

  it('collapses after Codex confirms a selection', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))
    fireEvent.click(screen.getByRole('button', { name: '5.6 Terra, Medio' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Reset position' })).not.toBeInTheDocument()
    })
    expect(window.codexOverlay?.apply).toHaveBeenCalledWith({ modelIndex: 1, effortIndex: 1 })
    expect(window.codexOverlay?.setOpen).toHaveBeenLastCalledWith(false)
  })

  it('opens immediately on primary pointer down while the selector is closed', async () => {
    render(<App />)

    const trigger = await screen.findByRole('button', { name: /5\.6 Sol/ })
    fireEvent.pointerDown(trigger, { button: 0, pointerId: 1 })

    expect(window.codexOverlay?.setOpen).toHaveBeenLastCalledWith(true)
    expect(screen.getByRole('button', { name: 'Collapse palette' })).toBeInTheDocument()
  })

  it('uses the same compact model name as the native selector', async () => {
    const bridge = createBridge()
    vi.mocked(bridge.getLabels).mockResolvedValue({
      models: ['GPT-5.6 Sol'],
      efforts: nativeEfforts,
      supportedEfforts: [[0, 1, 2, 3, 4]],
      speedLabel: '', speeds: [], speedIndex: -1,
    })
    window.codexOverlay = bridge

    render(<App />)

    expect(await screen.findByRole('button', { name: /^5\.6 Sol/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^GPT-/ })).not.toBeInTheDocument()
  })

  it('keeps the model version in the visible palette headings', async () => {
    render(<App />)

    fireEvent.click(await screen.findByRole('button', { name: /5\.6 Sol/ }))

    expect(screen.getByText('5.6 Sol')).toBeInTheDocument()
    expect(screen.getByText('5.6 Terra')).toBeInTheDocument()
    expect(screen.getByText('5.6 Luna')).toBeInTheDocument()
    expect(screen.getByText('5.4 Mini')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '5.6 Terra, Medio' })).toBeEnabled()
  })

  it('follows the active conversation selection and updates the selector tone', async () => {
    const bridge = createBridge()
    let publishSelection: Parameters<CodexOverlayBridge['onSelectionChanged']>[0] | undefined
    vi.mocked(bridge.onSelectionChanged).mockImplementation((listener) => {
      publishSelection = listener
      return () => undefined
    })
    window.codexOverlay = bridge
    render(<App />)

    const initialTrigger = await screen.findByRole('button', { name: /5\.6 Sol/ })
    expect(initialTrigger).toHaveClass('has-model-tone', 'tone-solar')

    act(() => publishSelection?.({ modelIndex: 1, effortIndex: 1, speedIndex: 1 }))

    const updatedTrigger = screen.getByRole('button', { name: /5\.6 Terra Medio/ })
    expect(updatedTrigger).toHaveClass('has-model-tone', 'tone-terra')
    fireEvent.click(updatedTrigger)
    expect(screen.getByRole('button', { name: '5.6 Terra, Medio' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: nativeSpeeds[1] })).toHaveAttribute('aria-pressed', 'true')
  })

  it('follows palette open state changes triggered by the main-process shortcut', async () => {
    const bridge = createBridge()
    let publishOpen: Parameters<CodexOverlayBridge['onOpenChanged']>[0] | undefined
    vi.mocked(bridge.onOpenChanged).mockImplementation((listener) => {
      publishOpen = listener
      return () => undefined
    })
    window.codexOverlay = bridge
    render(<App />)

    await screen.findByRole('button', { name: /5\.6 Sol/ })
    act(() => publishOpen?.(true))
    expect(screen.getByRole('button', { name: 'Collapse palette' })).toBeInTheDocument()

    act(() => publishOpen?.(false))
    expect(screen.queryByRole('button', { name: 'Collapse palette' })).not.toBeInTheDocument()
  })

  it('opens the close menu on right click', async () => {
    render(<App />)
    await screen.findByRole('button', { name: /5\.6 Sol/ })

    fireEvent.contextMenu(screen.getByRole('main'))

    expect(window.codexOverlay?.showContextMenu).toHaveBeenCalledOnce()
  })

  it('marks a renderer-internal selection as direct', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))
    fireEvent.click(screen.getByRole('button', { name: '5.6 Terra, Medio' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Reset position' })).not.toBeInTheDocument()
    })
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Terra/ }))
    expect(await screen.findByText(/Direct/)).toBeInTheDocument()
  })

  it('uses and applies the two native localized speed values', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: /5\.6 Sol/ })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))

    expect(screen.getByText('Velocidad')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Estándar' })).toHaveAttribute('aria-pressed', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Rápido' }))

    await waitFor(() => {
      expect(window.codexOverlay?.applySpeed).toHaveBeenCalledWith(1)
    })
    expect(screen.getByRole('button', { name: 'Rápido' })).toHaveAttribute('aria-pressed', 'true')
  })
})
