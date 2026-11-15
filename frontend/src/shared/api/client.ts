import { ApiError, toError } from './errors'
import { currentToken, ensureSession, renewSession } from './session'
import type {
  Instrument,
  Order,
  OrderStatus,
  PlaceOrderInput,
  PlaceOrderResult,
  Portfolio,
  QuotesResponse,
  Trade,
  TradingConfig,
} from './types'
import { apiUrl } from './url'

export { ApiError }

async function send(path: string, init: RequestInit = {}, authenticated = true, retried = false): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  let tokenUsed: string | null = null
  if (authenticated) {
    await ensureSession()
    tokenUsed = currentToken()
    headers.set('Authorization', `Bearer ${tokenUsed}`)
  }

  let response: Response
  try {
    response = await fetch(apiUrl(path), { ...init, headers })
  } catch {
    throw new ApiError(0, 'network', 'Network error')
  }

  // The server keeps accounts in memory; after a restart our token is no longer valid. Start fresh once.
  // If another request already replaced the session while this one was in flight, the 401 is for the old
  // token: just retry with the current one instead of creating yet another account.
  if (response.status === 401 && authenticated && !retried) {
    if (currentToken() === tokenUsed) await renewSession('reset')
    return send(path, init, authenticated, true)
  }

  if (!response.ok) throw await toError(response)
  return response
}

async function json<T>(path: string, init?: RequestInit, authenticated = true): Promise<T> {
  return (await (await send(path, init, authenticated)).json()) as T
}

export const api = {
  config: () => json<TradingConfig>('/api/config', undefined, false),
  instruments: () => json<Instrument[]>('/api/instruments', undefined, false),
  quotes: () => json<QuotesResponse>('/api/quotes', undefined, false),
  portfolio: () => json<Portfolio>('/api/portfolio'),
  orders: (status?: OrderStatus) => json<Order[]>(`/api/orders${status ? `?status=${status}` : ''}`),
  trades: () => json<Trade[]>('/api/trades'),

  /**
   * Places an order. The idempotency key is created once per user action by the caller and reused if this
   * call retries after a network failure, so a flaky connection can never buy twice.
   */
  async placeOrder(input: PlaceOrderInput, idempotencyKey: string): Promise<PlaceOrderResult> {
    const init: RequestInit = {
      method: 'POST',
      body: JSON.stringify(input),
      headers: { 'Idempotency-Key': idempotencyKey },
    }
    let response: Response
    try {
      response = await send('/api/orders', init)
    } catch (error) {
      if (!(error instanceof ApiError) || error.code !== 'network') throw error
      await new Promise((r) => setTimeout(r, 800))
      response = await send('/api/orders', init) // same key: replays if the first attempt actually landed
    }
    return {
      order: (await response.json()) as Order,
      replayed: response.headers.get('Idempotent-Replayed') === 'true',
    }
  },

  cancelOrder: (id: string) => json<Order>(`/api/orders/${id}`, { method: 'DELETE' }),
}
