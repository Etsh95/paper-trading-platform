import clsx from 'clsx'
import { AnimatePresence, motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { OrderStatus } from '../api/types'

const styles: Record<OrderStatus, string> = {
  Open: 'text-lantern bg-lantern/12',
  Filled: 'text-rise bg-rise/12',
  Cancelled: 'text-muted bg-hover',
  Rejected: 'text-fall bg-fall/12',
}

/** Status badge; when an order's status changes in place, the new state pops in. */
export function StatusPill({ status }: { status: OrderStatus }) {
  const { t } = useTranslation()
  return (
    <span className="inline-grid">
      <AnimatePresence mode="popLayout" initial={false}>
        <motion.span
          key={status}
          initial={{ opacity: 0, scale: 0.8 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.8 }}
          transition={{ duration: 0.22 }}
          className={clsx('rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', styles[status])}
        >
          {t(`status.${status}`)}
        </motion.span>
      </AnimatePresence>
    </span>
  )
}

export function SidePill({ side }: { side: 'Buy' | 'Sell' }) {
  const { t } = useTranslation()
  return <span className={clsx('font-medium', side === 'Buy' ? 'text-rise' : 'text-fall')}>{t(`side.${side}`)}</span>
}
