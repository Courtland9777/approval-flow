import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getPurchaseRequest } from './api'
import { tokenStore } from './auth'

describe('API errors', () => {
  beforeEach(() => {
    tokenStore.set('session-token')
  })

  it('marks a 409 Problem Details response as stale and sends the bearer token', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      title: 'Concurrency conflict',
      detail: 'The request changed after it was read. Reload it.',
    }), {
      status: 409,
      headers: { 'Content-Type': 'application/problem+json' },
    }))

    await expect(getPurchaseRequest('request-id')).rejects.toMatchObject({
      status: 409,
      stale: true,
      message: 'Concurrency conflict: The request changed after it was read. Reload it.',
    })
    expect(fetchMock).toHaveBeenCalledWith('/api/purchase-requests/request-id', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer session-token' }),
    }))
  })
})
