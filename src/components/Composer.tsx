import { ArrowUp, ChevronDown, Folder, Mic, Plus, Settings2, Square } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { SelectedModel } from '../types'

interface ComposerProps {
  projectPath: string | null
  selected: SelectedModel
  paletteOpen: boolean
  busy: boolean
  disabled?: boolean
  onTogglePalette: () => void
  onSelectProject: () => void
  onSend: (prompt: string) => void
  onInterrupt: () => void
}

const basename = (path: string) => path.split(/[\\/]/).filter(Boolean).at(-1) ?? path
const effortName = (effort: string) =>
  effort === 'xhigh'
    ? 'Extra High'
    : effort.replaceAll('_', ' ').replace(/\b\w/g, (value) => value.toUpperCase())

export function Composer({
  projectPath,
  selected,
  paletteOpen,
  busy,
  disabled,
  onTogglePalette,
  onSelectProject,
  onSend,
  onInterrupt,
}: ComposerProps) {
  const [prompt, setPrompt] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    const textarea = textareaRef.current
    if (!textarea) return
    textarea.style.height = 'auto'
    textarea.style.height = `${Math.min(textarea.scrollHeight, 150)}px`
  }, [prompt])

  const submit = () => {
    const value = prompt.trim()
    if (!value || busy || disabled) return
    setPrompt('')
    onSend(value)
  }

  return (
    <div className="composer-shell">
      <div className="project-bar">
        <button className="project-button" type="button" onClick={onSelectProject} title={projectPath ?? 'Choose a project'}>
          <Folder size={19} />
          <span>{projectPath ? basename(projectPath) : 'Choose project'}</span>
        </button>
        <span className="branch-label">main</span>
      </div>

      <textarea
        ref={textareaRef}
        value={prompt}
        placeholder="Do anything"
        aria-label="Message Codex"
        disabled={disabled}
        onChange={(event) => setPrompt(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault()
            submit()
          }
        }}
      />

      <div className="composer-toolbar">
        <div className="toolbar-left">
          <button className="icon-button" type="button" aria-label="Add attachment" disabled>
            <Plus size={21} />
          </button>
          <button className="custom-button" type="button" disabled>
            <Settings2 size={18} />
            Custom
          </button>
        </div>

        <div className="toolbar-right">
          <button
            className={`model-trigger ${paletteOpen ? 'open' : ''}`}
            type="button"
            onClick={onTogglePalette}
            aria-expanded={paletteOpen}
          >
            <strong>{selected.model.displayName}</strong>
            <span>{effortName(selected.effort)}</span>
            <ChevronDown size={17} />
          </button>
          <button className="icon-button microphone" type="button" aria-label="Voice input" disabled>
            <Mic size={21} />
          </button>
          {busy ? (
            <button className="send-button stop" type="button" onClick={onInterrupt} aria-label="Stop current turn">
              <Square size={17} fill="currentColor" />
            </button>
          ) : (
            <button
              className="send-button"
              type="button"
              onClick={submit}
              disabled={!prompt.trim() || disabled}
              aria-label="Send message"
            >
              <ArrowUp size={23} />
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
