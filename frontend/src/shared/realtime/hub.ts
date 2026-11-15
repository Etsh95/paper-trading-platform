import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useSyncExternalStore } from 'react'
import { currentToken, ensureSession, onSessionChange } from '@/shared/api/session'
import type { Order, Quote, Trade } from '@/shared/api/types'
import { apiUrl } from '@/shared/api/url'

export type HubStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

export interface HubHandlers {
  quotes(quotes: Quote[]): void
  orderUpdated(order: Order): void
  tradeExecuted(trade: Trade): void
  /** The connection came back after a drop: anything pushed in between was missed, so refresh. */
  reconnected(): void
}

let status: HubStatus = 'connecting'
const statusListeners = new Set<() => void>()
const setStatus = (next: HubStatus) => {
  status = next
  statusListeners.forEach((l) => l())
}

export const useHubStatus = () =>
  useSyncExternalStore(
    (l) => {
      statusListeners.add(l)
      return () => statusListeners.delete(l)
    },
    () => status,
  )

/**
 * One SignalR connection for the app: public quote stream plus this account's order/fill events.
 * Reconnects automatically, and restarts when the session changes so the new token joins the right group.
 */
export function startHub(handlers: HubHandlers): () => void {
  let connection: HubConnection | null = null
  let stopped = false
  // Each (re)connect gets a generation; callbacks from superseded connections are ignored.
  let generation = 0

  const connect = async () => {
    const gen = ++generation
    const isCurrent = () => !stopped && gen === generation
    const retryLater = () => setTimeout(() => isCurrent() && void connect(), 5_000)

    setStatus('connecting')
    try {
      // Inside the try: if the session can't be created (API down at startup), retry instead of giving up.
      await ensureSession()
      if (!isCurrent()) return

      const c = new HubConnectionBuilder()
        .withUrl(apiUrl('/hubs/market'), { accessTokenFactory: () => currentToken() ?? '' })
        .withAutomaticReconnect([0, 1_000, 2_000, 5_000, 10_000, 15_000, 30_000])
        .configureLogging(LogLevel.Warning)
        .build()

      // Every callback is gated on the generation, so a connection being replaced (e.g. after "start over")
      // can't deliver the previous account's events into the current one.
      c.on('quotes', (quotes: Quote[]) => isCurrent() && handlers.quotes(quotes))
      c.on('orderUpdated', (order: Order) => isCurrent() && handlers.orderUpdated(order))
      c.on('tradeExecuted', (trade: Trade) => isCurrent() && handlers.tradeExecuted(trade))
      c.onreconnecting(() => isCurrent() && setStatus('reconnecting'))
      c.onreconnected(() => {
        if (!isCurrent()) return
        setStatus('live')
        handlers.reconnected()
      })
      c.onclose(() => {
        if (!isCurrent()) return
        setStatus('offline')
        retryLater()
      })

      connection = c
      await c.start()
      if (isCurrent()) setStatus('live')
      else void c.stop()
    } catch {
      if (isCurrent()) {
        setStatus('offline')
        retryLater()
      }
    }
  }

  // A different account (server restart or "start over"): reconnect so the new token joins the right account
  // group. The first session needs no reconnect; connect() is already waiting for it.
  const unsubscribe = onSessionChange((_, reason) => {
    if (reason === 'initial') return
    const old = connection
    connection = null
    void connect()
    if (old && old.state !== HubConnectionState.Disconnected) void old.stop()
  })

  void connect()

  return () => {
    stopped = true
    unsubscribe()
    void connection?.stop()
  }
}
