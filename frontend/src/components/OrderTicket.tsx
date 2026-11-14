import clsx from 'clsx'
import { LoaderCircle } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useId, useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError } from '../api/client'
import type { Instrument, OrderSide, OrderType } from '../api/types'
import { useLanguage } from '../i18n'
import { D, placesOf } from '../lib/decimal'
import { formatDecimal, formatMoney } from '../lib/format'
import { evaluateTicket, quantityForFraction, type TicketContext, type TicketIssue } from '../lib/ticket'
import { useHubStatus } from '../realtime/hub'
import { useConfig, useInstruments, usePlaceOrder } from '../state/queries'
import { useQuote } from '../state/quotes'
import { useLiveValuation } from '../state/valuation'
import { Segmented } from './Segmented'

const FRACTIONS = [0.25, 0.5, 0.75, 1] as const

export function OrderTicket({ symbol, onSymbolChange }: { symbol: string; onSymbolChange: (s: string) => void }) {
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
  const [touched, setTouched] = useState({ quantity: false, limitPrice: false })
  const [attempted, setAttempted] = useState(false)
  const quantityRef = useRef<HTMLInputElement>(null)
  // Bumped on every edit, so a settling order can tell whether the user has started on the next one.
  const editVersion = useRef(0)
  useEffect(() => {
    editVersion.current += 1
  }, [side, type, quantity, limitPrice])
  const priceRef = useRef<HTMLInputElement>(null)
  const ids = { quantity: useId(), price: useId(), symbol: useId(), form: useId() }

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

  if (!instrument || !ctx || !config) return <TicketSkeleton />

  const evaluation = evaluateTicket({ side, type, quantity, limitPrice }, ctx)
  const issueFor = (field: TicketIssue['field']) => evaluation.issues.find((i) => i.field === field)
  const visible = (field: 'quantity' | 'limitPrice') => (attempted || touched[field]) && issueFor(field)
  // Funds/position problems are useful as soon as there's a valid estimate; no need to wait for submit.
  const formIssue = issueFor('form')
  const showFormIssue = formIssue && (attempted || evaluation.estimate || formIssue.code === 'no_market_data')

  const pricePlaces = placesOf(instrument.tickSize)
  const qtyPlaces = placesOf(instrument.stepSize)

  /** Numbers inside messages use the same formatting as the rest of the UI. */
  const messageParams = (issue: TicketIssue) => {
    const p = { ...(issue.params ?? {}) }
    if (issue.code === 'insufficient_funds' && p.max) p.max = formatMoney(p.max, language)
    if (issue.code === 'insufficient_position' && p.max) p.max = formatDecimal(p.max, language, 0, qtyPlaces)
    if (issue.code === 'min_notional' && p.min) p.min = formatMoney(p.min, language)
    return p
  }

  const touch = side === 'Buy' ? quote?.ask : quote?.bid
  const feePct = formatDecimal(D(config.feeRate).mul(100), language, 0, 4) + '%'

  const switchType = (next: OrderType) => {
    setType(next)
    // Start limit orders from the current price on the near side of the book.
    if (next === 'Limit' && limitPrice === '' && quote) setLimitPrice(side === 'Buy' ? quote.bid : quote.ask)
  }

  const applyFraction = (fraction: number) => {
    const price = type === 'Limit' && evaluation.limitPrice ? evaluation.limitPrice : touch
    setQuantity(quantityForFraction(fraction, side, price, ctx))
    setTouched((s) => ({ ...s, quantity: true }))
  }

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setAttempted(true)
    if (evaluation.issues.length > 0 || !evaluation.quantity) {
      if (issueFor('quantity')) quantityRef.current?.focus()
      else if (issueFor('limitPrice')) priceRef.current?.focus()
      return
    }

    // One key per user action. The client reuses it on a network retry; the server dedupes on it.
    const idempotencyKey = crypto.randomUUID()
    const versionAtSubmit = editVersion.current
    try {
      const { order } = await place.mutateAsync({
        key: idempotencyKey,
        input: { symbol, side, type, quantity: evaluation.quantity, limitPrice: type === 'Limit' ? evaluation.limitPrice : undefined },
      })
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
        setTouched((t) => ({ ...t, quantity: false }))
        setAttempted(false)
      }
    } catch (error) {
      const code = error instanceof ApiError ? error.code : 'unknown'
      toast.error(t('toast.rejected'), { description: t(`reasons.${code}`, { defaultValue: t('reasons.unknown') }) })
    }
  }

  const sideTone = side === 'Buy' ? 'bg-rise hover:brightness-110' : 'bg-fall hover:brightness-110'

  return (
    <form onSubmit={onSubmit} noValidate aria-labelledby={ids.form} className="flex flex-col gap-5 p-5 sm:p-6">
      <div className="flex items-center justify-between gap-3">
        <h2 id={ids.form} className="text-lg font-semibold">
          {t('ticket.title')}
        </h2>
        <label htmlFor={ids.symbol} className="sr-only">
          {t('cols.market')}
        </label>
        <select
          id={ids.symbol}
          value={symbol}
          onChange={(e) => onSymbolChange(e.target.value)}
          className="rounded-md border border-rule bg-harbor px-2.5 py-1.5 text-sm font-medium"
        >
          {instruments?.map((i: Instrument) => (
            <option key={i.symbol} value={i.symbol}>
              {i.baseAsset}/{i.quoteAsset}
            </option>
          ))}
        </select>
      </div>

      <Segmented
        id="ticket-side"
        label={t('ticket.sideLabel')}
        value={side}
        onChange={setSide}
        options={[
          { value: 'Buy', label: t('ticket.buy'), tone: 'rise' },
          { value: 'Sell', label: t('ticket.sell'), tone: 'fall' },
        ]}
      />

      <div className="flex flex-col gap-2">
        <Segmented
          id="ticket-type"
          size="sm"
          label={t('ticket.typeLabel')}
          value={type}
          onChange={switchType}
          options={[
            { value: 'Market', label: t('ticket.market') },
            { value: 'Limit', label: t('ticket.limit') },
          ]}
        />
        <AnimatePresence mode="wait" initial={false}>
          <motion.p
            key={type + side}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            className="text-sm text-muted"
          >
            {type === 'Market' ? t('ticket.marketHint', { touch: t(side === 'Buy' ? 'ticket.touchAsk' : 'ticket.touchBid') }) : t('ticket.limitHint')}
          </motion.p>
        </AnimatePresence>
      </div>

      <AnimatePresence initial={false}>
        {type === 'Limit' && (
          <motion.div
            key="limit"
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
            className="-m-1 overflow-hidden p-1"
          >
            <NumberField
              id={ids.price}
              inputRef={priceRef}
              label={t('ticket.limitPrice')}
              unit={instrument.quoteAsset}
              value={limitPrice}
              onChange={setLimitPrice}
              onBlur={() => setTouched((s) => ({ ...s, limitPrice: true }))}
              issue={visible('limitPrice') || undefined}
              action={
                quote && (
                  <button type="button" onClick={() => setLimitPrice(side === 'Buy' ? quote.bid : quote.ask)} className="text-xs text-lantern hover:underline">
                    {t('ticket.useCurrent')}
                  </button>
                )
              }
            />
          </motion.div>
        )}
      </AnimatePresence>

      <div className="flex flex-col gap-2">
        <NumberField
          id={ids.quantity}
          inputRef={quantityRef}
          label={t('ticket.quantity')}
          unit={instrument.baseAsset}
          value={quantity}
          onChange={setQuantity}
          onBlur={() => setTouched((s) => ({ ...s, quantity: true }))}
          issue={visible('quantity') || undefined}
        />
        <div className="grid grid-cols-4 gap-1.5">
          {FRACTIONS.map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => applyFraction(f)}
              aria-label={t('ticket.fraction', { pct: `${f * 100}%` })}
              className="num rounded-md border border-rule py-1 text-xs text-muted transition-colors hover:border-lantern hover:text-fog"
            >
              {f * 100}%
            </button>
          ))}
        </div>
        <p className="text-xs text-muted">
          {t('ticket.available')}{' '}
          <span className="num text-fog">
            {side === 'Buy'
              ? `${formatMoney(ctx.availableCash, language)} ${instrument.quoteAsset}`
              : `${formatDecimal(ctx.availableQuantity, language, 0, qtyPlaces)} ${instrument.baseAsset}`}
          </span>
        </p>
      </div>

      <dl className="flex flex-col gap-1.5 border-t border-rule pt-4 text-sm">
        <EstimateRow label={type === 'Market' ? t('ticket.estPrice') : t('ticket.price')} value={evaluation.estimate ? formatDecimal(evaluation.estimate.price, language, pricePlaces) : '–'} />
        <EstimateRow label={t('ticket.notional')} value={evaluation.estimate ? formatMoney(evaluation.estimate.notional, language) : '–'} />
        <EstimateRow label={t('ticket.fee', { rate: feePct })} value={evaluation.estimate ? formatDecimal(evaluation.estimate.fee, language, 2, 8) : '–'} />
        <EstimateRow
          strong
          label={side === 'Buy' ? t('ticket.totalBuy') : t('ticket.totalSell')}
          value={evaluation.estimate ? `${formatMoney(evaluation.estimate.total, language)} ${instrument.quoteAsset}` : '–'}
        />
      </dl>

      <AnimatePresence initial={false}>
        {showFormIssue && (
          <motion.p
            key={formIssue.code}
            role="alert"
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            className="rounded-md bg-fall/10 px-3 py-2 text-sm text-fall"
          >
            {t(`validation.${formIssue.code}`, messageParams(formIssue))}
          </motion.p>
        )}
      </AnimatePresence>

      <motion.button
        type="submit"
        disabled={place.isPending}
        whileTap={{ scale: 0.98 }}
        className={clsx(
          'flex items-center justify-center gap-2 rounded-lg py-3 text-base font-semibold text-on-signal transition-[background-color,filter] duration-300 disabled:opacity-70',
          sideTone,
        )}
      >
        {place.isPending && <LoaderCircle className="size-4 animate-spin" aria-hidden />}
        {place.isPending ? t('ticket.sending') : t(side === 'Buy' ? 'ticket.submitBuy' : 'ticket.submitSell', { asset: instrument.baseAsset })}
      </motion.button>
    </form>
  )
}

function NumberField(props: {
  id: string
  label: string
  unit: string
  value: string
  onChange: (v: string) => void
  onBlur: () => void
  issue?: TicketIssue
  inputRef: React.RefObject<HTMLInputElement | null>
  action?: React.ReactNode
}) {
  const { t } = useTranslation()
  const { id, label, unit, value, onChange, onBlur, issue, inputRef, action } = props
  const errorId = `${id}-error`
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
        </label>
        {action}
      </div>
      <div
        className={clsx(
          'flex items-center rounded-lg border bg-harbor transition-colors focus-within:border-lantern',
          issue ? 'border-fall' : 'border-rule',
        )}
      >
        <input
          id={id}
          ref={inputRef}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          inputMode="decimal"
          autoComplete="off"
          spellCheck={false}
          placeholder="0.00"
          aria-invalid={issue ? true : undefined}
          aria-describedby={issue ? errorId : undefined}
          className="num min-w-0 flex-1 bg-transparent px-3 py-2.5 text-base outline-none placeholder:text-muted/60"
          dir="ltr"
        />
        <span className="pe-3 text-sm text-muted">{unit}</span>
      </div>
      <AnimatePresence initial={false}>
        {issue && (
          <motion.p
            id={errorId}
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            className="text-sm text-fall"
          >
            {t(`validation.${issue.code}`, issue.params ?? {})}
          </motion.p>
        )}
      </AnimatePresence>
    </div>
  )
}

function EstimateRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={clsx('flex justify-between gap-4', strong ? 'pt-1 text-base font-semibold' : 'text-muted')}>
      <dt>{label}</dt>
      <dd className={clsx('num', strong ? 'text-fog' : 'text-fog/90')}>{value}</dd>
    </div>
  )
}

function TicketSkeleton() {
  return (
    <div className="flex animate-pulse flex-col gap-4 p-6" aria-hidden>
      <div className="h-5 w-32 rounded bg-hover" />
      <div className="h-11 rounded-lg bg-hover" />
      <div className="h-9 rounded-lg bg-hover" />
      <div className="h-11 rounded-lg bg-hover" />
      <div className="h-24 rounded-lg bg-hover" />
      <div className="h-12 rounded-lg bg-hover" />
    </div>
  )
}
