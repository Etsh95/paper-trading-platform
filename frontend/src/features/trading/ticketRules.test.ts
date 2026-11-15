import { describe, expect, it } from 'vitest'
import type { Instrument } from '@/shared/api/types'
import { D, feeFor } from '@/shared/lib/decimal'
import { evaluateTicket, quantityForFraction, type TicketContext, type TicketInput } from './ticketRules'

const btc: Instrument = { symbol: 'BTCUSDT', baseAsset: 'BTC', quoteAsset: 'USDT', tickSize: '0.01', stepSize: '0.00001', minNotional: '5' }

const ctx = (over: Partial<TicketContext> = {}): TicketContext => ({
  instrument: btc,
  bid: '50000',
  ask: '50010',
  availableCash: '100000',
  availableQuantity: '0.5',
  feeRate: '0.001',
  ...over,
})

const input = (over: Partial<TicketInput> = {}): TicketInput => ({ side: 'Buy', type: 'Market', quantity: '0.1', limitPrice: '', ...over })

const codes = (r: ReturnType<typeof evaluateTicket>) => r.issues.map((i) => i.code)

describe('evaluateTicket', () => {
  it('estimates a market buy at the ask with the fee included, exactly', () => {
    const r = evaluateTicket(input(), ctx())
    expect(r.issues).toEqual([])
    expect(r.estimate?.price.toFixed()).toBe('50010')
    expect(r.estimate?.notional.toFixed()).toBe('5001')
    expect(r.estimate?.fee.toFixed()).toBe('5.001')
    expect(r.estimate?.total.toFixed()).toBe('5006.001')
  })

  it('estimates a market sell at the bid with the fee deducted', () => {
    const r = evaluateTicket(input({ side: 'Sell' }), ctx())
    expect(r.estimate?.total.toFixed()).toBe(D('5000').minus('5').toFixed())
  })

  it('uses the limit price for limit orders', () => {
    const r = evaluateTicket(input({ type: 'Limit', limitPrice: '49000' }), ctx())
    expect(r.estimate?.price.toFixed()).toBe('49000')
    expect(r.limitPrice).toBe('49000')
  })

  it.each([
    ['', 'quantity_required'],
    ['abc', 'quantity_invalid'],
    ['-1', 'quantity_invalid'],
    ['0', 'quantity_invalid'],
    ['1e3', 'quantity_invalid'],
    ['0.000001', 'quantity_step'],
  ])('rejects quantity %j with %s', (quantity, code) => {
    expect(codes(evaluateTicket(input({ quantity }), ctx()))).toContain(code)
  })

  it.each([
    ['', 'price_required'],
    ['0', 'price_invalid'],
    ['49000.001', 'price_tick'],
  ])('rejects limit price %j with %s', (limitPrice, code) => {
    expect(codes(evaluateTicket(input({ type: 'Limit', limitPrice }), ctx()))).toContain(code)
  })

  it('accepts Arabic-Indic digits and the Arabic decimal separator', () => {
    const r = evaluateTicket(input({ quantity: '٠٫١' }), ctx())
    expect(r.issues).toEqual([])
    expect(r.quantity).toBe('0.1')
  })

  it('blocks buys that cost more than available cash including the fee', () => {
    // 2 BTC at 50,010 = 100,020 > 100,000
    expect(codes(evaluateTicket(input({ quantity: '2' }), ctx()))).toEqual(['insufficient_funds'])
    // Exactly affordable notional, but the fee pushes it over.
    expect(codes(evaluateTicket(input({ quantity: '1' }), ctx({ availableCash: '50010' })))).toEqual(['insufficient_funds'])
  })

  it('blocks sells larger than the available position', () => {
    expect(codes(evaluateTicket(input({ side: 'Sell', quantity: '0.6' }), ctx()))).toEqual(['insufficient_position'])
  })

  it('enforces the minimum order value', () => {
    expect(codes(evaluateTicket(input({ quantity: '0.00001' }), ctx()))).toEqual(['min_notional'])
  })

  it('exposes a valid limit price even before a quantity is entered (used to size "use 100%")', () => {
    expect(evaluateTicket(input({ type: 'Limit', quantity: '', limitPrice: '49000' }), ctx()).limitPrice).toBe('49000')
    expect(evaluateTicket(input({ type: 'Limit', quantity: '', limitPrice: '49000.001' }), ctx()).limitPrice).toBeUndefined()
  })

  it('reports missing market data for market orders only', () => {
    expect(codes(evaluateTicket(input(), ctx({ ask: undefined, bid: undefined })))).toEqual(['no_market_data'])
    expect(codes(evaluateTicket(input({ type: 'Limit', limitPrice: '49000' }), ctx({ ask: undefined, bid: undefined })))).toEqual([])
  })
})

describe('quantityForFraction', () => {
  it('never exceeds available cash at 100%, fee included', () => {
    const c = ctx({ availableCash: '12345.67' })
    const qty = quantityForFraction(1, 'Buy', '50010', c)
    const notional = D('50010').mul(qty)
    expect(notional.plus(feeFor(notional, c.feeRate)).lte('12345.67')).toBe(true)
    expect(D(qty).mod(btc.stepSize).isZero()).toBe(true)
    // ...and one more step would not fit
    const next = D(qty).plus(btc.stepSize)
    const nextNotional = D('50010').mul(next)
    expect(nextNotional.plus(feeFor(nextNotional, c.feeRate)).gt('12345.67')).toBe(true)
  })

  it('sells a share of the available quantity, floored to the step', () => {
    expect(quantityForFraction(0.5, 'Sell', undefined, ctx({ availableQuantity: '0.33333' }))).toBe('0.16666')
  })

  it('returns empty when there is no price to size a buy', () => {
    expect(quantityForFraction(0.5, 'Buy', undefined, ctx())).toBe('')
  })
})
