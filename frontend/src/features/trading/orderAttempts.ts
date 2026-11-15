import { ApiError } from '@/shared/api/client'
import type { PlaceOrderInput } from '@/shared/api/types'

/**
 * Chooses the idempotency key for each order submission.
 *
 * The API client already retries once with the same key. If that also fails at the network level, the order may
 * still have reached the server, so its outcome is unknown. Resubmitting the *same* order then reuses the same key,
 * and the server replays the original result instead of filling a second order. Once the server gives a definitive
 * answer (accepted or rejected), the next submission is a new order with a new key.
 */
export class OrderAttempts {
  private pending: { fingerprint: string; key: string } | null = null
  private readonly newKey: () => string

  constructor(newKey: () => string = () => crypto.randomUUID()) {
    this.newKey = newKey
  }

  keyFor(input: PlaceOrderInput): string {
    const fingerprint = fingerprintOf(input)
    if (this.pending?.fingerprint === fingerprint) return this.pending.key
    this.pending = { fingerprint, key: this.newKey() }
    return this.pending.key
  }

  /** Outcome known: forget the key so the next submission is treated as a new order. */
  settle(): void {
    this.pending = null
  }
}

/**
 * True when the order may or may not have reached the engine: the network failed, or a gateway timed out or was
 * unavailable (502/503/504) after possibly forwarding it. Only then may the key be reused for a resubmission.
 */
export function isOutcomeUnknown(error: unknown): boolean {
  if (!(error instanceof ApiError)) return false
  return error.code === 'network' || [502, 503, 504].includes(error.status)
}

/** One instance per page load, outside React state, so a pending key survives navigating between pages. */
export const orderAttempts = new OrderAttempts()

const fingerprintOf = (input: PlaceOrderInput) =>
  JSON.stringify([input.symbol, input.side, input.type, input.quantity, input.limitPrice ?? null])
