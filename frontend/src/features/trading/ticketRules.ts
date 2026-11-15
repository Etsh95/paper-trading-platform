import type { Instrument, OrderSide, OrderType } from '@/shared/api/types'
import { D, Decimal, feeFor, floorToStep, isPlainNumber, normalizeNumericInput } from '@/shared/lib/decimal'

export interface TicketInput {
  side: OrderSide
  type: OrderType
  quantity: string
  limitPrice: string
}

export interface TicketContext {
  instrument: Instrument
  bid?: string
  ask?: string
  availableCash: string
  /** Base asset quantity free to sell (held minus reserved). */
  availableQuantity: string
  feeRate: string
}

export type TicketField = 'quantity' | 'limitPrice' | 'form'

export interface TicketIssue {
  field: TicketField
  /** i18n key under `validation.` */
  code: string
  params?: Record<string, string>
}

export interface TicketEstimate {
  price: Decimal
  notional: Decimal
  fee: Decimal
  /** Buy: cash leaving the account. Sell: cash arriving. */
  total: Decimal
}

export interface TicketEvaluation {
  issues: TicketIssue[]
  estimate?: TicketEstimate
  /** Normalized values to send to the API (exact strings, never floats). */
  quantity?: string
  limitPrice?: string
}

function parse(raw: string): string | null {
  const value = normalizeNumericInput(raw)
  return isPlainNumber(value) ? value : null
}

/**
 * Pre-trade validation. Mirrors the server's rules so users get instant feedback, but the server stays the
 * authority: anything that slips through (a price moving, a race with another tab) is rejected there.
 */
export function evaluateTicket(input: TicketInput, ctx: TicketContext): TicketEvaluation {
  const { instrument } = ctx
  const issues: TicketIssue[] = []

  const quantity = parse(input.quantity)
  if (input.quantity.trim() === '') issues.push({ field: 'quantity', code: 'quantity_required' })
  else if (quantity === null || D(quantity).lte(0)) issues.push({ field: 'quantity', code: 'quantity_invalid' })
  else if (!D(quantity).mod(instrument.stepSize).isZero())
    issues.push({ field: 'quantity', code: 'quantity_step', params: { step: instrument.stepSize } })

  let limitPrice: string | null = null
  if (input.type === 'Limit') {
    limitPrice = parse(input.limitPrice)
    if (input.limitPrice.trim() === '') issues.push({ field: 'limitPrice', code: 'price_required' })
    else if (limitPrice === null || D(limitPrice).lte(0)) issues.push({ field: 'limitPrice', code: 'price_invalid' })
    else if (!D(limitPrice).mod(instrument.tickSize).isZero())
      issues.push({ field: 'limitPrice', code: 'price_tick', params: { tick: instrument.tickSize } })
  }

  const touch = input.side === 'Buy' ? ctx.ask : ctx.bid
  if (input.type === 'Market' && !touch) issues.push({ field: 'form', code: 'no_market_data' })

  const fieldsOk = !issues.some((i) => i.field !== 'form')
  const priceText = input.type === 'Limit' ? limitPrice : touch
  if (!fieldsOk || !quantity || !priceText) return { issues }

  const price = D(priceText)
  const notional = price.mul(quantity)
  const fee = feeFor(notional, ctx.feeRate)
  const total = input.side === 'Buy' ? notional.plus(fee) : notional.minus(fee)
  const estimate = { price, notional, fee, total }

  if (notional.lt(instrument.minNotional))
    issues.push({ field: 'form', code: 'min_notional', params: { min: instrument.minNotional, quote: instrument.quoteAsset } })
  else if (input.side === 'Buy' && total.gt(ctx.availableCash))
    issues.push({ field: 'form', code: 'insufficient_funds', params: { max: D(ctx.availableCash).toFixed(2, Decimal.ROUND_DOWN), quote: instrument.quoteAsset } })
  else if (input.side === 'Sell' && D(quantity).gt(ctx.availableQuantity))
    issues.push({ field: 'form', code: 'insufficient_position', params: { max: D(ctx.availableQuantity).toFixed(), asset: instrument.baseAsset } })

  return { issues, estimate, quantity, limitPrice: limitPrice ?? undefined }
}

/**
 * Quantity for a "use X% of what I can afford / hold" shortcut, floored to the step size so it is always valid.
 * For buys the fee is included so 100% never exceeds the available cash.
 */
export function quantityForFraction(fraction: number, side: OrderSide, price: string | undefined, ctx: TicketContext): string {
  const share = D(fraction)
  if (side === 'Sell') return floorToStep(D(ctx.availableQuantity).mul(share), ctx.instrument.stepSize).toFixed()
  if (!price || D(price).lte(0)) return ''
  const budget = D(ctx.availableCash).mul(share)
  const perUnit = D(price).mul(D(1).plus(ctx.feeRate))
  let qty = floorToStep(budget.div(perUnit), ctx.instrument.stepSize)
  // Fee rounding (ceil to 8 dp) can push the total a hair over; step down until it fits.
  while (qty.gt(0) && D(price).mul(qty).plus(feeFor(D(price).mul(qty), ctx.feeRate)).gt(budget))
    qty = qty.minus(ctx.instrument.stepSize)
  return qty.gt(0) ? qty.toFixed() : ''
}
