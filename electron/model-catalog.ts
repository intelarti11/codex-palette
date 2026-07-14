import type { CodexModelCatalogEntry } from './codex-cdp'

const EFFORT_ORDER = ['low', 'medium', 'high', 'xhigh', 'ultra']
const EFFORT_LABELS: Record<string, Record<string, string>> = {
  fr: { low: 'Léger', medium: 'Moyen', high: 'Élevé', xhigh: 'Très élevé', ultra: 'Ultra' },
  es: { low: 'Ligero', medium: 'Medio', high: 'Alto', xhigh: 'Muy alto', ultra: 'Ultra' },
  en: { low: 'Low', medium: 'Medium', high: 'High', xhigh: 'Extra high', ultra: 'Ultra' },
}

export function labelsFromModelCatalog(catalog: CodexModelCatalogEntry[], locale: string) {
  const language = locale.toLowerCase().split('-')[0]
  const effortLabels = EFFORT_LABELS[language] ?? EFFORT_LABELS.en
  const availableEfforts = EFFORT_ORDER.filter((effort) => catalog.some((model) => model.supportedReasoningEfforts.includes(effort)))
  const hasFastTier = catalog.some((model) => model.serviceTiers.length > 0)
  const speedText = language === 'fr'
    ? { label: 'Vitesse', standard: 'Standard', fast: 'Rapide' }
    : language === 'es'
      ? { label: 'Velocidad', standard: 'Estándar', fast: 'Rápido' }
      : { label: 'Speed', standard: 'Standard', fast: 'Fast' }
  return {
    models: catalog.map((model) => model.displayName),
    efforts: availableEfforts.map((effort) => effortLabels[effort] ?? effort),
    supportedEfforts: catalog.map((model) => model.supportedReasoningEfforts.map((effort) => availableEfforts.indexOf(effort)).filter((index) => index >= 0)),
    speedLabel: hasFastTier ? speedText.label : '',
    speeds: hasFastTier ? [speedText.standard, speedText.fast] : [],
    speedIndex: hasFastTier ? 0 : -1,
  }
}
