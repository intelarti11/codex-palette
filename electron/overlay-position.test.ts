import { describe, expect, it } from 'vitest'
import { openBoundsForAnchor, selectorBoundsToAnchor, selectorBoundsToDipRectangle } from './overlay-position'

describe('overlay positioning', () => {
  it('centers the closed palette on the native selector', () => {
    expect(selectorBoundsToAnchor(
      { x: 1191, y: 981, width: 112, height: 28 },
      { width: 212, height: 50 },
      (point) => point,
    )).toEqual({ x: 1141, y: 970 })
  })

  it('converts physical UIA coordinates to Electron DIP coordinates', () => {
    expect(selectorBoundsToAnchor(
      { x: 1500, y: 900, width: 180, height: 42 },
      { width: 212, height: 50 },
      ({ x, y }) => ({ x: x / 1.5, y: y / 1.5 }),
    )).toEqual({ x: 954, y: 589 })
  })

  it('returns the exact dynamic selector size in Electron coordinates', () => {
    expect(selectorBoundsToDipRectangle(
      { x: 1752, y: 1473, width: 204, height: 42 },
      ({ x, y }) => ({ x: x / 1.5, y: y / 1.5 }),
    )).toEqual({ x: 1168, y: 982, width: 136, height: 28 })
  })

  it('keeps the expanded palette inside the display work area', () => {
    expect(openBoundsForAnchor(
      { x: 120, y: 80 },
      { width: 212, height: 50 },
      { width: 640, height: 360 },
      { x: 0, y: 0, width: 1920, height: 1040 },
    )).toEqual({ x: 0, y: 0, width: 640, height: 360 })
  })
})
