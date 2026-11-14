import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useSyncExternalStore } from 'react'
import { currentToken, ensureSession, onSessionChange } from '../api/session'
import type { Order, Quote, Trade } from '../api/types'
import { apiUrl } from '../api/url'

export type HubStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

export interface HubHandlers {
  quotes(quotes: Quote[]): void
  orderUpdated(order: Order): void
  tradeExecuted(trade: Trade): void
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
    await ensureSession()
    if (stopped || gen !== generation) return
    const isCurrent = () => !stopped && gen === generation
    const retryLater = () => setTimeout(() => isCurrent() && void connect(), 5_000)

    const c = new HubConnectionBuilder()
      .withUrl(apiUrl('/hubs/market'), { accessTokenFactory: () => currentToken() ?? '' })
      .withAutomaticReconnect([0, 1_000, 2_000, 5_000, 10_000, 15_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build()

    c.on('quotes', handlers.quotes)
    c.on('orderUpdated', handlers.orderUpdated)
    c.on('tradeExecuted', handlers.tradeExecuted)
    c.onreconnecting(() => isCurrent() && setStatus('reconnecting'))
    c.onreconnected(() => isCurrent() && setStatus('live'))
    c.onclose(() => {
      if (!isCurrent()) return
      setStatus('offline')
      retryLater()
    })

    connection = c
    setStatus('connecting')
    try {
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

  // New session (server restart or "start over"): reconnect so the new token joins the right account group.
  const unsubscribe = onSessionChange(() => {
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
