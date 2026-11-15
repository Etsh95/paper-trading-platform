import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { useQuote } from '@/features/market'
import { useLiveValuation } from '@/features/portfolio'
import { ApiError } from '@/shared/api/client'
import { useConfig, useInstruments } from '@/shared/api/referenceData'
import type { OrderSide, OrderType } from '@/shared/api/types'
import { useLanguage } from '@/shared/i18n'
import { D, placesOf } from '@/shared/lib/decimal'
import { formatDecimal, formatMoney } from '@/shared/lib/format'
import { useHubStatus } from '@/shared/realtime/hub'
import { usePlaceOrder } from './accountQueries'
import { OrderAttempts } from './orderAttempts'
import { evaluateTicket, quantityForFraction, type TicketContext, type TicketIssue } from './ticketRules'

type Field = 'quantity' | 'limitPrice'

/**
 * View-model for the order ticket: form state, validation, estimate, submission and notifications.
 * The component only renders what this returns. Returns `null` until reference data and balances are loaded.
 */
export function useOrderTicket(symbol: string) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const { data: instruments } = useInstruments()
  const { data: config } = useConfig()
  const valuation = useLiveValuation()
  const quote = useQuote(symbol)
  const hub = useHubStatus()
  const place = usePlaceOrder()

  const [side, setSide] = useState<OrderSide>('Buy')
  const [type, setType] = useState<OrderType>('Market')
  const [quantity, setQuantity] = useState('')
  const [limitPrice, setLimitPrice] = useState('')
  const [touched, setTouched] = useState<Record<Field, boolean>>({ quantity: false, limitPrice: false })
  const [attempted, setAttempted] = useState(false)
  const quantityRef = useRef<HTMLInputElement>(null)
  const priceRef = useRef<HTMLInputElement>(null)
  const [attempts] = useState(() => new OrderAttempts())

  // Bumped on every edit, so a settling order can tell whether the user has started on the next one.
  const editVersion = useRef(0)
  useEffect(() => {
    editVersion.current += 1
  }, [side, type, quantity, limitPrice])

  const instrument = instruments?.find((i) => i.symbol === symbol)
  const position = valuation?.positions.find((p) => p.symbol === symbol)

  const ctx: TicketContext | undefined = useMemo(
    () =>
      instrument && config && valuation
        ? {
            instrument,
            bid: quote?.bid,
            ask: quote?.ask,
            availableCash: valuation.availableCash.toFixed(),
            availableQuantity: position?.available.toFixed() ?? '0',
            feeRate: config.feeRate,
          }
        : undefined,
    [instrument, config, valuation, quote?.bid, quote?.ask, position],
  )

  if (!instrument || !ctx || !config || !instruments) return null

  const evaluation = evaluateTicket({ side, type, quantity, limitPrice }, ctx)
  const pricePlaces = placesOf(instrument.tickSize)
  const qtyPlaces = placesOf(instrument.stepSize)
  const touch = side === 'Buy' ? quote?.ask : quote?.bid
  const issueFor = (field: TicketIssue['field']) => evaluation.issues.find((i) => i.field === field)

  /** Numbers inside messages use the same formatting as the rest of the UI. */
  const message = (issue: TicketIssue) => {
    const p = { ...(issue.params ?? {}) }
    if (issue.code === 'insufficient_funds' && p.max) p.max = formatMoney(p.max, language)
    if (issue.code === 'insufficient_position' && p.max) p.max = formatDecimal(p.max, language, 0, qtyPlaces)
    if (issue.code === 'min_notional' && p.min) p.min = formatMoney(p.min, language)
    return t(`validation.${issue.code}`, p)
  }

  const fieldError = (field: Field) => {
    const issue = issueFor(field)
    return issue && (attempted || touched[field]) ? message(issue) : undefined
  }

  // Funds/position problems are useful as soon as there's a valid estimate; no need to wait for submit.
  const formIssue = issueFor('form')
  const formError =
    formIssue && (attempted || evaluation.estimate || formIssue.code === 'no_market_data')
      ? { code: formIssue.code, text: message(formIssue) }
      : undefined

  const selectType = (next: OrderType) => {
    setType(next)
    // Start limit orders from the current price on the near side of the book.
    if (next === 'Limit' && limitPrice === '' && quote) setLimitPrice(side === 'Buy' ? quote.bid : quote.ask)
  }

  const fillCurrentPrice = () => quote && setLimitPrice(side === 'Buy' ? quote.bid : quote.ask)

  const applyFraction = (fraction: number) => {
    const price = type === 'Limit' && evaluation.limitPrice ? evaluation.limitPrice : touch
    setQuantity(quantityForFraction(fraction, side, price, ctx))
    setTouched((s) => ({ ...s, quantity: true }))
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setAttempted(true)
    if (evaluation.issues.length > 0 || !evaluation.quantity) {
      if (issueFor('quantity')) quantityRef.current?.focus()
      else if (issueFor('limitPrice')) priceRef.current?.focus()
      return
    }

    const input = { symbol, side, type, quantity: evaluation.quantity, limitPrice: type === 'Limit' ? evaluation.limitPrice : undefined }
    // One key per order: reused only if this exact order is resubmitted after a network failure (unknown outcome).
    const idempotencyKey = attempts.keyFor(input)
    const versionAtSubmit = editVersion.current
    try {
      const { order } = await place.mutateAsync({ key: idempotencyKey, input })
      attempts.settle()
      const params = {
        qty: formatDecimal(order.quantity, language, 0, qtyPlaces),
        asset: instrument.baseAsset,
        price: formatDecimal(order.fillPrice ?? order.limitPrice ?? '0', language, pricePlaces),
      }
      if (order.status === 'Open') toast(t(side === 'Buy' ? 'toast.limitPlacedBuy' : 'toast.limitPlacedSell', params))
      // Fills are announced by the realtime stream; only announce here if it's down.
      else if (order.status === 'Filled' && hub !== 'live') toast.success(t(side === 'Buy' ? 'toast.bought' : 'toast.sold', params))

      // Clear only if nothing was edited since submitting: the user may already be entering the next order
      // (possibly with the same quantity) while this one settles.
      if (editVersion.current === versionAtSubmit) {
        setQuantity('')
        setTouched((s) => ({ ...s, quantity: false }))
        setAttempted(false)
      }
    } catch (error) {
      const code = error instanceof ApiError ? error.code : 'unknown'
      if (code !== 'network') attempts.settle() // a definitive answer; only network failures leave it unknown
      toast.error(t('toast.rejected'), { description: t(`reasons.${code}`, { defaultValue: t('reasons.unknown') }) })
    }
  }

  return {
    instrument,
    instruments,
    hasQuote: Boolean(quote),
    side,
    setSide,
    type,
    selectType,
    quantity,
    setQuantity,
    limitPrice,
    setLimitPrice,
    fillCurrentPrice,
    applyFraction,
    markTouched: (field: Field) => setTouched((s) => ({ ...s, [field]: true })),
    fieldError,
    formError,
    refs: { quantity: quantityRef, price: priceRef },
    estimate: evaluation.estimate,
    available:
      side === 'Buy'
        ? `${formatMoney(ctx.availableCash, language)} ${instrument.quoteAsset}`
        : `${formatDecimal(ctx.availableQuantity, language, 0, qtyPlaces)} ${instrument.baseAsset}`,
    feeRateText: formatDecimal(D(config.feeRate).mul(100), language, 0, 4) + '%',
    pricePlaces,
    submit,
    submitting: place.isPending,
  }
}

export type OrderTicketModel = NonNullable<ReturnType<typeof useOrderTicket>>
