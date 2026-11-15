import Decimal from 'decimal.js'

// Enough headroom for price x quantity x fee-rate products without rounding.
Decimal.set({ precision: 40, rounding: Decimal.ROUND_HALF_EVEN })

export { Decimal }

export const D = (value: Decimal.Value | null | undefined): Decimal => new Decimal(value ?? 0)

/** Number of decimal places implied by a tick or step size ("0.00001" -> 5). */
export const placesOf = (increment: string): number => D(increment).decimalPlaces()

/** Mirrors the server's fee rule: notional x rate, rounded *up* to 8 dp. */
export const feeFor = (notional: Decimal, feeRate: string): Decimal =>
  notional.mul(feeRate).toDecimalPlaces(8, Decimal.ROUND_UP)

/** Largest multiple of `step` that is <= value. */
export const floorToStep = (value: Decimal, step: string): Decimal => {
  const s = D(step)
  return value.div(s).floor().mul(s)
}

/** Accepts Arabic-Indic / Persian digits and the Arabic decimal separator as well as ASCII. */
export function normalizeNumericInput(raw: string): string {
  return raw
    .replace(/[٠-٩]/g, (c) => String(c.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (c) => String(c.charCodeAt(0) - 0x06f0))
    .replace(/[٫،,]/g, '.')
    .replace(/\s+/g, '')
}

const plainNumber = /^\d+(\.\d+)?$/
export const isPlainNumber = (value: string) => plainNumber.test(value)
