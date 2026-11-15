import clsx from 'clsx'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useOrders } from '@/features/trading'
import { useInstrumentMap } from '@/shared/api/referenceData'
import type { Instrument, Order, OrderStatus } from '@/shared/api/types'
import { useLanguage } from '@/shared/i18n'
import { placesOf } from '@/shared/lib/decimal'
import { formatDecimal, formatTime } from '@/shared/lib/format'
import { SidePill, StatusPill } from '@/shared/ui/StatusPill'

type Filter = OrderStatus | 'all'
const FILTERS: Filter[] = ['all', 'Open', 'Filled', 'Cancelled', 'Rejected']

export function OrdersHistory() {
  const { t } = useTranslation()
  const [filter, setFilter] = useState<Filter>('all')
  const { data: orders, isLoading } = useOrders(filter === 'all' ? undefined : filter)
  const instruments = useInstrumentMap()

  return (
    <>
      <div role="group" aria-label={t('history.filter')} className="flex flex-wrap gap-1.5 px-5 pb-3 sm:px-6">
        {FILTERS.map((f) => (
          <button
            key={f}
            type="button"
            aria-pressed={filter === f}
            onClick={() => setFilter(f)}
            className={clsx(
              'rounded-full border px-3 py-1 text-sm transition-colors',
              filter === f ? 'border-lantern bg-lantern/10 text-fog' : 'border-rule text-muted hover:text-fog',
            )}
          >
            {f === 'all' ? t('history.all') : t(`status.${f}`)}
          </button>
        ))}
      </div>

      {!isLoading && orders?.length === 0 ? (
        <p className="px-5 pb-6 text-sm text-muted sm:px-6">{filter === 'all' ? t('history.noOrders') : t('history.noOrdersFiltered')}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[56rem] text-sm">
            <thead className="text-xs text-muted">
              <tr className="border-y border-rule">
                <th className="px-5 py-2 text-start font-normal sm:px-6">{t('cols.time')}</th>
                <th className="px-3 py-2 text-start font-normal">{t('cols.market')}</th>
                <th className="px-3 py-2 text-start font-normal">{t('cols.side')}</th>
                <th className="px-3 py-2 text-start font-normal">{t('cols.type')}</th>
                <th className="px-3 py-2 text-end font-normal">{t('cols.quantity')}</th>
                <th className="px-3 py-2 text-end font-normal">{t('cols.limit')}</th>
                <th className="px-3 py-2 text-end font-normal">{t('cols.fillPrice')}</th>
                <th className="px-3 py-2 text-end font-normal">{t('cols.fee')}</th>
                <th className="px-5 py-2 text-start font-normal sm:px-6">{t('cols.status')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              <AnimatePresence initial={false}>
                {orders?.map((o) => <OrderRow key={o.id} order={o} instrument={instruments.get(o.symbol)} />)}
              </AnimatePresence>
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

function OrderRow({ order, instrument }: { order: Order; instrument: Instrument | undefined }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const pp = instrument ? placesOf(instrument.tickSize) : 2
  const qp = instrument ? placesOf(instrument.stepSize) : 8

  return (
    <motion.tr layout="position" initial={{ opacity: 0, backgroundColor: 'var(--hover)' }} animate={{ opacity: 1, backgroundColor: 'rgba(0,0,0,0)' }} exit={{ opacity: 0 }}>
      <td className="num px-5 py-2.5 whitespace-nowrap text-muted sm:px-6">{formatTime(order.createdAt, language)}</td>
      <td className="px-3 py-2.5 font-medium">{instrument ? `${instrument.baseAsset}/${instrument.quoteAsset}` : order.symbol}</td>
      <td className="px-3 py-2.5">
        <SidePill side={order.side} />
      </td>
      <td className="px-3 py-2.5 text-muted">{t(`type.${order.type}`)}</td>
      <td className="num px-3 py-2.5 text-end">{formatDecimal(order.quantity, language, 0, qp)}</td>
      <td className="num px-3 py-2.5 text-end">{order.limitPrice ? formatDecimal(order.limitPrice, language, pp) : '–'}</td>
      <td className="num px-3 py-2.5 text-end">{order.fillPrice ? formatDecimal(order.fillPrice, language, pp) : '–'}</td>
      <td className="num px-3 py-2.5 text-end text-muted">{order.status === 'Filled' ? formatDecimal(order.fee, language, 2, 8) : '–'}</td>
      <td className="px-5 py-2.5 sm:px-6">
        <div className="flex flex-col items-start gap-0.5">
          <StatusPill status={order.status} />
          {order.rejectReason && <span className="text-xs text-muted">{t(`reasons.${order.rejectReason}`, { defaultValue: order.rejectReason })}</span>}
        </div>
      </td>
    </motion.tr>
  )
}
