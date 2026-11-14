import clsx from 'clsx'
import { motion } from 'motion/react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHubStatus } from '../realtime/hub'
import { useMarket } from '../state/quotes'

/** Tells the user whether prices are live, simulated, or stale. Honesty about the data source matters here. */
/** Matches the server's stale-quote rule closely enough that "paused" here means market orders would be refused. */
const STALE_AFTER_MS = 5_000

function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])
  return now
}

export function ConnectionBadge() {
  const { t } = useTranslation()
  const hub = useHubStatus()
  const { status, receivedAt } = useMarket()
  const now = useNow(1_000)
  const simulated = status.source === 'Mock'
  const stale = receivedAt === null || now - receivedAt > STALE_AFTER_MS

  const state = hub !== 'live' ? hub : stale ? 'paused' : simulated ? 'simulated' : 'live'
  const label = t(`connection.${state}`)
  const title =
    state === 'live'
      ? t('connection.liveTitle', { source: status.source ?? 'Binance' })
      : state === 'simulated'
        ? t('connection.simulatedTitle')
        : state === 'paused'
          ? t('connection.pausedTitle')
          : label
  const dot = state === 'live' ? 'bg-rise' : state === 'simulated' || state === 'paused' ? 'bg-lantern' : state === 'offline' ? 'bg-fall' : 'bg-muted'

  return (
    <span title={title} className="inline-flex items-center gap-2 text-sm text-muted" role="status">
      <span className="relative flex size-2">
        {state === 'live' && (
          <motion.span
            className="absolute inset-0 rounded-full bg-rise"
            animate={{ scale: [1, 2.4], opacity: [0.6, 0] }}
            transition={{ duration: 1.8, repeat: Infinity, ease: 'easeOut' }}
          />
        )}
        <span className={clsx('relative size-2 rounded-full', dot)} />
      </span>
      <span className="hidden sm:inline">{label}</span>
    </span>
  )
}
