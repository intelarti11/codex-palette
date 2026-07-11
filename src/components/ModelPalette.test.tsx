import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { demoModels } from '../lib/demo-models'
import { buildEffortRows, ModelPalette } from './ModelPalette'

describe('ModelPalette', () => {
  it('preserves the advertised effort order', () => {
    expect(buildEffortRows(demoModels)).toEqual(['light', 'medium', 'high', 'xhigh', 'ultra'])
  })

  it('selects a supported model and effort combination', () => {
    const onSelect = vi.fn()
    render(
      <ModelPalette
        models={demoModels}
        selected={{ model: demoModels[0], effort: 'medium' }}
        onSelect={onSelect}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Terra, High' }))
    expect(onSelect).toHaveBeenCalledWith({ model: demoModels[1], effort: 'high' })
  })
})
