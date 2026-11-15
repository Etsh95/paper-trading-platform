import clsx from 'clsx'
import { ChevronDown, LoaderCircle } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { useLanguage } from '@/shared/i18n'
import { formatDecimal, formatMoney } from '@/shared/lib/format'
import { NumberField } from '@/shared/ui/NumberField'
import { Segmented } from '@/shared/ui/Segmented'
import { useOrderTicket, type OrderTicketModel } from './useOrderTicket'

const FRACTIONS = [0.25, 0.5, 0.75, 1] as const

/** Order entry. All behaviour lives in {@link useOrderTicket}; this component only renders it. */
export function OrderTicket({ symbol, onSymbolChange }: { symbol: string; onSymbolChange: (s: string) => void }) {
  const ticket = useOrderTicket(symbol)
  const ids = { form: useId(), symbol: useId(), quantity: useId(), price: useId() }
  if (!ticket) return <TicketSkeleton />

  return (
    <form onSubmit={ticket.submit} noValidate aria-labelledby={ids.form} className="flex flex-col gap-5 p-5 sm:p-6">
      <Header ticket={ticket} symbol={symbol} onSymbolChange={onSymbolChange} titleId={ids.form} selectId={ids.symbol} />
      <SideAndType ticket={ticket} />
      <LimitPrice ticket={ticket} id={ids.price} />
      <Quantity ticket={ticket} id={ids.quantity} />
      <Estimate ticket={ticket} />
      <FormError ticket={ticket} />
      <SubmitButton ticket={ticket} />
    </form>
  )
}

interface Part {
  ticket: OrderTicketModel
}

function Header({ ticket, symbol, onSymbolChange, titleId, selectId }: Part & { symbol: string; onSymbolChange: (s: string) => void; titleId: string; selectId: string }) {
  const { t } = useTranslation()
  return (
    <div className="flex items-center justify-between gap-3">
      <h2 id={titleId} className="text-lg font-semibold">
        {t('ticket.title')}
      </h2>
      <label htmlFor={selectId} className="sr-only">
        {t('cols.market')}
      </label>
      {/* Native arrow hidden (it can't be padded); a chevron at the inline end mirrors correctly in RTL. */}
      <div className="relative">
        <select
          id={selectId}
          value={symbol}
          onChange={(e) => onSymbolChange(e.target.value)}
          className="appearance-none rounded-md border border-rule bg-harbor py-1.5 ps-3 pe-9 text-sm font-medium transition-colors hover:border-muted"
        >
          {ticket.instruments.map((i) => (
            <option key={i.symbol} value={i.symbol}>
              {i.baseAsset}/{i.quoteAsset}
            </option>
          ))}
        </select>
        <ChevronDown className="pointer-events-none absolute end-3 top-1/2 size-4 -translate-y-1/2 text-muted" aria-hidden />
      </div>
    </div>
  )
}

function SideAndType({ ticket }: Part) {
  const { t } = useTranslation()
  return (
    <>
      <Segmented
        id="ticket-side"
        label={t('ticket.sideLabel')}
        value={ticket.side}
        onChange={ticket.setSide}
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
          value={ticket.type}
          onChange={ticket.selectType}
          options={[
            { value: 'Market', label: t('ticket.market') },
            { value: 'Limit', label: t('ticket.limit') },
          ]}
        />
        <AnimatePresence mode="wait" initial={false}>
          <motion.p
            key={ticket.type + ticket.side}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
            className="text-sm text-muted"
          >
            {ticket.type === 'Market'
              ? t('ticket.marketHint', { touch: t(ticket.side === 'Buy' ? 'ticket.touchAsk' : 'ticket.touchBid') })
              : t('ticket.limitHint')}
          </motion.p>
        </AnimatePresence>
      </div>
    </>
  )
}

function LimitPrice({ ticket, id }: Part & { id: string }) {
  const { t } = useTranslation()
  return (
    <AnimatePresence initial={false}>
      {ticket.type === 'Limit' && (
        <motion.div
          key="limit"
          initial={{ height: 0, opacity: 0 }}
          animate={{ height: 'auto', opacity: 1 }}
          exit={{ height: 0, opacity: 0 }}
          transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
          className="-m-1 overflow-hidden p-1"
        >
          <NumberField
            id={id}
            inputRef={ticket.refs.price}
            label={t('ticket.limitPrice')}
            unit={ticket.instrument.quoteAsset}
            value={ticket.limitPrice}
            onChange={ticket.setLimitPrice}
            onBlur={() => ticket.markTouched('limitPrice')}
            error={ticket.fieldError('limitPrice')}
            action={
              ticket.hasQuote && (
                <button type="button" onClick={ticket.fillCurrentPrice} className="text-xs text-lantern hover:underline">
                  {t('ticket.useCurrent')}
                </button>
              )
            }
          />
        </motion.div>
      )}
    </AnimatePresence>
  )
}

function Quantity({ ticket, id }: Part & { id: string }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-2">
      <NumberField
        id={id}
        inputRef={ticket.refs.quantity}
        label={t('ticket.quantity')}
        unit={ticket.instrument.baseAsset}
        value={ticket.quantity}
        onChange={ticket.setQuantity}
        onBlur={() => ticket.markTouched('quantity')}
        error={ticket.fieldError('quantity')}
      />
      <div className="grid grid-cols-4 gap-1.5">
        {FRACTIONS.map((f) => (
          <button
            key={f}
            type="button"
            onClick={() => ticket.applyFraction(f)}
            aria-label={t('ticket.fraction', { pct: `${f * 100}%` })}
            className="num rounded-md border border-rule py-1 text-xs text-muted transition-colors hover:border-lantern hover:text-fog"
          >
            {f * 100}%
          </button>
        ))}
      </div>
      <p className="text-xs text-muted">
        {t('ticket.available')} <span className="num text-fog">{ticket.available}</span>
      </p>
    </div>
  )
}

function Estimate({ ticket }: Part) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const e = ticket.estimate
  const quoteAsset = ticket.instrument.quoteAsset
  return (
    <dl className="flex flex-col gap-1.5 border-t border-rule pt-4 text-sm">
      <EstimateRow label={ticket.type === 'Market' ? t('ticket.estPrice') : t('ticket.price')} value={e ? formatDecimal(e.price, language, ticket.pricePlaces) : '–'} />
      <EstimateRow label={t('ticket.notional')} value={e ? formatMoney(e.notional, language) : '–'} />
      <EstimateRow label={t('ticket.fee', { rate: ticket.feeRateText })} value={e ? formatDecimal(e.fee, language, 2, 8) : '–'} />
      <EstimateRow
        strong
        label={ticket.side === 'Buy' ? t('ticket.totalBuy') : t('ticket.totalSell')}
        value={e ? `${formatMoney(e.total, language)} ${quoteAsset}` : '–'}
      />
    </dl>
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

function FormError({ ticket }: Part) {
  return (
    <AnimatePresence initial={false}>
      {ticket.formError && (
        <motion.p
          key={ticket.formError.code}
          role="alert"
          initial={{ opacity: 0, y: -4 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0 }}
          className="rounded-md bg-fall/10 px-3 py-2 text-sm text-fall"
        >
          {ticket.formError.text}
        </motion.p>
      )}
    </AnimatePresence>
  )
}

function SubmitButton({ ticket }: Part) {
  const { t } = useTranslation()
  const label = ticket.submitting
    ? t('ticket.sending')
    : t(ticket.side === 'Buy' ? 'ticket.submitBuy' : 'ticket.submitSell', { asset: ticket.instrument.baseAsset })
  return (
    <motion.button
      type="submit"
      disabled={ticket.submitting}
      whileTap={{ scale: 0.98 }}
      className={clsx(
        'flex items-center justify-center gap-2 rounded-lg py-3 text-base font-semibold text-on-signal transition-[background-color,filter] duration-300 hover:brightness-110 disabled:opacity-70',
        ticket.side === 'Buy' ? 'bg-rise' : 'bg-fall',
      )}
    >
      {ticket.submitting && <LoaderCircle className="size-4 animate-spin" aria-hidden />}
      {label}
    </motion.button>
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
