import { ShieldAlert, Terminal } from 'lucide-react'
import type { ApprovalRequest } from '../types'

interface ApprovalDialogProps {
  request: ApprovalRequest
  onDecision: (decision: 'accept' | 'acceptForSession' | 'decline' | 'cancel') => void
}

export function ApprovalDialog({ request, onDecision }: ApprovalDialogProps) {
  const params = request.params
  const command = typeof params.command === 'string' ? params.command : null
  const reason = typeof params.reason === 'string' ? params.reason : null
  const cwd = typeof params.cwd === 'string' ? params.cwd : null
  const isFileChange = request.method.includes('fileChange')

  return (
    <div className="modal-backdrop" role="presentation">
      <section className="approval-dialog" role="dialog" aria-modal="true" aria-labelledby="approval-title">
        <div className="approval-icon">
          {isFileChange ? <ShieldAlert size={24} /> : <Terminal size={24} />}
        </div>
        <div>
          <h2 id="approval-title">Approval required</h2>
          <p>{reason ?? (isFileChange ? 'Codex wants to modify files in this project.' : 'Codex wants to run a command.')}</p>
          {command ? <pre>{command}</pre> : null}
          {cwd ? <small>{cwd}</small> : null}
        </div>
        <div className="approval-actions">
          <button className="secondary-button" type="button" onClick={() => onDecision('decline')}>
            Decline
          </button>
          <button className="secondary-button" type="button" onClick={() => onDecision('acceptForSession')}>
            Allow for session
          </button>
          <button className="primary-button" type="button" onClick={() => onDecision('accept')}>
            Allow once
          </button>
        </div>
      </section>
    </div>
  )
}
