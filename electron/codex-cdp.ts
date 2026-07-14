export type DirectSelection = {
  modelLabel: string
  modelIndex: number
  effortIndex: number
}

export type DirectSelectionResult = {
  ok: true
  selection: string
  inputMode: 'cdp-internal'
}

export type DirectSpeedResult = {
  ok: true
  speedIndex: number
  inputMode: 'cdp-internal'
}

export type CodexCurrentSelection = {
  modelLabel: string
  modelIndex: number
  effortIndex: number
  speedIndex: number
}

export type CodexModelCatalogEntry = {
  displayName: string
  defaultReasoningEffort: string
  supportedReasoningEfforts: string[]
  serviceTiers: Array<{ id: string; name: string }>
}

export type CodexLocalizedUiStrings = {
  enableSilentMode: string
  restartingCodex: string
}

export type CodexSelectorPresentation = {
  width: number
  height: number
  paddingLeft: string
  paddingRight: string
  gap: string
  border: string
  borderRadius: string
  backgroundColor: string
  hoverBackgroundColor: string
  color: string
  modelColor: string
  fontFamily: string
  fontSize: string
  fontWeight: string
  lineHeight: string
  boxShadow: string
  iconSize: number
}

type CdpTarget = {
  type?: string
  url?: string
  webSocketDebuggerUrl?: string
}

type CdpEvaluateResponse = {
  id?: number
  error?: { message?: string }
  result?: {
    exceptionDetails?: {
      exception?: { description?: string }
      text?: string
    }
    result?: { value?: unknown }
  }
}

const EFFORT_VALUES = ['low', 'medium', 'high', 'xhigh', 'ultra'] as const

export function buildModelCatalogExpression() {
  return `
(async () => {
  const resourceUrls = [
    ...performance.getEntriesByType("resource").map((entry) => entry.name),
    ...[...document.querySelectorAll("link[href], script[src]")].map((node) => node.href || node.src),
  ].filter((name) => typeof name === "string");
  const hostModuleUrl = resourceUrls.find((name) => /\\/use-host-config-[^/]+\\.js(?:\\?|$)/.test(name));
  if (!hostModuleUrl) throw new Error("Codex internal request module was not found.");
  const hostModule = await import(hostModuleUrl);
  const request = Object.values(hostModule).find((value) => {
    if (typeof value !== "function") return false;
    try { return /\\.sendRequest\\(/.test(Function.prototype.toString.call(value)); }
    catch { return false; }
  });
  if (typeof request !== "function") throw new Error("Codex internal request function was not found.");
  const response = await request("list-models-for-host", {
    hostId: "local", includeHidden: true, cursor: null, limit: 100, priority: "critical",
  });
  const models = Array.isArray(response?.data) ? response.data : [];
  return models.filter((model) => model?.hidden !== true).map((model) => ({
    displayName: String(model?.displayName ?? ""),
    defaultReasoningEffort: String(model?.defaultReasoningEffort ?? ""),
    supportedReasoningEfforts: Array.isArray(model?.supportedReasoningEfforts)
      ? model.supportedReasoningEfforts.map((item) => String(item?.reasoningEffort ?? "")).filter(Boolean)
      : [],
    serviceTiers: Array.isArray(model?.serviceTiers)
      ? model.serviceTiers.map((tier) => ({ id: String(tier?.id ?? ""), name: String(tier?.name ?? "") }))
          .filter((tier) => tier.id && tier.name)
      : [],
  }));
})()
`.trim()
}

export function buildLocalizedUiStringsExpression() {
  return `
(() => {
  const messages = [
    { id: "threadPage.remoteConnectionStatusBadge.restartRequired", defaultMessage: "Restart required" },
    { id: "threadPage.remoteConnectionStatusBadge.restarting", defaultMessage: "Restarting" },
  ];
  function findIntl(value, depth = 0, seen = new WeakSet()) {
    if (value == null || typeof value !== "object" || depth > 5 || seen.has(value)) return null;
    seen.add(value);
    try {
      if (typeof value.formatMessage === "function") {
        return { enableSilentMode: value.formatMessage(messages[0]), restartingCodex: value.formatMessage(messages[1]) };
      }
    } catch {}
    let descriptors;
    try { descriptors = Object.getOwnPropertyDescriptors(value); } catch { return null; }
    const skipped = new Set(["children", "child", "sibling", "return", "stateNode", "_owner"]);
    for (const [key, descriptor] of Object.entries(descriptors).slice(0, 150)) {
      if (skipped.has(key) || !("value" in descriptor)) continue;
      const result = findIntl(descriptor.value, depth + 1, seen);
      if (result) return result;
    }
    return null;
  }
  for (const node of document.querySelectorAll("main, textarea, button, [role=main]")) {
    const fiberKey = Object.keys(node).find((key) => key.startsWith("__reactFiber$"));
    if (!fiberKey) continue;
    let fiber = node[fiberKey];
    for (let hops = 0; fiber && hops < 120; hops += 1, fiber = fiber.return) {
      for (const candidate of [fiber.memoizedProps, fiber.memoizedState, fiber.pendingProps]) {
        const result = findIntl(candidate);
        if (result) return result;
      }
    }
  }
  return { enableSilentMode: "Restart required", restartingCodex: "Restarting" };
})()
`.trim()
}

export function buildSelectorPresentationExpression() {
  return `
(() => {
  const trigger = document.querySelector('[data-codex-intelligence-trigger="true"]');
  if (!(trigger instanceof HTMLElement)) throw new Error("Codex native selector was not found.");
  const visibleSpan = (needle) => [...trigger.querySelectorAll("span")].find((node) => {
    const style = getComputedStyle(node);
    return String(node.className).includes(needle)
      && style.visibility !== "hidden"
      && style.display !== "none";
  });
  const model = visibleSpan("WorkTriggerModelText") ?? visibleSpan("WorkTriggerModelLabel");
  const effort = visibleSpan("WorkTriggerEffortLabel");
  const icon = trigger.querySelector("svg");
  const style = getComputedStyle(trigger);
  const modelStyle = model ? getComputedStyle(model) : style;
  const effortStyle = effort ? getComputedStyle(effort) : style;
  const rect = trigger.getBoundingClientRect();
  const iconRect = icon?.getBoundingClientRect();
  const rootStyle = getComputedStyle(document.documentElement);
  return {
    width: Math.round(rect.width * 1000) / 1000,
    height: Math.round(rect.height * 1000) / 1000,
    paddingLeft: style.paddingLeft,
    paddingRight: style.paddingRight,
    gap: style.gap,
    border: style.border,
    borderRadius: style.borderRadius,
    backgroundColor: style.backgroundColor,
    hoverBackgroundColor: rootStyle.getPropertyValue("--color-token-list-hover-background").trim()
      || "rgba(26, 28, 31, 0.053)",
    color: effortStyle.color,
    modelColor: modelStyle.color,
    fontFamily: style.fontFamily,
    fontSize: modelStyle.fontSize,
    fontWeight: modelStyle.fontWeight,
    lineHeight: modelStyle.lineHeight,
    boxShadow: style.boxShadow,
    iconSize: Math.round(iconRect?.width || 14),
  };
})()
`.trim()
}

export function buildSelectorShortcutExpression() {
  return `
(() => {
  const trigger = document.querySelector('[data-codex-intelligence-trigger="true"]');
  if (!trigger) throw new Error("Codex native selector was not found.");
  const fiberKey = Object.keys(trigger).find((key) => key.startsWith("__reactFiber$"));
  if (!fiberKey) throw new Error("Codex native selector shortcut was not found.");
  let fiber = trigger[fiberKey];
  for (let hops = 0; fiber && hops < 60; hops += 1, fiber = fiber.return) {
    const shortcut = fiber.memoizedProps?.shortcut;
    if (typeof shortcut === "string" && shortcut.trim()) return shortcut.trim();
  }
  throw new Error("Codex native selector shortcut was not found.");
})()
`.trim()
}

export function buildCurrentSelectionExpression() {
  return `
(() => {
  function findControls() {
    const trigger = document.querySelector('[data-codex-intelligence-trigger="true"]');
    if (!trigger) return null;
    const fiberKey = Object.keys(trigger).find((key) => key.startsWith("__reactFiber$"));
    if (!fiberKey) return null;
    let fiber = trigger[fiberKey];
    for (let hops = 0; fiber && hops < 100; hops += 1, fiber = fiber.return) {
      const props = fiber.memoizedProps;
      if (props && typeof props.onSelectModel === "function" && Array.isArray(props.models)) return props;
    }
    return null;
  }

  const controls = findControls();
  if (!controls) throw new Error("Codex native selector state was not found.");
  const visibleModels = controls.models.filter((model) => model?.hidden !== true);
  const modelIndex = visibleModels.findIndex((model) => model?.model === controls.model);
  const model = visibleModels[modelIndex];
  const effortIndex = ${JSON.stringify(EFFORT_VALUES)}.indexOf(controls.reasoningEffort);
  const speedIndex = Array.isArray(controls.serviceTierOptions)
    ? controls.serviceTierOptions.findIndex((option) => option?.value === controls.selectedServiceTier)
    : -1;
  if (!model || modelIndex < 0 || effortIndex < 0) {
    throw new Error("Codex returned an incomplete native selector state.");
  }
  return {
    modelLabel: String(model.displayName ?? ""),
    modelIndex,
    effortIndex,
    speedIndex,
  };
})()
`.trim()
}

export function buildSilentSelectionExpression(selection: DirectSelection) {
  const input = JSON.stringify(selection)

  return `
(async () => {
  const selection = ${input};
  const effort = ${JSON.stringify(EFFORT_VALUES)}[selection.effortIndex];
  const normalize = (value) => String(value ?? "")
    .normalize("NFKD")
    .replace(/[\\u0300-\\u036f]/g, "")
    .replace(/[^a-zA-Z0-9]+/g, " ")
    .trim()
    .toLowerCase();
  if (!effort) throw new Error("Requested Codex effort was not found.");

  function findControls() {
    const trigger = document.querySelector('[data-codex-intelligence-trigger="true"]');
    if (!trigger) return null;
    const fiberKey = Object.keys(trigger).find((key) => key.startsWith("__reactFiber$"));
    if (!fiberKey) return null;
    let fiber = trigger[fiberKey];
    for (let hops = 0; fiber && hops < 100; hops += 1, fiber = fiber.return) {
      const props = fiber.memoizedProps;
      if (props && typeof props.onSelectModel === "function" && Array.isArray(props.models)) return props;
    }
    return null;
  }

  const controls = findControls();
  if (!controls) throw new Error("Codex native model callback was not found.");
  const visibleModels = controls.models.filter((model) => model?.hidden !== true);
  const requestedName = normalize(selection.modelLabel);
  const model = controls.models.find((candidate) => normalize(candidate?.displayName) === requestedName)
    ?? controls.models.find((candidate) => normalize(candidate?.displayName).includes(requestedName))
    ?? visibleModels[selection.modelIndex];
  if (!model?.model) throw new Error("Requested Codex model was not found.");
  const supported = Array.isArray(model.supportedReasoningEfforts)
    ? model.supportedReasoningEfforts.map((item) => item?.reasoningEffort)
    : [];
  if (supported.length > 0 && !supported.includes(effort)) {
    throw new Error("Requested Codex model and effort combination is unsupported.");
  }

  const result = controls.onSelectModel(model.model, effort);
  if (result && typeof result.then === "function") await result;
  let confirmed = false;
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 50));
    const current = findControls();
    if (current?.model === model.model && current?.reasoningEffort === effort) {
      confirmed = true;
      break;
    }
  }
  if (!confirmed) throw new Error("Codex did not reflect the requested native selection.");
  return {
    ok: true,
    selection: model.displayName + " · " + effort,
    inputMode: "cdp-internal",
  };
})()
`.trim()
}

export function buildSilentSpeedExpression(speedIndex: number, fastServiceTierId: string) {
  const serviceTier = speedIndex === 0 ? null : fastServiceTierId
  return `
(async () => {
  const serviceTier = ${JSON.stringify(serviceTier)};
  const speedIndex = ${JSON.stringify(speedIndex)};

  function findControls() {
    const trigger = document.querySelector('[data-codex-intelligence-trigger="true"]');
    if (!trigger) return null;
    const fiberKey = Object.keys(trigger).find((key) => key.startsWith("__reactFiber$"));
    if (!fiberKey) return null;
    let fiber = trigger[fiberKey];
    for (let hops = 0; fiber && hops < 100; hops += 1, fiber = fiber.return) {
      const props = fiber.memoizedProps;
      if (props && typeof props.onSelectServiceTier === "function" && Array.isArray(props.serviceTierOptions)) {
        return props;
      }
    }
    return null;
  }

  const controls = findControls();
  if (!controls) throw new Error("Codex native speed callback was not found.");
  if (!controls.serviceTierOptions.some((option) => option?.value === serviceTier)) {
    throw new Error("Requested Codex speed tier was not found.");
  }
  const result = controls.onSelectServiceTier(serviceTier);
  if (result && typeof result.then === "function") await result;
  let confirmed = false;
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await new Promise((resolve) => setTimeout(resolve, 50));
    if (findControls()?.selectedServiceTier === serviceTier) {
      confirmed = true;
      break;
    }
  }
  if (!confirmed) throw new Error("Codex did not reflect the requested speed tier.");
  return { ok: true, speedIndex, inputMode: "cdp-internal" };
})()
`.trim()
}

async function evaluateCdp(webSocketUrl: string, expression: string): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(webSocketUrl)
    let settled = false
    const timeout = setTimeout(() => {
      finish(() => reject(new Error('Codex CDP evaluation timed out.')))
    }, 8_000)

    const finish = (callback: () => void) => {
      if (settled) return
      settled = true
      clearTimeout(timeout)
      try { socket.close() } catch { /* The target may already be gone. */ }
      callback()
    }

    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({
        id: 1,
        method: 'Runtime.evaluate',
        params: { expression, awaitPromise: true, returnByValue: true, userGesture: false },
      }))
    })
    socket.addEventListener('message', (event) => {
      let payload: CdpEvaluateResponse
      try {
        payload = JSON.parse(String(event.data)) as CdpEvaluateResponse
      } catch {
        return
      }
      if (payload.id !== 1) return
      const exception = payload.result?.exceptionDetails
      if (payload.error || exception) {
        const message = payload.error?.message
          ?? exception?.exception?.description
          ?? exception?.text
          ?? 'Codex rejected the internal selection.'
        finish(() => reject(new Error(message)))
        return
      }
      finish(() => resolve(payload.result?.result?.value))
    })
    socket.addEventListener('error', () => {
      finish(() => reject(new Error('Could not connect to the Codex CDP target.')))
    })
    socket.addEventListener('close', () => {
      finish(() => reject(new Error('Codex closed the CDP target before confirming the selection.')))
    })
  })
}

async function getCodexRendererTarget(port: number) {
  if (!Number.isInteger(port) || port < 1 || port > 65_535) throw new Error('Invalid Codex CDP port.')
  const response = await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(2_000) })
  if (!response.ok) throw new Error('Codex CDP target list is unavailable.')
  const targets = await response.json() as CdpTarget[]
  const target = targets.find((candidate) =>
    candidate.type === 'page'
    && candidate.url?.startsWith('app://')
    && typeof candidate.webSocketDebuggerUrl === 'string',
  )
  if (!target?.webSocketDebuggerUrl) throw new Error('Codex renderer CDP target was not found.')
  return target.webSocketDebuggerUrl
}

export async function getModelCatalogThroughCodexRenderer(port: number): Promise<CodexModelCatalogEntry[]> {
  const result = await evaluateCdp(await getCodexRendererTarget(port), buildModelCatalogExpression())
  if (!Array.isArray(result)) throw new Error('Codex returned an invalid model catalog.')
  return result.filter((entry): entry is CodexModelCatalogEntry => {
    if (typeof entry !== 'object' || entry === null) return false
    const candidate = entry as Partial<CodexModelCatalogEntry>
    return typeof candidate.displayName === 'string'
      && typeof candidate.defaultReasoningEffort === 'string'
      && Array.isArray(candidate.supportedReasoningEfforts)
      && Array.isArray(candidate.serviceTiers)
  })
}

export async function getLocalizedUiStringsThroughCodexRenderer(port: number): Promise<CodexLocalizedUiStrings> {
  const result = await evaluateCdp(await getCodexRendererTarget(port), buildLocalizedUiStringsExpression())
  if (typeof result !== 'object' || result === null) throw new Error('Codex returned invalid localized UI strings.')
  const strings = result as Partial<CodexLocalizedUiStrings>
  if (typeof strings.enableSilentMode !== 'string' || typeof strings.restartingCodex !== 'string') {
    throw new Error('Codex returned incomplete localized UI strings.')
  }
  return strings as CodexLocalizedUiStrings
}

export async function getSelectorPresentationThroughCodexRenderer(port: number): Promise<CodexSelectorPresentation> {
  const result = await evaluateCdp(await getCodexRendererTarget(port), buildSelectorPresentationExpression())
  if (typeof result !== 'object' || result === null) throw new Error('Codex returned invalid selector presentation data.')
  const presentation = result as Partial<CodexSelectorPresentation>
  const stringKeys: Array<keyof CodexSelectorPresentation> = [
    'paddingLeft', 'paddingRight', 'gap', 'border', 'borderRadius', 'backgroundColor',
    'hoverBackgroundColor', 'color', 'modelColor', 'fontFamily', 'fontSize', 'fontWeight',
    'lineHeight', 'boxShadow',
  ]
  if (
    !Number.isFinite(presentation.width)
    || !Number.isFinite(presentation.height)
    || !Number.isFinite(presentation.iconSize)
    || stringKeys.some((key) => typeof presentation[key] !== 'string')
  ) throw new Error('Codex returned incomplete selector presentation data.')
  return presentation as CodexSelectorPresentation
}

export async function getSelectorShortcutThroughCodexRenderer(port: number): Promise<string> {
  const result = await evaluateCdp(await getCodexRendererTarget(port), buildSelectorShortcutExpression())
  if (typeof result !== 'string' || !result.trim()) throw new Error('Codex returned an invalid selector shortcut.')
  return result.trim()
}

export async function getCurrentSelectionThroughCodexRenderer(port: number): Promise<CodexCurrentSelection> {
  const result = await evaluateCdp(await getCodexRendererTarget(port), buildCurrentSelectionExpression())
  if (typeof result !== 'object' || result === null) throw new Error('Codex returned invalid selector state.')
  const selection = result as Partial<CodexCurrentSelection>
  if (
    typeof selection.modelLabel !== 'string'
    || !Number.isInteger(selection.modelIndex)
    || !Number.isInteger(selection.effortIndex)
    || !Number.isInteger(selection.speedIndex)
  ) throw new Error('Codex returned incomplete selector state.')
  return selection as CodexCurrentSelection
}

export async function applySpeedThroughCodexRenderer(
  port: number,
  speedIndex: number,
  fastServiceTierId: string,
): Promise<DirectSpeedResult> {
  if (speedIndex !== 0 && speedIndex !== 1) throw new Error('Invalid Codex speed index.')
  if (!fastServiceTierId) throw new Error('Codex fast service tier was not found.')
  const result = await evaluateCdp(
    await getCodexRendererTarget(port),
    buildSilentSpeedExpression(speedIndex, fastServiceTierId),
  )
  if (
    typeof result !== 'object'
    || result === null
    || (result as { ok?: unknown }).ok !== true
    || (result as { inputMode?: unknown }).inputMode !== 'cdp-internal'
  ) throw new Error('Codex did not confirm the internal speed selection.')
  return result as DirectSpeedResult
}

export async function applySelectionThroughCodexRenderer(
  port: number,
  selection: DirectSelection,
): Promise<DirectSelectionResult> {
  const result = await evaluateCdp(
    await getCodexRendererTarget(port),
    buildSilentSelectionExpression(selection),
  )
  if (
    typeof result !== 'object'
    || result === null
    || (result as { ok?: unknown }).ok !== true
    || (result as { inputMode?: unknown }).inputMode !== 'cdp-internal'
  ) {
    throw new Error('Codex did not confirm the internal selection.')
  }
  return result as DirectSelectionResult
}
