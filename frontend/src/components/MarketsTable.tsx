import clsx from 'clsx'
import { motion } from 'motion/react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { Instrument } from '../api/types'
import { useLanguage } from '../i18n'
import { D, placesOf } from '../lib/decimal'
import { formatDecimal, formatPercent, signOf } from '../lib/format'
import { useInstruments } from '../state/queries'
import { useMarket, type LiveQuote } from '../state/quotes'
import { Sparkline } from './Sparkline'

const columns = 'grid grid-cols-[minmax(0,1.3fr)_1fr_1fr_0.8fr] md:grid-cols-[minmax(0,1.2fr)_1fr_1fr_0.8fr_6rem] items-center gap-x-3'

export function MarketsTable({ selected, onSelect }: { selected: string; onSelect: (symbol: string) => void }) {
  const { t } = useTranslation()
  const { data: instruments } = useInstruments()
  const { quotes } = useMarket()

  return (
    <section aria-labelledby="markets-title">
      <header className="flex items-baseline justify-between px-5 pt-5 pb-2 sm:px-6">
        <h2 id="markets-title" className="text-lg font-semibold">
          {t('markets.title')}
        </h2>
        <span className="text-xs text-muted">{t('markets.changeHint')}</span>
      </header>

      <div role="grid" aria-labelledby="markets-title" aria-readonly="true">
        <div role="row" className={clsx(columns, 'px-5 py-2 text-xs text-muted sm:px-6')}>
          <span role="columnheader">{t('markets.pair')}</span>
          <span role="columnheader" className="text-end">{t('markets.bid')}</span>
          <span role="columnheader" className="text-end">{t('markets.ask')}</span>
          <span role="columnheader" className="text-end">{t('markets.change')}</span>
          <span role="columnheader" className="hidden text-end md:block">{t('markets.trend')}</span>
        </div>

        {(instruments ?? []).map((instrument, index) => (
          <MarketRow
            key={instrument.symbol}
            index={index}
            instrument={instrument}
            quote={quotes.get(instrument.symbol)}
            selected={instrument.symbol === selected}
            onSelect={onSelect}
          />
        ))}
      </div>
    </section>
  )
}

function MarketRow(props: {
  index: number
  instrument: Instrument
  quote: LiveQuote | undefined
  selected: boolean
  onSelect: (symbol: string) => void
}) {
  const { index, instrument, quote, selected, onSelect } = props
  const { t } = useTranslation()
  const { language } = useLanguage()
  const places = placesOf(instrument.tickSize)

  const change = quote ? D(quote.mid).minus(quote.openMid).div(quote.openMid) : D(0)
  const changeSign = signOf(change.toDecimalPlaces(4)) // colour what is shown: 0.00% stays neutral

  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      onSelect(instrument.symbol)
    }
  }

  return (
    // The page's one orchestrated entrance: rows settle in once when the markets first load.
    <motion.div
      role="row"
      tabIndex={0}
      aria-selected={selected}
      aria-label={t('markets.select', { symbol: `${instrument.baseAsset}/${instrument.quoteAsset}` })}
      onClick={() => onSelect(instrument.symbol)}
      onKeyDown={onKeyDown}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.15 + index * 0.06, duration: 0.4, ease: [0.22, 1, 0.36, 1] }}
      className={clsx(columns, 'relative cursor-pointer px-5 py-3 outline-offset-[-2px] sm:px-6', !selected && 'hover:bg-hover')}
    >
      {selected && (
        <motion.span
          layoutId="market-selection"
          aria-hidden
          className="absolute inset-0 border-s-2 border-lantern bg-lantern/8"
          transition={{ type: 'spring', stiffness: 420, damping: 38 }}
        />
      )}

      <span role="gridcell" className="relative truncate">
        {/* Pair notation reads left to right in both languages. */}
        <span dir="ltr" className="inline-flex items-baseline gap-0.5">
          <span className="font-semibold">{instrument.baseAsset}</span>
          <span className="text-sm text-muted">/{instrument.quoteAsset}</span>
        </span>
      </span>
      <PriceCell value={quote?.bid} quote={quote} places={places} />
      <PriceCell value={quote?.ask} quote={quote} places={places} />
      <span
        role="gridcell"
        className={clsx('num relative text-end text-sm', changeSign === 'up' ? 'text-rise' : changeSign === 'down' ? 'text-fall' : 'text-muted')}
      >
        {quote ? formatPercent(change, language) : '–'}
      </span>
      <span role="gridcell" className="relative hidden justify-end md:flex">
        <Sparkline values={quote?.history ?? []} />
      </span>
    </motion.div>
  )
}

function PriceCell({ value, quote, places }: { value: string | undefined; quote: LiveQuote | undefined; places: number }) {
  const { language } = useLanguage()
  if (!value || !quote) {
    return (
      <span role="gridcell" className="relative text-end text-muted">
        –
      </span>
    )
  }
  return (
    <span role="gridcell" className="relative text-end">
      {/* Re-keyed on every tick so the flash restarts; colour encodes the direction of the move. */}
      <span
        key={quote.tick}
        className={clsx('num -mx-1 rounded px-1 font-medium', quote.tick > 0 && (quote.direction === 'up' ? 'flash-up' : 'flash-down'))}
      >
        {formatDecimal(value, language, places)}
      </span>
    </span>
  )
}
