import { describe, expect, it, vi } from 'vitest'
import { buildCurrentSelectionExpression, buildLocalizedUiStringsExpression, buildModelCatalogExpression, buildSelectorPresentationExpression, buildSelectorShortcutExpression, buildSilentSelectionExpression, buildSilentSpeedExpression } from './codex-cdp'

describe('Codex CDP internal selector', () => {
  it('reads model capabilities without updating thread settings', () => {
    const expression = buildModelCatalogExpression()
    expect(expression).toContain('list-models-for-host')
    expect(expression).toContain('modelId: String(model?.model')
    expect(expression).toContain('supportedReasoningEfforts')
    expect(expression).not.toContain('update-thread-settings-for-next-turn')
  })

  it('reuses Codex translation ids for restart states', () => {
    const expression = buildLocalizedUiStringsExpression()
    expect(expression).toContain('threadPage.remoteConnectionStatusBadge.restartRequired')
    expect(expression).toContain('threadPage.remoteConnectionStatusBadge.restarting')
    expect(expression).toContain('formatMessage')
  })

  it('reads the closed native selector geometry and computed design without opening it', () => {
    const expression = buildSelectorPresentationExpression()
    expect(expression).toContain('data-codex-intelligence-trigger')
    expect(expression).toContain('getBoundingClientRect')
    expect(expression).toContain('getComputedStyle')
    expect(expression).toContain('--color-token-list-hover-background')
    expect(expression).not.toContain('.click()')
  })

  it('reads the configurable native selector shortcut from React props', () => {
    const expression = buildSelectorShortcutExpression()
    expect(expression).toContain('data-codex-intelligence-trigger')
    expect(expression).toContain('fiber.memoizedProps?.shortcut')
    expect(expression).not.toContain('Ctrl+Shift+M')
  })

  it('reads the current conversation selection from the native React controls', () => {
    const expression = buildCurrentSelectionExpression()
    expect(expression).toContain('data-codex-intelligence-trigger')
    expect(expression).toContain('controls.reasoningEffort')
    expect(expression).toContain('controls.selectedServiceTier')
    expect(expression).toContain('modelId: String(model.model')
    expect(expression).toContain('modelIndex')
    expect(expression).not.toContain('thread/settings/update')
  })

  it('updates the service tier without changing model or effort', () => {
    const expression = buildSilentSpeedExpression(1, 'priority')
    expect(expression).toContain('data-codex-intelligence-trigger')
    expect(expression).toContain('onSelectServiceTier(serviceTier)')
    expect(expression).toContain('"priority"')
    expect(expression).not.toContain('thread/settings/update')
  })

  it('uses null to restore standard speed', () => {
    expect(buildSilentSpeedExpression(0, 'priority')).toContain('const serviceTier = null')
  })
  it('uses the callback already wired to the native Codex selector', () => {
    const expression = buildSilentSelectionExpression({
      modelId: 'gpt-5.6-luna',
      modelLabel: '5.6 Luna',
      modelIndex: 2,
      effortIndex: 1,
    })

    expect(expression).toContain('data-codex-intelligence-trigger')
    expect(expression).toContain('candidate?.model === selection.modelId')
    expect(expression).toContain('onSelectPower(powerSelection)')
    expect(expression).toContain('onSelectModel(model.model, effort)')
    expect(expression).not.toContain('includes(requestedName)')
    expect(expression).not.toContain('thread/settings/update')
    expect(expression).toContain('"medium"')
  })

  it('prefers the exact native model id and power selection object at runtime', async () => {
    const trigger = document.createElement('button')
    trigger.setAttribute('data-codex-intelligence-trigger', 'true')
    document.body.append(trigger)

    const exactPowerSelection = {
      id: 'gpt-5.6-luna-medium',
      model: 'gpt-5.6-luna',
      reasoningEffort: 'medium',
    }
    const modelControls = {
      model: 'gpt-5.6-sol',
      reasoningEffort: 'low',
      models: [
        { model: 'gpt-5.6-luna-preview', displayName: '5.6 Luna', supportedReasoningEfforts: [{ reasoningEffort: 'medium' }] },
        { model: 'gpt-5.6-luna', displayName: '5.6 Luna', supportedReasoningEfforts: [{ reasoningEffort: 'medium' }] },
      ],
      onSelectModel: vi.fn(),
    }
    const powerControls = {
      powerSelections: [exactPowerSelection],
      onSelectPower: vi.fn(() => {
        modelControls.model = exactPowerSelection.model
        modelControls.reasoningEffort = exactPowerSelection.reasoningEffort
      }),
    }
    const fiber = {
      memoizedProps: modelControls,
      pendingProps: null,
      return: {
        memoizedProps: powerControls,
        pendingProps: null,
        return: null,
      },
    }
    Object.defineProperty(trigger, '__reactFiber$test', { value: fiber, enumerable: true })

    try {
      const expression = buildSilentSelectionExpression({
        modelId: 'gpt-5.6-luna',
        modelLabel: '5.6 Luna',
        modelIndex: 0,
        effortIndex: 1,
      })
      const result = await new Function(`return ${expression}`)() as { ok: boolean }

      expect(result.ok).toBe(true)
      expect(powerControls.onSelectPower).toHaveBeenCalledWith(exactPowerSelection)
      expect(modelControls.onSelectModel).not.toHaveBeenCalled()
    } finally {
      trigger.remove()
    }
  })

  it('escapes the model label inside the generated expression', () => {
    const expression = buildSilentSelectionExpression({
      modelId: 'gpt-5.6-luna',
      modelLabel: 'Luna"; throw new Error("escaped")',
      modelIndex: 0,
      effortIndex: 0,
    })

    expect(expression).toContain('Luna\\"; throw new Error(\\"escaped\\")')
  })

  it('does not guess a task id or route requests to an app-server host itself', () => {
    const expression = buildSilentSelectionExpression({
      modelId: 'gpt-5.6-sol',
      modelLabel: '5.6 Sol',
      modelIndex: 0,
      effortIndex: 0,
    })

    expect(expression).not.toContain('conversationId')
    expect(expression).not.toContain('threadId')
    expect(expression).not.toContain('hostId')
  })
})
