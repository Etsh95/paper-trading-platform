import { motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { useInstrumentMap } from '@/shared/api/referenceData'
import { useLanguage } from '@/shared/i18n'
import { formatMoney } from '@/shared/lib/format'
import type { LiveValuation } from './valuation'

const palette = ['var(--lantern)', 'var(--rise)', '#7aa2f7', '#c792ea', '#f78c6c']

/** Stacked bar: what share of account value sits in cash vs each position. Widths animate as prices move. */
export function Allocation({ valuation }: { valuation: LiveValuation }) {
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
