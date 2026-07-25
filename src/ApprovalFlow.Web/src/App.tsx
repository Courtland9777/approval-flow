import { FormEvent, useCallback, useEffect, useState } from 'react'
import {
  ApiError,
  createPurchaseRequest,
  getPurchaseRequest,
  getSession,
  listFinanceQueue,
  listManagerQueue,
  listMine,
  login,
  logout,
  revisePurchaseRequest,
  transitionPurchaseRequest,
} from './api'
import { tokenStore } from './auth'
import type {
  Page,
  PurchaseRequest,
  PurchaseRequestSummary,
  RequestDraft,
  RequestStatus,
  Role,
  Session,
} from './types'

const emptyDraft = (): RequestDraft => ({
  vendor: '',
  costCenter: '',
  category: 'Office',
  businessJustification: '',
  requestedDeliveryDate: '',
  lineItems: [{ description: '', quantity: 1, unitPrice: 0 }],
})

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const dateTime = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

function messageFrom(error: unknown) {
  return error instanceof Error ? error.message : 'Something went wrong.'
}

function Login({ onLogin }: { onLogin: (session: Session) => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setLoading(true)
    setError('')
    try {
      onLogin(await login(email, password))
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="login-shell">
      <section className="card login-card" aria-labelledby="login-heading">
        <p className="eyebrow">Local purchase approvals</p>
        <h1 id="login-heading">Sign in to ApprovalFlow</h1>
        <p>Use one of the seeded local demo accounts. Registration is intentionally unavailable.</p>
        {error && <div className="alert error" role="alert">{error}</div>}
        <form onSubmit={submit}>
          <label htmlFor="email">Email</label>
          <input
            id="email"
            name="email"
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
          <label htmlFor="password">Password</label>
          <input
            id="password"
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <button className="primary full" type="submit" disabled={loading}>
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </section>
    </main>
  )
}

function RequestForm({
  initial,
  submitLabel,
  onSubmit,
  onCancel,
  onReload,
}: {
  initial?: PurchaseRequest
  submitLabel: string
  onSubmit: (draft: RequestDraft, reason: string) => Promise<void>
  onCancel: () => void
  onReload?: () => void
}) {
  const [draft, setDraft] = useState<RequestDraft>(() => initial ? {
    vendor: initial.vendor,
    costCenter: initial.costCenter,
    category: initial.category,
    businessJustification: initial.businessJustification,
    requestedDeliveryDate: initial.requestedDeliveryDate,
    lineItems: initial.lineItems.map(({ description, quantity, unitPrice }) => ({
      description, quantity, unitPrice,
    })),
  } : emptyDraft())
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [stale, setStale] = useState(false)

  const updateItem = (index: number, field: 'description' | 'quantity' | 'unitPrice', value: string) => {
    setDraft((current) => ({
      ...current,
      lineItems: current.lineItems.map((item, itemIndex) => itemIndex === index
        ? { ...item, [field]: field === 'description' ? value : Number(value) }
        : item),
    }))
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (draft.lineItems.some((item) => !item.description || item.quantity < 1 || item.unitPrice <= 0)) {
      setError('Every line item needs a description, positive quantity, and positive unit price.')
      return
    }
    setSaving(true)
    setError('')
    setStale(false)
    try {
      await onSubmit(draft, reason)
    } catch (caught) {
      setError(messageFrom(caught))
      setStale(caught instanceof ApiError && caught.stale)
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="card" aria-labelledby="request-form-heading">
      <h2 id="request-form-heading">{initial ? 'Revise request' : 'New purchase request'}</h2>
      {error && <div className="alert error" role="alert">{error}</div>}
      {stale && onReload && (
        <button className="secondary" type="button" onClick={onReload}>Reload latest request</button>
      )}
      <form onSubmit={submit}>
        <div className="form-grid">
          <div>
            <label htmlFor="vendor">Vendor</label>
            <input id="vendor" required maxLength={200} value={draft.vendor}
              onChange={(event) => setDraft({ ...draft, vendor: event.target.value })} />
          </div>
          <div>
            <label htmlFor="cost-center">Cost center</label>
            <input id="cost-center" required maxLength={50} value={draft.costCenter}
              onChange={(event) => setDraft({ ...draft, costCenter: event.target.value })} />
          </div>
          <div>
            <label htmlFor="category">Category</label>
            <select id="category" value={draft.category}
              onChange={(event) => setDraft({ ...draft, category: event.target.value })}>
              <option>Office</option>
              <option>Software</option>
              <option>Security</option>
              <option>Equipment</option>
            </select>
          </div>
          <div>
            <label htmlFor="delivery-date">Requested delivery date</label>
            <input id="delivery-date" type="date" required value={draft.requestedDeliveryDate}
              onChange={(event) => setDraft({ ...draft, requestedDeliveryDate: event.target.value })} />
          </div>
        </div>
        <label htmlFor="justification">Business justification</label>
        <textarea id="justification" required minLength={10} maxLength={2000}
          value={draft.businessJustification}
          onChange={(event) => setDraft({ ...draft, businessJustification: event.target.value })} />
        <fieldset>
          <legend>Line items</legend>
          {draft.lineItems.map((item, index) => (
            <div className="line-item" key={index}>
              <div>
                <label htmlFor={`description-${index}`}>Description</label>
                <input id={`description-${index}`} required value={item.description}
                  onChange={(event) => updateItem(index, 'description', event.target.value)} />
              </div>
              <div>
                <label htmlFor={`quantity-${index}`}>Quantity</label>
                <input id={`quantity-${index}`} type="number" min="1" step="1" required value={item.quantity}
                  onChange={(event) => updateItem(index, 'quantity', event.target.value)} />
              </div>
              <div>
                <label htmlFor={`price-${index}`}>Unit price</label>
                <input id={`price-${index}`} type="number" min="0.01" step="0.01" required value={item.unitPrice}
                  onChange={(event) => updateItem(index, 'unitPrice', event.target.value)} />
              </div>
              {draft.lineItems.length > 1 && (
                <button type="button" className="text danger"
                  onClick={() => setDraft({ ...draft, lineItems: draft.lineItems.filter((_, i) => i !== index) })}>
                  Remove
                </button>
              )}
            </div>
          ))}
          <button type="button" className="secondary"
            onClick={() => setDraft({
              ...draft,
              lineItems: [...draft.lineItems, { description: '', quantity: 1, unitPrice: 0 }],
            })}>
            Add line item
          </button>
        </fieldset>
        {initial && (
          <>
            <label htmlFor="revision-reason">Revision note</label>
            <input id="revision-reason" maxLength={1000} value={reason}
              onChange={(event) => setReason(event.target.value)} />
          </>
        )}
        <div className="actions">
          <button className="primary" type="submit" disabled={saving}>
            {saving ? 'Saving…' : submitLabel}
          </button>
          <button className="secondary" type="button" onClick={onCancel}>Cancel</button>
        </div>
      </form>
    </section>
  )
}

function RequestTable({
  page,
  loading,
  onSelect,
  onPage,
}: {
  page?: Page<PurchaseRequestSummary>
  loading: boolean
  onSelect: (id: string) => void
  onPage: (page: number) => void
}) {
  if (loading) return <p aria-live="polite">Loading requests…</p>
  if (!page?.items.length) return <p className="empty">No requests match this view.</p>
  return (
    <>
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>Vendor</th><th>Requester</th><th>Status</th><th>Total</th><th><span className="sr-only">Open</span></th></tr>
          </thead>
          <tbody>
            {page.items.map((request) => (
              <tr key={request.id}>
                <td>{request.vendor}<small>{request.category}</small></td>
                <td>{request.requester}</td>
                <td><Status value={request.status} /></td>
                <td>{money.format(request.total)}</td>
                <td><button className="text" onClick={() => onSelect(request.id)}>View</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <nav className="pagination" aria-label="Request pages">
        <button className="secondary" disabled={page.page <= 1} onClick={() => onPage(page.page - 1)}>Previous</button>
        <span>Page {page.page} · {page.totalCount} total</span>
        <button className="secondary" disabled={page.page * page.pageSize >= page.totalCount}
          onClick={() => onPage(page.page + 1)}>Next</button>
      </nav>
    </>
  )
}

function Status({ value }: { value: RequestStatus }) {
  return <span className={`status status-${value.toLowerCase()}`}>{value.replace(/([A-Z])/g, ' $1').trim()}</span>
}

function RequestDetail({
  request,
  role,
  onChanged,
  onReload,
  onRevise,
}: {
  request: PurchaseRequest
  role: Role
  onChanged: (request: PurchaseRequest) => void
  onReload: () => void
  onRevise: () => void
}) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [stale, setStale] = useState(false)

  const act = async (action: 'submit' | 'approve' | 'reject' | 'return') => {
    if ((action === 'reject' || action === 'return') && !reason.trim()) {
      setError('A reason is required to reject or return a request.')
      return
    }
    setBusy(action)
    setError('')
    setStale(false)
    try {
      onChanged(await transitionPurchaseRequest(request.id, action, request.rowVersion, reason))
      setReason('')
    } catch (caught) {
      setError(messageFrom(caught))
      setStale(caught instanceof ApiError && caught.stale)
    } finally {
      setBusy('')
    }
  }

  const canSubmit = role === 'Employee' && request.status === 'Draft'
  const canRevise = role === 'Employee' && request.status === 'ReturnedForChanges'
  const canReview =
    (role === 'Manager' && request.status === 'PendingManagerApproval') ||
    (role === 'FinanceAdministrator' && request.status === 'PendingFinanceApproval')

  return (
    <section className="card detail" aria-labelledby="request-detail-heading">
      <div className="heading-row">
        <div>
          <p className="eyebrow">Purchase request</p>
          <h2 id="request-detail-heading">{request.vendor}</h2>
        </div>
        <Status value={request.status} />
      </div>
      {error && (
        <div className="alert error" role="alert">
          {error}
          {stale && <button className="secondary inline" onClick={onReload}>Reload latest request</button>}
        </div>
      )}
      <dl className="facts">
        <div><dt>Requester</dt><dd>{request.requester}</dd></div>
        <div><dt>Total</dt><dd>{money.format(request.total)}</dd></div>
        <div><dt>Cost center</dt><dd>{request.costCenter}</dd></div>
        <div><dt>Category</dt><dd>{request.category}</dd></div>
        <div><dt>Delivery</dt><dd>{request.requestedDeliveryDate}</dd></div>
        <div><dt>Finance review</dt><dd>{request.requiresFinanceApproval ? 'Required' : 'Not required'}</dd></div>
      </dl>
      <h3>Business justification</h3>
      <p>{request.businessJustification}</p>
      <h3>Line items</h3>
      <ul className="items">
        {request.lineItems.map((item) => (
          <li key={item.id}><span>{item.description} × {item.quantity}</span><strong>{money.format(item.lineTotal)}</strong></li>
        ))}
      </ul>
      {(canSubmit || canRevise || canReview) && (
        <div className="decision-panel">
          {canReview && (
            <>
              <label htmlFor="decision-reason">Decision reason <span className="hint">(required for reject/return)</span></label>
              <textarea id="decision-reason" maxLength={1000} value={reason}
                onChange={(event) => setReason(event.target.value)} />
              <div className="actions">
                <button className="primary" disabled={!!busy} onClick={() => act('approve')}>Approve</button>
                <button className="secondary" disabled={!!busy} onClick={() => act('return')}>Return for changes</button>
                <button className="danger-button" disabled={!!busy} onClick={() => act('reject')}>Reject</button>
              </div>
            </>
          )}
          {canSubmit && <button className="primary" disabled={!!busy} onClick={() => act('submit')}>Submit for approval</button>}
          {canRevise && <button className="primary" onClick={onRevise}>Revise request</button>}
        </div>
      )}
      <h3>Audit history</h3>
      {request.auditEntries.length ? (
        <ol className="timeline">
          {request.auditEntries.map((entry) => (
            <li key={entry.id}>
              <strong>{entry.fromStatus} → {entry.toStatus}</strong>
              <span>{entry.actor} · {dateTime.format(new Date(entry.occurredAt))}</span>
              {entry.reason && <p>{entry.reason}</p>}
            </li>
          ))}
        </ol>
      ) : <p className="empty">No transitions recorded yet.</p>}
    </section>
  )
}

function Workspace({ session, onLogout }: { session: Session; onLogout: () => void }) {
  const availableRoles = session.roles.filter((role) =>
    ['Employee', 'Manager', 'FinanceAdministrator'].includes(role))
  const [role, setRole] = useState<Role>(availableRoles[0])
  const [pageNumber, setPageNumber] = useState(1)
  const [status, setStatus] = useState<RequestStatus | ''>('')
  const [page, setPage] = useState<Page<PurchaseRequestSummary>>()
  const [selected, setSelected] = useState<PurchaseRequest>()
  const [editing, setEditing] = useState(false)
  const [creating, setCreating] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const loadList = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const result = role === 'Employee'
        ? await listMine(pageNumber, status || undefined)
        : role === 'Manager'
          ? await listManagerQueue(pageNumber)
          : await listFinanceQueue(pageNumber)
      setPage(result)
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setLoading(false)
    }
  }, [pageNumber, role, status])

  useEffect(() => { void loadList() }, [loadList])

  const open = async (id: string) => {
    setError('')
    try {
      setSelected(await getPurchaseRequest(id))
      setCreating(false)
      setEditing(false)
    } catch (caught) {
      setError(messageFrom(caught))
    }
  }

  const reloadSelected = async () => {
    if (selected) await open(selected.id)
  }

  const changed = (request: PurchaseRequest) => {
    setSelected(request)
    void loadList()
  }

  const changeRole = (next: Role) => {
    setRole(next)
    setPageNumber(1)
    setStatus('')
    setSelected(undefined)
    setCreating(false)
    setEditing(false)
  }

  return (
    <>
      <header className="app-header">
        <div>
          <a className="brand" href="/">ApprovalFlow</a>
          <span className="identity">{session.userName}</span>
        </div>
        <button className="secondary" onClick={onLogout}>Log out</button>
      </header>
      <main className="app-shell">
        <aside className="sidebar">
          <p className="eyebrow">Working as</p>
          <div className="role-switcher" role="group" aria-label="Active role">
            {availableRoles.map((availableRole) => (
              <button key={availableRole}
                aria-pressed={role === availableRole}
                onClick={() => changeRole(availableRole)}>
                {availableRole === 'FinanceAdministrator' ? 'Finance' : availableRole}
              </button>
            ))}
          </div>
          <p className="sidebar-note">
            {role === 'Employee' ? 'Your purchase requests'
              : role === 'Manager' ? 'Pending manager review'
                : 'Pending finance review'}
          </p>
        </aside>
        <div className="content">
          <div className="heading-row">
            <div>
              <p className="eyebrow">{role === 'Employee' ? 'Employee workspace' : `${role === 'Manager' ? 'Manager' : 'Finance'} queue`}</p>
              <h1>{role === 'Employee' ? 'Purchase requests' : 'Requests awaiting review'}</h1>
            </div>
            {role === 'Employee' && (
              <button className="primary" onClick={() => {
                setCreating(true); setEditing(false); setSelected(undefined)
              }}>New request</button>
            )}
          </div>
          {error && <div className="alert error" role="alert">{error}</div>}
          {creating && (
            <RequestForm submitLabel="Create draft" onCancel={() => setCreating(false)}
              onSubmit={async (draft) => {
                const created = await createPurchaseRequest(draft)
                setCreating(false)
                setSelected(created)
                await loadList()
              }} />
          )}
          {editing && selected && (
            <RequestForm initial={selected} submitLabel="Save revision" onCancel={() => setEditing(false)}
              onReload={async () => { await reloadSelected(); setEditing(false) }}
              onSubmit={async (draft, reason) => {
                const revised = await revisePurchaseRequest(selected.id, draft, selected.rowVersion, reason)
                setEditing(false)
                changed(revised)
              }} />
          )}
          {!creating && !editing && (
            <section className="card">
              {role === 'Employee' && (
                <div className="filters">
                  <label htmlFor="status-filter">Status</label>
                  <select id="status-filter" value={status} onChange={(event) => {
                    setStatus(event.target.value as RequestStatus | '')
                    setPageNumber(1)
                  }}>
                    <option value="">All statuses</option>
                    {['Draft', 'PendingManagerApproval', 'PendingFinanceApproval', 'Approved', 'Rejected', 'ReturnedForChanges']
                      .map((value) => <option key={value} value={value}>{value}</option>)}
                  </select>
                </div>
              )}
              <RequestTable page={page} loading={loading} onSelect={open} onPage={setPageNumber} />
            </section>
          )}
          {!creating && !editing && selected && (
            <RequestDetail request={selected} role={role} onChanged={changed}
              onReload={reloadSelected} onRevise={() => setEditing(true)} />
          )}
        </div>
      </main>
    </>
  )
}

export default function App() {
  const [session, setSession] = useState<Session>()
  const [checking, setChecking] = useState(tokenStore.get() !== null)

  useEffect(() => {
    if (!tokenStore.get()) return
    getSession()
      .then(setSession)
      .catch(() => logout())
      .finally(() => setChecking(false))
  }, [])

  if (checking) return <main className="center" aria-live="polite">Restoring session…</main>
  if (!session) return <Login onLogin={setSession} />
  return <Workspace session={session} onLogout={() => { logout(); setSession(undefined) }} />
}
