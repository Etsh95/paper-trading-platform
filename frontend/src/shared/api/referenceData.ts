import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api } from './client'
import { queryKeys } from './queryKeys'
import type { Instrument } from './types'

/** Trading rules (fee rate, starting cash). Static for the app's lifetime. */
export const useConfig = () => useQuery({ queryKey: queryKeys.config, queryFn: api.config, staleTime: Infinity })

/** Tradable instruments and their tick/step/min-notional filters. */
export const useInstruments = () =>
  useQuery({ queryKey: queryKeys.instruments, queryFn: api.instruments, staleTime: Infinity })

export function useInstrumentMap(): ReadonlyMap<string, Instrument> {
  const { data } = useInstruments()
  return useMemo(() => new Map((data ?? []).map((i) => [i.symbol, i])), [data])
}
