import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MotionConfig } from 'motion/react'
import type { ReactNode } from 'react'
import { BrowserRouter } from 'react-router'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 2_000, retry: 1, refetchOnWindowFocus: true },
  },
})

/** App-wide providers in one place. */
export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/* reducedMotion="user": with the OS "reduce motion" setting, transforms become instant state changes. */}
        <MotionConfig reducedMotion="user">{children}</MotionConfig>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
