import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api } from '@/shared/api/client'
import { queryKeys } from '@/shared/api/queryKeys'
import type { Position } from '@/shared/api/types'
import { D, type Decimal } from '@/shared/lib/decimal'
import { useMarket } from '@/features/market'

/** Server-confirmed balances. Pushed events invalidate it; the interval is only a safety net. */
export const usePortfolio = () =>
  useQuery({ queryKey: queryKeys.portfolio, queryFn: api.portfolio, refetchInterval: 15_000 })

export interface LivePosition extends Position {
  liveMark: string | null
  liveValue: Decimal
  liveUnrealized: Decimal
  available: Decimal
}

export interface LiveValuation {
  cash: Decimal
  availableCash: Decimal
  reservedCash: Decimal
  initialCash: Decimal
  positionsValue: Decimal
  equity: Decimal
  realizedPnl: Decimal
  unrealizedPnl: Decimal
  totalPnl: Decimal
  totalPnlFraction: Decimal
  totalFees: Decimal
  positions: LivePosition[]
}

/**
 * Balances come from the server (the source of truth); marks are re-applied from the live quote stream so
 * account value ticks in real time between refetches. Same rule as the server: longs are valued at the bid.
 */
export function useLiveValuation(): LiveValuation | undefined {
  const { data: portfolio } = usePortfolio()
  const { quotes } = useMarket()

  return useMemo(() => {
    if (!portfolio) return undefined
    const positions = portfolio.positions.map((p): LivePosition => {
      const mark = quotes.get(p.symbol)?.bid ?? p.markPrice
      const value = mark ? D(p.quantity).mul(mark) : D(p.marketValue)
      return { ...p, liveMark: mark, liveValue: value, liveUnrealized: value.minus(p.costBasis), available: D(p.quantity).minus(p.reservedQuantity) }
    })
    const positionsValue = positions.reduce((sum, p) => sum.plus(p.liveValue), D(0))
    const unrealized = positions.reduce((sum, p) => sum.plus(p.liveUnrealized), D(0))

    const realized = D(portfolio.realizedPnl)
    const totalPnl = realized.plus(unrealized)
    const initial = D(portfolio.initialCash)
    return {
      cash: D(portfolio.cash),
      availableCash: D(portfolio.availableCash),
      reservedCash: D(portfolio.reservedCash),
      initialCash: initial,
      positionsValue,
      equity: D(portfolio.cash).plus(positionsValue),
      realizedPnl: realized,
      unrealizedPnl: unrealized,
      totalPnl,
      totalPnlFraction: initial.isZero() ? D(0) : totalPnl.div(initial),
      totalFees: D(portfolio.totalFees),
      positions,
    }
  }, [portfolio, quotes])
}
