export type OverlayPoint = { x: number; y: number }
export type OverlayRectangle = OverlayPoint & { width: number; height: number }

export function selectorBoundsToDipRectangle(
  selector: OverlayRectangle,
  toDipPoint: (point: OverlayPoint) => OverlayPoint,
): OverlayRectangle {
  const topLeft = toDipPoint({ x: selector.x, y: selector.y })
  const bottomRight = toDipPoint({
    x: selector.x + selector.width,
    y: selector.y + selector.height,
  })
  return {
    x: Math.round(topLeft.x),
    y: Math.round(topLeft.y),
    width: Math.max(1, Math.round(bottomRight.x - topLeft.x)),
    height: Math.max(1, Math.round(bottomRight.y - topLeft.y)),
  }
}

export function selectorBoundsToAnchor(
  selector: OverlayRectangle,
  closedSize: { width: number; height: number },
  toDipPoint: (point: OverlayPoint) => OverlayPoint,
): OverlayPoint {
  const bounds = selectorBoundsToDipRectangle(selector, toDipPoint)
  return {
    x: Math.round(bounds.x + (bounds.width - closedSize.width) / 2),
    y: Math.round(bounds.y + (bounds.height - closedSize.height) / 2),
  }
}

export function openBoundsForAnchor(
  anchor: OverlayPoint,
  closedSize: { width: number; height: number },
  openSize: { width: number; height: number },
  workArea: OverlayRectangle,
): OverlayRectangle {
  const x = anchor.x - (openSize.width - closedSize.width)
  const y = anchor.y - (openSize.height - closedSize.height)
  return {
    x: Math.min(Math.max(x, workArea.x), workArea.x + workArea.width - openSize.width),
    y: Math.min(Math.max(y, workArea.y), workArea.y + workArea.height - openSize.height),
    ...openSize,
  }
}
