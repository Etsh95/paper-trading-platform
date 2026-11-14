import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Instrument, OrderStatus, PlaceOrderInput } from '../api/types'

export const keys = {
  config: ['config'] as const,
  instruments: ['instruments'] as const,
  portfolio: ['portfolio'] as const,
  orders: (status?: OrderStatus) => ['orders', status ?? 'all'] as const,
  trades: ['trades'] as const,
}

export const useConfig = () => useQuery({ queryKey: keys.config, queryFn: api.config, staleTime: Infinity })

export const useInstruments = () =>
  useQuery({ queryKey: keys.instruments, queryFn: api.instruments, staleTime: Infinity })

export const useInstrumentMap = () => {
  const { data } = useInstruments()
  return new Map<string, Instrument>((data ?? []).map((i) => [i.symbol, i]))
}

/** Server-confirmed balances. Pushed events invalidate it; the interval is only a safety net. */
export const usePortfolio = () =>
  useQuery({ queryKey: keys.portfolio, queryFn: api.portfolio, refetchInterval: 15_000 })

export const useOrders = (status?: OrderStatus) =>
  useQuery({ queryKey: keys.orders(status), queryFn: () => api.orders(status), refetchInterval: 30_000 })

export const useTrades = () => useQuery({ queryKey: keys.trades, queryFn: api.trades, refetchInterval: 30_000 })

/**
 * Refetches account data. Returns a promise that settles once the refetch is done: mutations return it from
 * onSettled, so they only complete after balances are current. Otherwise the ticket could validate the next
 * order against stale numbers (e.g. still-reserved coins right after a cancel) on a slow connection.
 */
export function useInvalidateAccount() {
  const client = useQueryClient()
  return () =>
    Promise.all([
      client.invalidateQueries({ queryKey: keys.portfolio }),
      client.invalidateQueries({ queryKey: ['orders'] }),
      client.invalidateQueries({ queryKey: keys.trades }),
    ])
}

export function usePlaceOrder() {
  const invalidate = useInvalidateAccount()
  return useMutation({
    mutationFn: ({ input, key }: { input: PlaceOrderInput; key: string }) => api.placeOrder(input, key),
    onSettled: invalidate,
  })
}

export function useCancelOrder() {
  const invalidate = useInvalidateAccount()
  return useMutation({ mutationFn: api.cancelOrder, onSettled: invalidate })
}
