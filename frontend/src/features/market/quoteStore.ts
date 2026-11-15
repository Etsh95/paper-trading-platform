import { useSyncExternalStore } from 'react'
import type { MarketStatus, Quote } from '@/shared/api/types'
import { D } from '@/shared/lib/decimal'

const HISTORY = 90

export interface LiveQuote extends Quote {
  /** Direction of the last mid-price change, drives the flash colour. */
  direction: 'up' | 'down' | 'flat'
  /** Incremented on every change so components can re-trigger their flash. */
  tick: number
  /** Mid price of the first quote seen this session, for the change column. */
  openMid: string
  /** Recent mids as numbers. Display-only (sparkline drawing); never used for money. */
  history: number[]
}

interface Snapshot {
  quotes: ReadonlyMap<string, LiveQuote>
  status: MarketStatus
  /** Browser clock time of the last quote that changed something. Avoids comparing server and client clocks. */
  receivedAt: number | null
}

const isNewer = (candidate: Quote, current: Quote) =>
  candidate.timestamp !== current.timestamp && Date.parse(candidate.timestamp) >= Date.parse(current.timestamp)

let snapshot: Snapshot = { quotes: new Map(), status: { source: null, connected: false, lastQuoteAt: null }, receivedAt: null }
const listeners = new Set<() => void>()

function emit(next: Snapshot) {
  snapshot = next
  listeners.forEach((l) => l())
}

/** Small external store: quotes change several times a second and live outside React Query on purpose. */
export const quoteStore = {
  apply(quotes: Quote[], source?: string) {
    if (quotes.length === 0) return
    const next = new Map(snapshot.quotes)
    let changed = false
    for (const q of quotes) {
      const prev = next.get(q.symbol)
      // Two sources feed this store (the REST snapshot and the hub). Never let an older quote replace a newer one.
      if (prev && !isNewer(q, prev)) continue
      changed = true
      const cmp = prev ? D(q.mid).comparedTo(prev.mid) : 0
      next.set(q.symbol, {
        ...q,
        direction: cmp > 0 ? 'up' : cmp < 0 ? 'down' : prev?.direction ?? 'flat',
        tick: cmp === 0 ? prev?.tick ?? 0 : (prev?.tick ?? 0) + 1,
        openMid: prev?.openMid ?? q.mid,
        history: [...(prev?.history ?? []), Number(q.mid)].slice(-HISTORY),
      })
    }
    if (!changed) return
    const last = quotes[quotes.length - 1]!
    emit({
      quotes: next,
      status: { source: source ?? snapshot.status.source, connected: true, lastQuoteAt: last.timestamp },
      receivedAt: Date.now(),
    })
  },

  setStatus(status: MarketStatus) {
    emit({ ...snapshot, status })
  },

  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },

  get: () => snapshot,
}

export const useMarket = () => useSyncExternalStore(quoteStore.subscribe, quoteStore.get)

export const useQuote = (symbol: string | undefined) => {
  const { quotes } = useMarket()
  return symbol ? quotes.get(symbol) : undefined
}
