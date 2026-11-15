import clsx from 'clsx'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useInstrumentMap } from '@/shared/api/referenceData'
import { useLanguage } from '@/shared/i18n'
import { D, placesOf } from '@/shared/lib/decimal'
import { formatDecimal, formatMoney, signOf } from '@/shared/lib/format'
import type { LiveValuation } from './valuation'

export function PositionsTable({ valuation }: { valuation: LiveValuation }) {
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
