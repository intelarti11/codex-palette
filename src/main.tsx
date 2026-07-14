import { createRoot } from 'react-dom/client'
import App from './App'
import './styles.css'

if (import.meta.env.DEV && new URLSearchParams(window.location.search).has('mock-codex')) {
  const models = ['5.6 Sol', '5.6 Terra', '5.6 Luna', '5.5', '5.4', '5.4 Mini']
  const efforts = ['Léger', 'Moyen', 'Élevé', 'Très élevé', 'Ultra']
  window.codexOverlay = {
    setOpen: async () => undefined,
    showContextMenu: () => undefined,
    enableSilentMode: async () => ({ ok: true, port: 45000 }),
    getLabels: async () => ({
      models,
      efforts,
      supportedEfforts: models.map((_, index) => index < 2 ? [0, 1, 2, 3, 4] : [0, 1, 2, 3]),
      speedLabel: 'Vitesse',
      speeds: ['Standard', 'Rapide'],
      speedIndex: 0,
      currentSelection: { modelIndex: 0, effortIndex: 3, speedIndex: 0 },
    }),
    onSelectorPresentation: () => () => undefined,
    onSelectionChanged: () => () => undefined,
    onOpenChanged: () => () => undefined,
    beginDrag: async () => ({ x: 0, y: 0 }),
    dragTo: () => undefined,
    endDrag: async () => undefined,
    apply: async ({ modelIndex, effortIndex }) => ({
      ok: true,
      selection: `${models[modelIndex]} · ${efforts[effortIndex]}`,
      inputMode: 'cdp-internal',
    }),
    applySpeed: async (speedIndex) => ({ ok: true, speedIndex, speed: ['Standard', 'Rapide'][speedIndex] }),
    resetPosition: async () => undefined,
    quit: async () => undefined,
  }
}

createRoot(document.getElementById('root')!).render(<App />)
