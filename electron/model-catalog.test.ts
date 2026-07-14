import { describe, expect, it } from 'vitest'
import { labelsFromModelCatalog } from './model-catalog'

describe('model catalog labels', () => {
  it('builds the matrix without opening native menus', () => {
    const labels = labelsFromModelCatalog([
      { modelId: 'gpt-5.6-sol', displayName: '5.6 Sol', defaultReasoningEffort: 'medium', supportedReasoningEfforts: ['low', 'medium', 'high', 'xhigh', 'ultra'], serviceTiers: [{ id: 'priority', name: 'Fast' }] },
      { modelId: 'gpt-5.6-luna', displayName: '5.6 Luna', defaultReasoningEffort: 'medium', supportedReasoningEfforts: ['low', 'medium', 'high', 'xhigh'], serviceTiers: [] },
    ], 'fr-FR')
    expect(labels.efforts).toEqual(['Léger', 'Moyen', 'Élevé', 'Très élevé', 'Ultra'])
    expect(labels.supportedEfforts).toEqual([[0, 1, 2, 3, 4], [0, 1, 2, 3]])
    expect(labels.speeds).toEqual(['Standard', 'Rapide'])
  })
})
