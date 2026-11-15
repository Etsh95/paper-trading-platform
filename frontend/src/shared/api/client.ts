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

/** A failed API call. `code` is the stable machine code from the server's problem details. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly order?: Order

  constructor(status: number, code: string, message: string, order?: Order) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.order = order
  }
}

interface ProblemBody {
  title?: string
  code?: string
  order?: Order
}

async function toError(response: Response): Promise<ApiError> {
  let body: ProblemBody = {}
  try {
    body = (await response.json()) as ProblemBody
  } catch {
    // Not JSON (e.g. 429 from the rate limiter or a proxy error page).
  }
  const code = body.code ?? (response.status === 429 ? 'rate_limited' : 'unknown')
  return new ApiError(response.status, code, body.title ?? response.statusText, body.order)
}

async function send(path: string, init: RequestInit = {}, authenticated = true, retried = false): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  if (authenticated) {
    await ensureSession()
    headers.set('Authorization', `Bearer ${currentToken()}`)
  }

  let response: Response
  try {
    response = await fetch(apiUrl(path), { ...init, headers })
  } catch {
    throw new ApiError(0, 'network', 'Network error')
  }

  // The server keeps accounts in memory; after a restart our token is no longer valid. Start fresh once.
  if (response.status === 401 && authenticated && !retried) {
    await renewSession('reset')
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
