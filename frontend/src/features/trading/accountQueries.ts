import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import { accountQueryKeys, queryKeys } from '@/shared/api/queryKeys'
import type { OrderStatus, PlaceOrderInput } from '@/shared/api/types'

export const useOrders = (status?: OrderStatus) =>
  useQuery({ queryKey: queryKeys.orders(status), queryFn: () => api.orders(status), refetchInterval: 30_000 })

export const useTrades = () => useQuery({ queryKey: queryKeys.trades, queryFn: api.trades, refetchInterval: 30_000 })

/**
 * Refetches account data. Returns a promise that settles once the refetch is done: mutations return it from
 * onSettled, so they only complete after balances are current. Otherwise the ticket could validate the next
 * order against stale numbers (e.g. still-reserved coins right after a cancel) on a slow connection.
 */
export function useInvalidateAccount() {
  const client = useQueryClient()
  return () => Promise.all(accountQueryKeys.map((queryKey) => client.invalidateQueries({ queryKey })))
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
