import clsx from 'clsx'
import { RotateCcw } from 'lucide-react'
import { motion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { renewSession } from '../api/session'
import { AccountSummary } from '../components/AccountSummary'
import { useLanguage } from '../i18n'
import { D, placesOf } from '../lib/decimal'
import { formatDecimal, formatMoney, signOf } from '../lib/format'
import { useInstrumentMap } from '../state/queries'
import { useLiveValuation, type LiveValuation } from '../state/valuation'

export function PortfolioPage() {
  const { t } = useTranslation()
  const valuation = useLiveValuation()

  return (
    <div className="flex flex-col gap-4 lg:gap-6">
      <h1 className="sr-only">{t('portfolio.title')}</h1>
      <div className="divide-y divide-rule overflow-hidden rounded-xl border border-rule bg-deck">
        <AccountSummary detailed />
        {valuation && <Allocation valuation={valuation} />}
      </div>

      <section aria-labelledby="positions-title" className="overflow-hidden rounded-xl border border-rule bg-deck">
        <header className="flex flex-wrap items-baseline justify-between gap-2 px-5 pt-5 pb-3 sm:px-6">
          <h2 id="positions-title" className="text-lg font-semibold">
            {t('portfolio.positions')}
          </h2>
          <p className="text-xs text-muted">{t('portfolio.markHint')}</p>
        </header>
        {valuation && <PositionsTable valuation={valuation} />}
      </section>

      <ResetAccount />
    </div>
  )
}

const palette = ['var(--lantern)', 'var(--rise)', '#7aa2f7', '#c792ea', '#f78c6c']

/** Stacked bar: what share of account value sits in cash vs each position. Widths animate as prices move. */
function Allocation({ valuation }: { valuation: LiveValuation }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const instruments = useInstrumentMap()
  const total = valuation.equity
  if (total.lte(0)) return null

  const slices = [
    { key: 'cash', label: t('portfolio.cash'), value: valuation.cash, color: 'var(--muted)' },
    ...valuation.positions.map((p, i) => ({
      key: p.symbol,
      label: instruments.get(p.symbol)?.baseAsset ?? p.symbol,
      value: p.liveValue,
      color: palette[i % palette.length]!,
    })),
  ]

  return (
    <div className="px-5 py-5 sm:px-6">
      <h2 className="text-sm text-muted">{t('portfolio.allocation')}</h2>
      <div className="mt-3 flex h-3 overflow-hidden rounded-full bg-harbor" role="img" aria-label={slices.map((s) => `${s.label} ${s.value.div(total).mul(100).toFixed(1)}%`).join(', ')}>
        {slices.map((s) => (
          <motion.div
            key={s.key}
            className="h-full"
            style={{ backgroundColor: s.color }}
            initial={{ width: 0 }}
            animate={{ width: `${s.value.div(total).mul(100).toFixed(3)}%` }}
            transition={{ type: 'spring', stiffness: 120, damping: 24 }}
          />
        ))}
      </div>
      <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5 text-sm">
        {slices.map((s) => (
          <li key={s.key} className="flex items-center gap-2">
            <span className="size-2.5 rounded-full" style={{ backgroundColor: s.color }} aria-hidden />
            <span>{s.label}</span>
            <span className="num text-muted">{formatMoney(s.value, language)}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function PositionsTable({ valuation }: { valuation: LiveValuation }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const instruments = useInstrumentMap()

  if (valuation.positions.length === 0) {
    return (
      <p className="px-5 pb-6 text-sm text-muted sm:px-6">
        {t('portfolio.none')}{' '}
        <Link to="/" className="text-lantern hover:underline">
          {t('nav.trade')}
        </Link>
      </p>
    )
  }

  const tone = (v: Parameters<typeof signOf>[0]) => (signOf(v) === 'up' ? 'text-rise' : signOf(v) === 'down' ? 'text-fall' : 'text-muted')

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[44rem] text-sm">
        <thead className="text-xs text-muted">
          <tr className="border-b border-rule">
            <th className="px-5 py-2 text-start font-normal sm:px-6">{t('cols.asset')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.quantity')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.avgCost')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.mark')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.value')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.unrealized')}</th>
            <th className="px-5 py-2 text-end font-normal sm:px-6">{t('cols.realized')}</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-rule">
          {valuation.positions.map((p) => {
            const ins = instruments.get(p.symbol)
            const pp = ins ? placesOf(ins.tickSize) : 2
            const qp = ins ? placesOf(ins.stepSize) : 8
            const pct = D(p.costBasis).isZero() ? D(0) : p.liveUnrealized.div(p.costBasis)
            return (
              <tr key={p.symbol}>
                <td className="px-5 py-3 font-semibold sm:px-6">{ins?.baseAsset ?? p.symbol}</td>
                <td className="num px-3 py-3 text-end">{formatDecimal(p.quantity, language, 0, qp)}</td>
                <td className="num px-3 py-3 text-end">{formatDecimal(p.averageCost, language, pp)}</td>
                <td className="num px-3 py-3 text-end">{p.liveMark ? formatDecimal(p.liveMark, language, pp) : '–'}</td>
                <td className="num px-3 py-3 text-end font-medium">{formatMoney(p.liveValue, language)}</td>
                <td className={clsx('num px-3 py-3 text-end font-medium', tone(p.liveUnrealized))}>
                  {formatMoney(p.liveUnrealized, language, true)}
                  <span className="ms-1 text-xs opacity-80">({formatDecimal(pct.mul(100), language, 2, 2, true)}%)</span>
                </td>
                <td className={clsx('num px-5 py-3 text-end sm:px-6', tone(p.realizedPnl))}>{formatMoney(p.realizedPnl, language, true)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** Two-step confirm instead of a modal dialog: first click arms, second click resets. */
function ResetAccount() {
  const { t } = useTranslation()
  const [armed, setArmed] = useState(false)
  const [busy, setBusy] = useState(false)

  const onClick = async () => {
    if (!armed) {
      setArmed(true)
      setTimeout(() => setArmed(false), 4_000)
      return
    }
    setBusy(true)
    try {
      await renewSession('created')
      toast(t('toast.newAccount'))
    } finally {
      setBusy(false)
      setArmed(false)
    }
  }

  return (
    <div className="flex justify-end">
      <button
        type="button"
        onClick={onClick}
        disabled={busy}
        className={clsx(
          'inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors',
          armed ? 'border-fall bg-fall/10 text-fall' : 'border-rule text-muted hover:text-fog',
        )}
      >
        <RotateCcw className={clsx('size-4', busy && 'animate-spin')} aria-hidden />
        {armed ? t('portfolio.resetConfirm') : t('portfolio.reset')}
      </button>
    </div>
  )
}
