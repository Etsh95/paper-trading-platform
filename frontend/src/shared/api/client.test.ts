import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

/**
 * The API keeps accounts in memory, so after a server restart every token is rejected (401). The client then
 * creates one new session. A 401 that belongs to an *old* token must not create yet another one.
 */
describe('api client session recovery', () => {
  let sessionsCreated = 0
  let validToken = ''

  beforeEach(() => {
    vi.resetModules() // fresh session state per test
    try {
      localStorage.removeItem('paperdesk.session')
    } catch {
      // storage unavailable in this environment; the session module tolerates that too
    }
    sessionsCreated = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        if (url.endsWith('/api/sessions')) {
          sessionsCreated++
          validToken = `token-${sessionsCreated}`
          return json({ accountId: `acc-${sessionsCreated}`, accessToken: validToken, expiresAt: '2099-01-01T00:00:00Z', initialCash: '100000' })
        }
        const auth = new Headers(init?.headers).get('Authorization')
        return auth === `Bearer ${validToken}` ? json({ ok: true }) : new Response(null, { status: 401 })
      }),
    )
  })

  afterEach(() => vi.unstubAllGlobals())

  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

  it('renews the session once when the server rejects the current token', async () => {
    const { api } = await import('./client')
    await api.portfolio() // creates token-1
    validToken = 'server-restarted' // every existing token is now invalid

    await expect(api.portfolio()).resolves.toBeDefined()
    expect(sessionsCreated).toBe(2)
  })

  it('does not create another session for a late 401 from an already replaced token', async () => {
    const { api } = await import('./client')
    await api.portfolio() // token-1
    validToken = 'server-restarted'

    // A slow request is sent with token-1 and its 401 arrives only after another request has already
    // recovered to token-2. That late 401 belongs to the old token and must not create a third session.
    let releaseSlow!: () => void
    const slowGate = new Promise<void>((resolve) => (releaseSlow = resolve))
    const realFetch = fetch as unknown as (url: string, init?: RequestInit) => Promise<Response>
    vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
      if (url.endsWith('/api/trades') && new Headers(init?.headers).get('Authorization') === 'Bearer token-1') {
        await slowGate
        return new Response(null, { status: 401 })
      }
      return realFetch(url, init)
    })

    const slow = api.trades() // in flight with token-1
    await api.portfolio() // 401 -> recovers to token-2
    releaseSlow() // now the stale 401 arrives
    await slow

    expect(sessionsCreated).toBe(2)
  })

  it('a failed "start over" is not later mistaken for the first-ever session', async () => {
    const { api } = await import('./client')
    const session = await import('./session')
    const reasons: string[] = []
    session.onSessionChange((_, reason) => reasons.push(reason))
    await api.portfolio() // initial session

    const realFetch = fetch as unknown as (url: string, init?: RequestInit) => Promise<Response>
    let failNext = true
    vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
      if (url.endsWith('/api/sessions') && failNext) {
        failNext = false
        return new Response(null, { status: 429 })
      }
      return realFetch(url, init)
    })

    await expect(session.renewSession('created')).rejects.toThrow() // e.g. rate limited
    await api.portfolio() // creates the new session lazily

    expect(reasons).toEqual(['initial', 'created'])
  })
})
