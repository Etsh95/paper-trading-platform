import { LoaderCircle, X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { useMarket } from '@/features/market'
import { ApiError } from '@/shared/api/client'
import type { Order } from '@/shared/api/types'
import { useLanguage } from '@/shared/i18n'
import { D, placesOf } from '@/shared/lib/decimal'
import { formatDecimal, formatPercent } from '@/shared/lib/format'
import { useInstrumentMap } from '@/shared/api/referenceData'
import { SidePill } from '@/shared/ui/StatusPill'
import { useCancelOrder, useOrders } from './accountQueries'

export function OpenOrders() {
  const { t } = useTranslation()
  const { data: orders } = useOrders('Open')

  return (
    <section aria-labelledby="open-orders-title" className="pb-2">
      <header className="flex items-baseline gap-2 px-5 pt-5 pb-2 sm:px-6">
        <h2 id="open-orders-title" className="text-lg font-semibold">
          {t('orders.open')}
        </h2>
        {orders && orders.length > 0 && <span className="num text-sm text-muted">{orders.length}</span>}
      </header>
      {orders && orders.length === 0 && <p className="px-5 pb-4 text-sm text-muted sm:px-6">{t('orders.none')}</p>}
      <ul>
        <AnimatePresence initial={false}>
          {orders?.map((o) => <OpenOrderRow key={o.id} order={o} />)}
        </AnimatePresence>
      </ul>
    </section>
  )
}

function OpenOrderRow({ order }: { order: Order }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const instruments = useInstrumentMap()
  const { quotes } = useMarket()
  const cancel = useCancelOrder()
  const instrument = instruments.get(order.symbol)
  if (!instrument || !order.limitPrice) return null

  const quote = quotes.get(order.symbol)
  const touch = order.side === 'Buy' ? quote?.ask : quote?.bid
  // How far the market still has to move for this order to fill.
  const distance = touch ? D(order.limitPrice).minus(touch).div(touch) : null
  const qty = formatDecimal(order.quantity, language, 0, placesOf(instrument.stepSize))
  const price = formatDecimal(order.limitPrice, language, placesOf(instrument.tickSize))

  const onCancel = async () => {
    try {
      await cancel.mutateAsync(order.id)
      toast(t('toast.cancelled'))
    } catch (error) {
      const code = error instanceof ApiError ? error.code : 'unknown'
      toast.error(t(`reasons.${code}`, { defaultValue: t('reasons.unknown') }))
    }
  }

  return (
    <motion.li
      layout
      initial={{ opacity: 0, height: 0 }}
      animate={{ opacity: 1, height: 'auto' }}
      exit={{ opacity: 0, height: 0 }}
      transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
      className="overflow-hidden"
    >
      <div className="flex items-center gap-3 px-5 py-2.5 sm:px-6">
        <SidePill side={order.side} />
        <span className="num font-medium">{qty}</span>
        <span className="text-muted">{instrument.baseAsset}</span>
        <span className="text-sm text-muted">{t('orders.at')}</span>
        <span className="num font-medium">{price}</span>
        {distance && <span className="num hidden text-xs text-muted sm:inline">{formatPercent(distance, language)}</span>}
        <button
          type="button"
          onClick={onCancel}
          disabled={cancel.isPending}
          aria-label={t('orders.cancelLabel', { side: t(`side.${order.side}`), qty, asset: instrument.baseAsset })}
          className="ms-auto inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm text-muted transition-colors hover:bg-fall/10 hover:text-fall"
        >
          {cancel.isPending ? <LoaderCircle className="size-3.5 animate-spin" aria-hidden /> : <X className="size-3.5" aria-hidden />}
          {t('orders.cancel')}
        </button>
      </div>
    </motion.li>
  )
}
