import clsx from 'clsx'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLanguage } from '@/shared/i18n'
import { formatMoney, formatPercent, signOf } from '@/shared/lib/format'
import { RollingNumber } from '@/shared/ui/RollingNumber'
import { useLiveValuation } from './valuation'

/**
 * Holds a fast-changing value steady for `ms` between updates. Account value re-marks on every price tick
 * (several per second); rolling digits that never come to rest read as noise rather than information.
 */
function useSteadyValue(value: string, ms: number) {
  const [shown, setShown] = useState(value)
  const last = useRef(0)
  useEffect(() => {
    const wait = Math.max(0, last.current + ms - Date.now())
    const id = setTimeout(() => {
      last.current = Date.now()
      setShown(value)
    }, wait)
    return () => clearTimeout(id)
  }, [value, ms])
  return shown
}

/** The page's one bold element: account value with rolling digits. */
export function AccountSummary({ detailed = false }: { detailed?: boolean }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const v = useLiveValuation()
  const equityText = useSteadyValue(v ? formatMoney(v.equity, language) : '', 1_000)

  if (!v) return <SummarySkeleton />

  const pnlSign = signOf(v.totalPnl)
  const pnlTone = pnlSign === 'up' ? 'text-rise' : pnlSign === 'down' ? 'text-fall' : 'text-muted'

  const stats = [
    { label: t('summary.cash'), value: v.availableCash },
    { label: t('summary.reserved'), value: v.reservedCash },
    { label: t('summary.invested'), value: v.positionsValue },
    ...(detailed ? [{ label: t('summary.fees'), value: v.totalFees }] : []),
  ]

  return (
    <div className="flex flex-col gap-5 p-5 sm:flex-row sm:items-end sm:justify-between sm:p-6">
      <div>
        <h2 className="text-sm text-muted">{t('summary.equity')}</h2>
        <p className="mt-1 text-2xl leading-tight font-semibold tracking-tight">
          <RollingNumber text={equityText || formatMoney(v.equity, language)} />
          <span className="ms-2 text-lg font-normal text-muted">USDT</span>
        </p>
        <p className={clsx('mt-1 text-base font-medium', pnlTone)}>
          <span className="num">{formatMoney(v.totalPnl, language, true)}</span>{' '}
          <span className="num">({formatPercent(v.totalPnlFraction, language)})</span>{' '}
          <span className="font-normal text-muted">
            {t('summary.pnlSince', { amount: formatMoney(v.initialCash, language) })}
          </span>
        </p>
      </div>

      <dl className="grid grid-cols-3 gap-x-6 gap-y-3 sm:flex sm:gap-8">
        {stats.map((s) => (
          <div key={s.label}>
            <dt className="text-xs text-muted">{s.label}</dt>
            <dd className="num mt-0.5 text-base font-medium">{formatMoney(s.value, language)}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}

function SummarySkeleton() {
  return (
    <div className="animate-pulse p-6" aria-hidden>
      <div className="h-3 w-24 rounded bg-hover" />
      <div className="mt-3 h-10 w-64 rounded bg-hover" />
      <div className="mt-3 h-4 w-48 rounded bg-hover" />
    </div>
  )
}
