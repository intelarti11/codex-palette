export type ReasoningEffort = string

export interface ReasoningEffortOption {
  reasoningEffort: ReasoningEffort
  description: string
}

export interface CodexModel {
  id: string
  model: string
  displayName: string
  description: string
  hidden: boolean
  supportedReasoningEfforts: ReasoningEffortOption[]
  defaultReasoningEffort: ReasoningEffort
  inputModalities: string[]
  isDefault: boolean
}

export interface ModelListResponse {
  data: CodexModel[]
  nextCursor: string | null
}

export interface SelectedModel {
  model: CodexModel
  effort: ReasoningEffort
}

export interface RpcError {
  code: number
  message: string
  data?: unknown
}

export interface RpcResponse<T = unknown> {
  id: number | string
  result?: T
  error?: RpcError
}

export interface RpcNotification<T = unknown> {
  method: string
  params?: T
}

export interface RpcServerRequest<T = unknown> {
  id: number | string
  method: string
  params?: T
}

export interface ThreadStartResponse {
  thread: {
    id: string
    preview?: string
  }
  model: string
  reasoningEffort?: string | null
}

export interface TurnStartResponse {
  turn: {
    id: string
    status: string
  }
}

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant' | 'system'
  text: string
  pending?: boolean
}

export interface ApprovalRequest {
  id: number | string
  method: string
  params: Record<string, unknown>
}

export interface CodexPaletteBridge {
  start: () => Promise<{ running: boolean; version?: string }>
  stop: () => Promise<{ running: boolean }>
  send: (payload: unknown) => Promise<{ sent: boolean }>
  version: () => Promise<string>
  selectProject: () => Promise<string | null>
  openExternal: (url: string) => Promise<void>
  onMessage: (callback: (line: string) => void) => () => void
  onLog: (callback: (line: string) => void) => () => void
  onStatus: (callback: (status: unknown) => void) => () => void
}

declare global {
  interface Window {
    codexPalette?: CodexPaletteBridge
  }
}
