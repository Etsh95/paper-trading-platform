import { describe, expect, it } from 'vitest'
import { quoteStore } from './quoteStore'

const quote = (bid: string, at: string) => ({ symbol: 'BTCUSDT', bid, ask: bid, mid: bid, timestamp: at })

describe('quoteStore', () => {
  it('never lets an older quote (e.g. a slow REST snapshot) replace a newer pushed one', () => {
    quoteStore.apply([quote('101', '2026-01-01T00:00:02.000Z')]) // from the hub
    quoteStore.apply([quote('100', '2026-01-01T00:00:01.000Z')]) // late snapshot

    expect(quoteStore.get().quotes.get('BTCUSDT')?.bid).toBe('101')
  })

  it('applies newer quotes and records the direction of the move', () => {
    quoteStore.apply([quote('102', '2026-01-01T00:00:03.000Z')])

    const live = quoteStore.get().quotes.get('BTCUSDT')
    expect(live?.bid).toBe('102')
    expect(live?.direction).toBe('up')
  })

  it('ignores feed status from a snapshot older than what the live stream reported', () => {
    const before = quoteStore.get().status
    quoteStore.setStatus({ source: 'Mock', connected: false, lastQuoteAt: '2026-01-01T00:00:00.500Z' })

    expect(quoteStore.get().status).toEqual(before)
  })
})
