import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { OrdersHistory, TradesHistory } from '@/features/history'
import { Segmented } from '@/shared/ui/Segmented'

type Tab = 'orders' | 'trades'

export default function HistoryPage() {
  const { t } = useTranslation()
  const [tab, setTab] = useState<Tab>('orders')

  return (
    <section aria-labelledby="history-title" className="overflow-hidden rounded-xl border border-rule bg-deck">
      <header className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5 pb-4 sm:px-6">
        <h1 id="history-title" className="text-lg font-semibold">
          {t('history.title')}
        </h1>
        <div className="w-56">
          <Segmented
            id="history-tab"
            size="sm"
            label={t('history.title')}
            value={tab}
            onChange={setTab}
            options={[
              { value: 'orders', label: t('history.orders') },
              { value: 'trades', label: t('history.trades') },
            ]}
          />
        </div>
      </header>
      {tab === 'orders' ? <OrdersHistory /> : <TradesHistory />}
    </section>
  )
}
