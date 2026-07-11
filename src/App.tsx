import {
  Bell,
  Bot,
  Bug,
  ChevronDown,
  Code2,
  GitBranch,
  Hammer,
  SearchCode,
  Sparkles,
} from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ApprovalDialog } from './components/ApprovalDialog'
import { Composer } from './components/Composer'
import { ModelPalette } from './components/ModelPalette'
import { CodexClient } from './lib/codex-client'
import { demoModels } from './lib/demo-models'
import type {
  ApprovalRequest,
  ChatMessage,
  CodexModel,
  SelectedModel,
  ThreadStartResponse,
  TurnStartResponse,
} from './types'

const starterCards = [
  {
    icon: SearchCode,
    title: 'Explore and understand code',
    prompt: 'Explore this repository and explain its architecture, main modules, and important workflows.',
    tone: 'blue',
  },
  {
    icon: Hammer,
    title: 'Build a new feature, app, or tool',
    prompt: 'Inspect this project and propose a useful feature. Then implement it with tests.',
    tone: 'indigo',
  },
  {
    icon: Code2,
    title: 'Review code and suggest changes',
    prompt: 'Review the current codebase and suggest the highest-impact improvements, with concrete patches.',
    tone: 'green',
  },
  {
    icon: Bug,
    title: 'Fix issues and failures',
    prompt: 'Run the relevant checks, find the most important failure, and fix it safely.',
    tone: 'orange',
  },
]

const makeId = () => crypto.randomUUID?.() ?? `${Date.now()}-${Math.random()}`

export default function App() {
  const clientRef = useRef<CodexClient | null>(null)
  const threadIdRef = useRef<string | null>(null)
  const turnIdRef = useRef<string | null>(null)
  const assistantItemIdRef = useRef<string | null>(null)

  const [models, setModels] = useState<CodexModel[]>(demoModels)
  const [selected, setSelected] = useState<SelectedModel>({
    model: demoModels.find((model) => model.isDefault) ?? demoModels[0],
    effort: demoModels.find((model) => model.isDefault)?.defaultReasoningEffort ?? 'medium',
  })
  const [projectPath, setProjectPath] = useState<string | null>(null)
  const [paletteOpen, setPaletteOpen] = useState(false)
  const [connection, setConnection] = useState<'connecting' | 'live' | 'demo'>('connecting')
  const [version, setVersion] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [approval, setApproval] = useState<ApprovalRequest | null>(null)
  const [error, setError] = useState<string | null>(null)

  const defaultSelectionFor = useCallback((catalog: CodexModel[]) => {
    const model = catalog.find((candidate) => candidate.isDefault) ?? catalog[0]
    return { model, effort: model.defaultReasoningEffort }
  }, [])

  useEffect(() => {
    const client = new CodexClient()
    clientRef.current = client

    const removeNotification = client.onNotification((notification) => {
      const params = (notification.params ?? {}) as Record<string, unknown>

      if (notification.method === 'item/agentMessage/delta') {
        const itemId = String(params.itemId ?? '')
        const delta = String(params.delta ?? '')
        assistantItemIdRef.current = itemId
        setMessages((current) => {
          const existing = current.findIndex((message) => message.id === itemId)
          if (existing >= 0) {
            return current.map((message, index) =>
              index === existing ? { ...message, text: message.text + delta, pending: true } : message,
            )
          }
          return [...current, { id: itemId || makeId(), role: 'assistant', text: delta, pending: true }]
        })
      }

      if (notification.method === 'item/completed') {
        const item = params.item as Record<string, unknown> | undefined
        if (item?.type === 'agentMessage') {
          const id = String(item.id ?? assistantItemIdRef.current ?? makeId())
          const text = String(item.text ?? '')
          setMessages((current) => {
            const found = current.some((message) => message.id === id)
            if (!found) return [...current, { id, role: 'assistant', text }]
            return current.map((message) =>
              message.id === id ? { ...message, text: text || message.text, pending: false } : message,
            )
          })
        }

        if (item?.type === 'commandExecution') {
          const command = String(item.command ?? 'Command completed')
          const status = String(item.status ?? 'completed')
          setMessages((current) => [
            ...current,
            { id: makeId(), role: 'system', text: `${status}: ${command}` },
          ])
        }
      }

      if (notification.method === 'turn/completed') {
        setBusy(false)
        turnIdRef.current = null
        assistantItemIdRef.current = null
      }

      if (notification.method === 'error') {
        setError(String(params.message ?? 'Codex reported an error.'))
        setBusy(false)
      }
    })

    const removeServerRequest = client.onServerRequest((request) => {
      if (request.method.includes('requestApproval')) {
        setApproval({
          id: request.id,
          method: request.method,
          params: (request.params ?? {}) as Record<string, unknown>,
        })
      }
    })

    const connect = async () => {
      if (!window.codexPalette) {
        setConnection('demo')
        return
      }

      try {
        const bridgeVersion = await window.codexPalette.version()
        setVersion(bridgeVersion)
        await client.connect()
        const catalog = await client.listModels()
        if (catalog.data.length > 0) {
          setModels(catalog.data)
          setSelected(defaultSelectionFor(catalog.data))
        }
        setConnection('live')
      } catch (connectError) {
        setConnection('demo')
        setError(
          connectError instanceof Error
            ? `${connectError.message} Using the built-in preview catalog.`
            : 'Could not connect to Codex. Using demo mode.',
        )
      }
    }

    void connect()

    return () => {
      removeNotification()
      removeServerRequest()
      void client.disconnect()
    }
  }, [defaultSelectionFor])

  const chooseProject = useCallback(async () => {
    if (!window.codexPalette) {
      setProjectPath('/demo/codex-palette')
      return '/demo/codex-palette'
    }
    const path = await window.codexPalette.selectProject()
    if (path) {
      setProjectPath(path)
      threadIdRef.current = null
      setMessages([])
    }
    return path
  }, [])

  const simulateDemoTurn = useCallback((prompt: string) => {
    setBusy(true)
    const responseId = makeId()
    setTimeout(() => {
      setMessages((current) => [
        ...current,
        {
          id: responseId,
          role: 'assistant',
          text: `Demo mode is active. The visual model palette selected ${selected.model.displayName} with ${selected.effort} reasoning. Install and sign in to Codex CLI to run this prompt against your project.`,
        },
      ])
      setBusy(false)
    }, 650)
  }, [selected])

  const sendPrompt = useCallback(
    async (prompt: string) => {
      setError(null)
      setPaletteOpen(false)
      setMessages((current) => [...current, { id: makeId(), role: 'user', text: prompt }])

      if (connection !== 'live') {
        simulateDemoTurn(prompt)
        return
      }

      let cwd = projectPath
      if (!cwd) cwd = await chooseProject()
      if (!cwd) {
        setError('Choose a project folder before starting a Codex turn.')
        return
      }

      const client = clientRef.current
      if (!client) return

      try {
        setBusy(true)
        if (!threadIdRef.current) {
          const response = await client.request<ThreadStartResponse>('thread/start', {
            model: selected.model.model,
            cwd,
            sandbox: 'workspace-write',
          })
          threadIdRef.current = response.thread.id
        }

        const response = await client.request<TurnStartResponse>('turn/start', {
          threadId: threadIdRef.current,
          input: [{ type: 'text', text: prompt, textElements: [] }],
          model: selected.model.model,
          effort: selected.effort,
        })
        turnIdRef.current = response.turn.id
      } catch (sendError) {
        setBusy(false)
        setError(sendError instanceof Error ? sendError.message : 'Could not start the Codex turn.')
      }
    }, [chooseProject, connection, projectPath, selected, simulateDemoTurn],
  )

  const interrupt = useCallback(async () => {
    const client = clientRef.current
    if (connection !== 'live' || !client || !threadIdRef.current || !turnIdRef.current) {
      setBusy(false)
      return
    }
    try {
      await client.request('turn/interrupt', {
        threadId: threadIdRef.current,
        turnId: turnIdRef.current,
      })
    } finally {
      setBusy(false)
    }
  }, [connection])

  const decideApproval = useCallback(
    async (decision: 'accept' | 'acceptForSession' | 'decline' | 'cancel') => {
      if (!approval) return
      await clientRef.current?.respond(approval.id, { decision })
      setApproval(null)
    },
    [approval],
  )

  const hasConversation = messages.length > 0
  const connectionLabel = useMemo(() => {
    if (connection === 'connecting') return 'Connecting'
    if (connection === 'live') return version ? `Live · ${version}` : 'Live'
    return 'Preview mode'
  }, [connection, version])

  return (
    <main className="app-shell" onClick={() => paletteOpen && setPaletteOpen(false)}>
      <header className="app-header">
        <div className="brand">
          <div className="brand-mark" aria-hidden="true">
            <Sparkles size={23} />
          </div>
          <span>Codex Palette</span>
          <ChevronDown size={16} />
        </div>
        <div className="header-actions">
          <div className={`connection-pill ${connection}`}>
            <span />
            {connectionLabel}
          </div>
          <button className="header-icon" type="button" aria-label="Notifications">
            <Bell size={20} />
          </button>
          <div className="avatar">CP</div>
        </div>
      </header>

      <section className={`workspace ${hasConversation ? 'conversation-active' : ''}`}>
        {!hasConversation ? (
          <div className="welcome">
            <div className="welcome-mark">
              <Bot size={42} strokeWidth={1.45} />
            </div>
            <h1>What should we build?</h1>
            <div className="starter-grid">
              {starterCards.map(({ icon: Icon, title, prompt, tone }) => (
                <button
                  className={`starter-card ${tone}`}
                  type="button"
                  key={title}
                  onClick={() => void sendPrompt(prompt)}
                >
                  <Icon size={24} strokeWidth={1.8} />
                  <span>{title}</span>
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="conversation" aria-live="polite">
            {messages.map((message) => (
              <article className={`message ${message.role}`} key={message.id}>
                <div className="message-role">
                  {message.role === 'user' ? 'You' : message.role === 'assistant' ? selected.model.displayName : 'Tool'}
                </div>
                <div className="message-body">
                  {message.text}
                  {message.pending ? <span className="cursor" /> : null}
                </div>
              </article>
            ))}
          </div>
        )}

        {error ? (
          <div className="error-banner" role="alert">
            {error}
            <button type="button" onClick={() => setError(null)}>
              Dismiss
            </button>
          </div>
        ) : null}

        <div className="composer-area" onClick={(event) => event.stopPropagation()}>
          {paletteOpen ? (
            <ModelPalette
              models={models}
              selected={selected}
              onSelect={(next) => {
                setSelected(next)
                setPaletteOpen(false)
              }}
            />
          ) : null}
          <Composer
            projectPath={projectPath}
            selected={selected}
            paletteOpen={paletteOpen}
            busy={busy}
            disabled={connection === 'connecting'}
            onTogglePalette={() => setPaletteOpen((open) => !open)}
            onSelectProject={() => void chooseProject()}
            onSend={(prompt) => void sendPrompt(prompt)}
            onInterrupt={() => void interrupt()}
          />
          <div className="workspace-meta">
            <span><GitBranch size={14} /> Local project</span>
            <span>Unofficial client · Inspired by Karol (@KarolCodes)</span>
          </div>
        </div>
      </section>

      {approval ? <ApprovalDialog request={approval} onDecision={(decision) => void decideApproval(decision)} /> : null}
    </main>
  )
}
