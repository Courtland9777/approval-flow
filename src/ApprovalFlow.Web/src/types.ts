export type Role = 'Employee' | 'Manager' | 'FinanceAdministrator'
export type RequestStatus =
  | 'Draft'
  | 'PendingManagerApproval'
  | 'PendingFinanceApproval'
  | 'Approved'
  | 'Rejected'
  | 'ReturnedForChanges'
  | 'Cancelled'
  | 'Completed'

export interface Session {
  userName: string
  roles: Role[]
}

export interface LineItem {
  id: string
  description: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface AuditEntry {
  id: string
  actor: string
  occurredAt: string
  fromStatus: RequestStatus
  toStatus: RequestStatus
  reason?: string
}

export interface PurchaseRequestSummary {
  id: string
  vendor: string
  category: string
  requester: string
  status: RequestStatus
  total: number
  requiresFinanceApproval: boolean
  requestedDeliveryDate: string
  lastModifiedAt: string
}

export interface PurchaseRequest extends PurchaseRequestSummary {
  costCenter: string
  businessJustification: string
  createdAt: string
  rowVersion: string
  lineItems: LineItem[]
  auditEntries: AuditEntry[]
}

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface RequestDraft {
  vendor: string
  costCenter: string
  category: string
  businessJustification: string
  requestedDeliveryDate: string
  lineItems: Array<{ description: string; quantity: number; unitPrice: number }>
}
