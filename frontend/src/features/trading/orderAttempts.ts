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

const fingerprintOf = (input: PlaceOrderInput) =>
  JSON.stringify([input.symbol, input.side, input.type, input.quantity, input.limitPrice ?? null])
