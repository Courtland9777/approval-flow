import { tokenStore } from './auth'
import type {
  Page,
  PurchaseRequest,
  PurchaseRequestSummary,
  RequestDraft,
  RequestStatus,
  Session,
} from './types'

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly stale = false,
  ) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = tokenStore.get()
  const response = await fetch(path, {
    ...init,
    headers: {
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  })

  if (!response.ok) {
    let message = `Request failed (${response.status}).`
    try {
      const problem = (await response.json()) as {
        title?: string
        detail?: string
        errors?: Record<string, string[]>
      }
      const validation = problem.errors
        ? Object.values(problem.errors).flat().join(' ')
        : ''
      message = [problem.title, problem.detail, validation].filter(Boolean).join(': ')
    } catch {
      // Keep the status-based fallback for non-JSON responses.
    }
    throw new ApiError(
      message || `Request failed (${response.status}).`,
      response.status,
      response.status === 409,
    )
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export async function login(email: string, password: string): Promise<Session> {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  if (!response.ok) throw new ApiError('Login failed. Check your email and password.', response.status)
  const payload = (await response.json()) as { accessToken: string }
  tokenStore.set(payload.accessToken)
  try {
    return await getSession()
  } catch (error) {
    tokenStore.clear()
    throw error
  }
}

export const getSession = () => request<Session>('/api/auth/session')

export function logout() {
  tokenStore.clear()
}

const listQuery = (page: number, status?: string) => {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: '10',
    sort: 'LastModifiedDesc',
  })
  if (status) params.set('status', status)
  return params
}

export const listMine = (page: number, status?: RequestStatus) =>
  request<Page<PurchaseRequestSummary>>(`/api/purchase-requests/mine?${listQuery(page, status)}`)

export const listManagerQueue = (page: number) =>
  request<Page<PurchaseRequestSummary>>(`/api/purchase-requests/manager-queue?${listQuery(page)}`)

export const listFinanceQueue = (page: number) =>
  request<Page<PurchaseRequestSummary>>(`/api/purchase-requests/finance-queue?${listQuery(page)}`)

export const getPurchaseRequest = (id: string) =>
  request<PurchaseRequest>(`/api/purchase-requests/${id}`)

export const createPurchaseRequest = (draft: RequestDraft) =>
  request<PurchaseRequest>('/api/purchase-requests', {
    method: 'POST',
    body: JSON.stringify(draft),
  })

export const revisePurchaseRequest = (
  id: string,
  draft: RequestDraft,
  rowVersion: string,
  reason: string,
) =>
  request<PurchaseRequest>(`/api/purchase-requests/${id}`, {
    method: 'PUT',
    body: JSON.stringify({ ...draft, rowVersion, reason }),
  })

export const transitionPurchaseRequest = (
  id: string,
  action: 'submit' | 'approve' | 'reject' | 'return',
  rowVersion: string,
  reason: string,
) =>
  request<PurchaseRequest>(`/api/purchase-requests/${id}/${action}`, {
    method: 'POST',
    body: JSON.stringify({ rowVersion, reason }),
  })
