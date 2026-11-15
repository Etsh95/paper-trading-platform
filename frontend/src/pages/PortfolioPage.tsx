import { useTranslation } from 'react-i18next'
import { AccountSummary, Allocation, PositionsTable, ResetAccount, useLiveValuation } from '@/features/portfolio'

export default function PortfolioPage() {
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
