import type {
  ModelListResponse,
  RpcNotification,
  RpcResponse,
  RpcServerRequest,
} from '../types'

type NotificationHandler = (message: RpcNotification) => void
type ServerRequestHandler = (message: RpcServerRequest) => void

export class CodexClient {
  private nextId = 1
  private pending = new Map<
    number,
    {
      resolve: (value: unknown) => void
      reject: (error: Error) => void
      timeout: ReturnType<typeof setTimeout>
    }
  >()
  private notificationHandlers = new Set<NotificationHandler>()
  private serverRequestHandlers = new Set<ServerRequestHandler>()
  private unsubscribeMessage?: () => void
  private connected = false

  get isConnected() {
    return this.connected
  }

  async connect() {
    if (this.connected) return
    const bridge = window.codexPalette
    if (!bridge) throw new Error('Desktop bridge is unavailable.')

    this.unsubscribeMessage = bridge.onMessage((line) => this.handleLine(line))
    await bridge.start()

    await this.request('initialize', {
      clientInfo: {
        name: 'codex_palette',
        title: 'Codex Palette',
        version: '0.1.0',
      },
      capabilities: {
        experimentalApi: false,
      },
    })
    await this.notify('initialized', {})
    this.connected = true
  }

  async disconnect() {
    this.unsubscribeMessage?.()
    this.unsubscribeMessage = undefined
    this.connected = false
    for (const entry of this.pending.values()) {
      clearTimeout(entry.timeout)
      entry.reject(new Error('Codex connection closed.'))
    }
    this.pending.clear()
    await window.codexPalette?.stop()
  }

  onNotification(handler: NotificationHandler) {
    this.notificationHandlers.add(handler)
    return () => this.notificationHandlers.delete(handler)
  }

  onServerRequest(handler: ServerRequestHandler) {
    this.serverRequestHandlers.add(handler)
    return () => this.serverRequestHandlers.delete(handler)
  }

  async listModels(): Promise<ModelListResponse> {
    return this.request<ModelListResponse>('model/list', {
      limit: 100,
      includeHidden: false,
    })
  }

  async request<T = unknown>(method: string, params?: unknown): Promise<T> {
    const bridge = window.codexPalette
    if (!bridge) throw new Error('Desktop bridge is unavailable.')

    const id = this.nextId++
    const payload = params === undefined ? { method, id } : { method, id, params }

    const promise = new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`${method} timed out.`))
      }, 45_000)

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timeout,
      })
    })

    await bridge.send(payload)
    return promise
  }

  async notify(method: string, params?: unknown) {
    const payload = params === undefined ? { method } : { method, params }
    await window.codexPalette?.send(payload)
  }

  async respond(id: number | string, result: unknown) {
    await window.codexPalette?.send({ id, result })
  }

  private handleLine(line: string) {
    let message: RpcResponse | RpcNotification | RpcServerRequest
    try {
      message = JSON.parse(line) as RpcResponse | RpcNotification | RpcServerRequest
    } catch {
      return
    }

    if ('id' in message && 'method' in message) {
      for (const handler of this.serverRequestHandlers) handler(message)
      return
    }

    if ('id' in message) {
      const numericId = typeof message.id === 'number' ? message.id : Number(message.id)
      const entry = this.pending.get(numericId)
      if (!entry) return

      clearTimeout(entry.timeout)
      this.pending.delete(numericId)
      if (message.error) {
        entry.reject(new Error(message.error.message))
      } else {
        entry.resolve(message.result)
      }
      return
    }

    if ('method' in message) {
      for (const handler of this.notificationHandlers) handler(message)
    }
  }
}
