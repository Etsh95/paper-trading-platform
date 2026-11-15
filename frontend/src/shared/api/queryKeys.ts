import type { OrderStatus } from './types'

/** One place for React Query keys, so invalidation from any layer (realtime, mutations) stays consistent. */
export const queryKeys = {
  config: ['config'] as const,
  instruments: ['instruments'] as const,
  quotesSnapshot: ['quotes-snapshot'] as const,
  portfolio: ['portfolio'] as const,
  orders: (status?: OrderStatus) => ['orders', status ?? 'all'] as const,
  allOrders: ['orders'] as const,
  trades: ['trades'] as const,
}

/** Everything that belongs to the signed-in paper account. */
export const accountQueryKeys = [queryKeys.portfolio, queryKeys.allOrders, queryKeys.trades] as const
