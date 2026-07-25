import { describe, expect, it } from 'vitest'
import { tokenStore } from './auth'

describe('tokenStore', () => {
  it('stores the bearer token only in session storage', () => {
    tokenStore.set('demo-token')

    expect(sessionStorage.getItem('approvalflow.accessToken')).toBe('demo-token')
    expect(localStorage.getItem('approvalflow.accessToken')).toBeNull()

    tokenStore.clear()
    expect(tokenStore.get()).toBeNull()
  })
})
