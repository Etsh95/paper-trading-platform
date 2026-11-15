import { describe, expect, it } from 'vitest'
import type { PlaceOrderInput } from '@/shared/api/types'
import { OrderAttempts } from './orderAttempts'

const order: PlaceOrderInput = { symbol: 'BTCUSDT', side: 'Buy', type: 'Market', quantity: '0.1' }

describe('OrderAttempts', () => {
  const sequentialKeys = () => {
    let n = 0
    return new OrderAttempts(() => `key-${++n}`)
  }

  it('reuses the key when the same order is resubmitted after an unknown outcome', () => {
    const attempts = sequentialKeys()
    const first = attempts.keyFor(order)
    // network failure: not settled
    expect(attempts.keyFor({ ...order })).toBe(first)
  })

  it('uses a new key once the previous outcome is known', () => {
    const attempts = sequentialKeys()
    const first = attempts.keyFor(order)
    attempts.settle()
    expect(attempts.keyFor(order)).not.toBe(first)
  })

  it('treats a changed order as a new order', () => {
    const attempts = sequentialKeys()
    const first = attempts.keyFor(order)
    expect(attempts.keyFor({ ...order, quantity: '0.2' })).not.toBe(first)
  })
})
