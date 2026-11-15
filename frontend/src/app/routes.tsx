import { lazy, Suspense } from 'react'
import { Route, Routes, type Location } from 'react-router'
import TradePage from '@/pages/TradePage'
import { RouteErrorBoundary } from './RouteErrorBoundary'

// The landing page ships in the main bundle; the others load on first visit (route-level code splitting).
const PortfolioPage = lazy(() => import('@/pages/PortfolioPage'))
const HistoryPage = lazy(() => import('@/pages/HistoryPage'))

export function AppRoutes({ location }: { location: Location }) {
  return (
    <RouteErrorBoundary key={location.pathname}>
      <Suspense fallback={<PageFallback />}>
        <Routes location={location}>
          <Route path="/" element={<TradePage />} />
          <Route path="/portfolio" element={<PortfolioPage />} />
          <Route path="/history" element={<HistoryPage />} />
          <Route path="*" element={<TradePage />} />
        </Routes>
      </Suspense>
    </RouteErrorBoundary>
  )
}

function PageFallback() {
  return <div className="h-64 animate-pulse rounded-xl border border-rule bg-deck" aria-hidden />
}
