import clsx from 'clsx'
import { AnimatePresence, motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { useTrades } from '@/features/trading'
import { useInstrumentMap } from '@/shared/api/referenceData'
import type { Trade } from '@/shared/api/types'
import { useLanguage } from '@/shared/i18n'
import { placesOf } from '@/shared/lib/decimal'
import { formatDecimal, formatMoney, formatTime, signOf } from '@/shared/lib/format'
import { SidePill } from '@/shared/ui/StatusPill'

export function TradesHistory() {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const { data: trades, isLoading } = useTrades()
  const instruments = useInstrumentMap()

  if (!isLoading && trades?.length === 0) return <p className="border-t border-rule px-5 py-6 text-sm text-muted sm:px-6">{t('history.noTrades')}</p>

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[44rem] text-sm">
        <thead className="text-xs text-muted">
          <tr className="border-y border-rule">
            <th className="px-5 py-2 text-start font-normal sm:px-6">{t('cols.time')}</th>
            <th className="px-3 py-2 text-start font-normal">{t('cols.market')}</th>
            <th className="px-3 py-2 text-start font-normal">{t('cols.side')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.quantity')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.price')}</th>
            <th className="px-3 py-2 text-end font-normal">{t('cols.fee')}</th>
            <th className="px-5 py-2 text-end font-normal sm:px-6">{t('cols.realized')}</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-rule">
          <AnimatePresence initial={false}>
            {trades?.map((trade: Trade) => {
              const ins = instruments.get(trade.symbol)
              const sign = signOf(trade.realizedPnl)
              return (
                <motion.tr key={trade.id} layout="position" initial={{ opacity: 0 }} animate={{ opacity: 1 }}>
                  <td className="num px-5 py-2.5 whitespace-nowrap text-muted sm:px-6">{formatTime(trade.executedAt, language)}</td>
                  <td className="px-3 py-2.5 font-medium">{ins ? `${ins.baseAsset}/${ins.quoteAsset}` : trade.symbol}</td>
                  <td className="px-3 py-2.5">
                    <SidePill side={trade.side} />
                  </td>
                  <td className="num px-3 py-2.5 text-end">{formatDecimal(trade.quantity, language, 0, ins ? placesOf(ins.stepSize) : 8)}</td>
                  <td className="num px-3 py-2.5 text-end">{formatDecimal(trade.price, language, ins ? placesOf(ins.tickSize) : 2)}</td>
                  <td className="num px-3 py-2.5 text-end text-muted">{formatDecimal(trade.fee, language, 2, 8)}</td>
                  <td className={clsx('num px-5 py-2.5 text-end sm:px-6', sign === 'up' ? 'text-rise' : sign === 'down' ? 'text-fall' : 'text-muted')}>
                    {trade.side === 'Sell' ? formatMoney(trade.realizedPnl, language, true) : '–'}
                  </td>
                </motion.tr>
              )
            })}
          </AnimatePresence>
        </tbody>
      </table>
    </div>
  )
}
