import { Check, Moon, Sprout, Sun } from 'lucide-react'
import type { CodexModel, ReasoningEffort, SelectedModel } from '../types'

interface ModelPaletteProps {
  models: CodexModel[]
  selected: SelectedModel
  onSelect: (selection: SelectedModel) => void
}

const modelIcons = [Sun, Sprout, Moon]

const effortLabel = (effort: string) => {
  const labels: Record<string, string> = {
    minimal: 'Minimal',
    none: 'None',
    light: 'Light',
    low: 'Low',
    medium: 'Medium',
    high: 'High',
    xhigh: 'Extra High',
    extra_high: 'Extra High',
    ultra: 'Ultra',
  }
  return labels[effort] ?? effort.replaceAll('_', ' ').replace(/\b\w/g, (value) => value.toUpperCase())
}

export function buildEffortRows(models: CodexModel[]): ReasoningEffort[] {
  const rows: ReasoningEffort[] = []
  const preferredModel = models.find((model) => model.isDefault) ?? models[0]
  const orderedModels = preferredModel
    ? [preferredModel, ...models.filter((model) => model !== preferredModel)]
    : models

  for (const model of orderedModels) {
    for (const option of model.supportedReasoningEfforts) {
      if (!rows.includes(option.reasoningEffort)) rows.push(option.reasoningEffort)
    }
  }
  return rows
}

export function ModelPalette({ models, selected, onSelect }: ModelPaletteProps) {
  const effortRows = buildEffortRows(models)

  return (
    <div className="model-palette" role="dialog" aria-label="Choose a model and reasoning effort">
      <div className="palette-grid" style={{ '--model-count': Math.max(models.length, 1) } as React.CSSProperties}>
        <div className="palette-corner" />
        {models.map((model, index) => {
          const Icon = modelIcons[index % modelIcons.length]
          return (
            <div className={`model-heading model-tone-${index % 3}`} key={model.id}>
              <Icon size={19} strokeWidth={1.8} />
              <span>{model.displayName}</span>
            </div>
          )
        })}

        {effortRows.map((effort, rowIndex) => (
          <div className="palette-row" key={effort}>
            <div className="effort-label">{effortLabel(effort)}</div>
            {models.map((model, modelIndex) => {
              const supported = model.supportedReasoningEfforts.some(
                (option) => option.reasoningEffort === effort,
              )
              const isSelected = selected.model.id === model.id && selected.effort === effort
              const option = model.supportedReasoningEfforts.find(
                (item) => item.reasoningEffort === effort,
              )

              return (
                <button
                  className={`effort-cell model-tone-${modelIndex % 3} ${isSelected ? 'selected' : ''}`}
                  key={`${model.id}-${effort}`}
                  type="button"
                  disabled={!supported}
                  aria-pressed={isSelected}
                  aria-label={`${model.displayName}, ${effortLabel(effort)}`}
                  title={option?.description ?? 'Not supported'}
                  style={{ '--effort-level': rowIndex + 1 } as React.CSSProperties}
                  onClick={() => supported && onSelect({ model, effort })}
                >
                  {isSelected ? <Check size={21} strokeWidth={2} /> : null}
                </button>
              )
            })}
          </div>
        ))}
      </div>

      <div className="palette-footer">
        <div className="palette-default-row">
          <span className="default-star">★</span>
          <span>
            {selected.model.displayName} · {effortLabel(selected.effort)} selected
          </span>
        </div>
        <p>Shade indicates the relative reasoning intensity within each model.</p>
      </div>
    </div>
  )
}
