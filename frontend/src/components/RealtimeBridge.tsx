import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { onSessionChange } from '../api/session'
import type { Trade } from '../api/types'
import { useLanguage } from '../i18n'
import { placesOf } from '../lib/decimal'
import { formatDecimal } from '../lib/format'
import { startHub } from '../realtime/hub'
import { api } from '../api/client'
import { keys, useInstrumentMap } from '../state/queries'
import { quoteStore } from '../state/quotes'

/** Wires the SignalR stream into the app: quotes into the quote store, account events into React Query. */
export function RealtimeBridge() {
  const client = useQueryClient()
  const { t } = useTranslation()
  const { language } = useLanguage()
  const instruments = useInstrumentMap()

  // Initial snapshot (so the table isn't empty until the first push) and the feed source label.
  const { data: snapshot } = useQuery({ queryKey: ['quotes-snapshot'], queryFn: api.quotes, refetchInterval: 30_000 })
  useEffect(() => {
    if (snapshot) {
      quoteStore.apply(snapshot.quotes, snapshot.status.source ?? undefined)
      quoteStore.setStatus(snapshot.status)
    }
  }, [snapshot])

  // Handlers read the latest translations/instruments through a ref so the connection isn't rebuilt on change.
  const latest = useRef({ t, language, instruments })
  useEffect(() => {
    latest.current = { t, language, instruments }
  })

  useEffect(() => {
    const refreshAccount = () => {
      void client.invalidateQueries({ queryKey: keys.portfolio })
      void client.invalidateQueries({ queryKey: ['orders'] })
      void client.invalidateQueries({ queryKey: keys.trades })
    }

    const announceFill = (trade: Trade) => {
      const { t: tr, language: lang, instruments: map } = latest.current
      const instrument = map.get(trade.symbol)
      if (!instrument) return
      toast.success(
        tr(trade.side === 'Buy' ? 'toast.bought' : 'toast.sold', {
          qty: formatDecimal(trade.quantity, lang, 0, placesOf(instrument.stepSize)),
          asset: instrument.baseAsset,
          price: formatDecimal(trade.price, lang, placesOf(instrument.tickSize)),
        }),
      )
    }

    const stop = startHub({
      quotes: (quotes) => quoteStore.apply(quotes),
      orderUpdated: refreshAccount,
      tradeExecuted: (trade) => {
        refreshAccount()
        announceFill(trade)
      },
    })

    // A different account replaced the current one (server restart or "start over"): reset account data so
    // mounted views refetch for the new account. Reset rather than remove: removing a query that a component is
    // observing leaves that component without data until something else triggers a fetch.
    let firstSession = true
    const unsubscribe = onSessionChange((_, reason) => {
      if (firstSession && reason === 'created') {
        firstSession = false
        return
      }
      firstSession = false
      void client.resetQueries({ queryKey: ['orders'] })
      void client.resetQueries({ queryKey: keys.trades })
      void client.resetQueries({ queryKey: keys.portfolio })
      if (reason === 'reset') toast(latest.current.t('toast.sessionReset'))
    })

    return () => {
      stop()
      unsubscribe()
    }
  }, [client])

  return null
}
