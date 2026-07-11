import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import type { CodexOverlayBridge } from './types'

const nativeEfforts = ['Ligero', 'Medio', 'Alto', 'Muy alto', 'Ultra']

const createBridge = (): CodexOverlayBridge => ({
  setOpen: vi.fn().mockResolvedValue(undefined),
  getLabels: vi.fn().mockResolvedValue({ efforts: nativeEfforts }),
  beginDrag: vi.fn().mockResolvedValue({ x: 0, y: 0 }),
  dragTo: vi.fn(),
  endDrag: vi.fn().mockResolvedValue(undefined),
  apply: vi.fn().mockResolvedValue({ ok: true }),
  resetPosition: vi.fn().mockResolvedValue(undefined),
  quit: vi.fn().mockResolvedValue(undefined),
})

describe('Codex Palette Overlay', () => {
  beforeEach(() => {
    window.codexOverlay = createBridge()
    localStorage.clear()
  })

  afterEach(() => {
    cleanup()
    delete window.codexOverlay
  })

  it('uses effort labels read from the native Codex selector', async () => {
    render(<App />)

    expect(await screen.findByText('Muy alto')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))

    for (const label of nativeEfforts) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
  })

  it('disables combinations that the native model does not support', async () => {
    render(<App />)

    expect(await screen.findByText('Muy alto')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))

    expect(screen.getByRole('button', { name: '5.6 Luna, Ultra' })).toBeDisabled()
  })

  it('collapses from the header button', async () => {
    render(<App />)

    expect(await screen.findByText('Muy alto')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Collapse palette' }))

    expect(screen.queryByRole('button', { name: 'Reset position' })).not.toBeInTheDocument()
    expect(window.codexOverlay?.setOpen).toHaveBeenLastCalledWith(false)
  })

  it('collapses after Codex confirms a selection', async () => {
    render(<App />)

    expect(await screen.findByText('Muy alto')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /5\.6 Sol/ }))
    fireEvent.click(screen.getByRole('button', { name: '5.6 Terra, Medio' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Reset position' })).not.toBeInTheDocument()
    })
    expect(window.codexOverlay?.apply).toHaveBeenCalledWith({ modelIndex: 1, effortIndex: 1 })
    expect(window.codexOverlay?.setOpen).toHaveBeenLastCalledWith(false)
  })
})
