import type { Order } from './types'

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

export async function toError(response: Response): Promise<ApiError> {
  let body: ProblemBody = {}
  try {
    body = (await response.json()) as ProblemBody
  } catch {
    // Not JSON (e.g. 429 from the rate limiter or a proxy error page).
  }
  const code = body.code ?? (response.status === 429 ? 'rate_limited' : 'unknown')
  return new ApiError(response.status, code, body.title ?? response.statusText, body.order)
}
