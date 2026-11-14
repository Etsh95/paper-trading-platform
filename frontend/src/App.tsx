import { AnimatePresence, MotionConfig, motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { Route, Routes, useLocation } from 'react-router'
import { Toaster } from 'sonner'
import { AppHeader } from './components/AppHeader'
import { RealtimeBridge } from './components/RealtimeBridge'
import { useLanguage } from './i18n'
import { useTheme } from './lib/theme'
import { HistoryPage } from './pages/HistoryPage'
import { PortfolioPage } from './pages/PortfolioPage'
import { TradePage } from './pages/TradePage'

export default function App() {
  const { t } = useTranslation()
  const { isRtl } = useLanguage()
  const { theme, toggle } = useTheme()
  const location = useLocation()

  return (
    // reducedMotion="user": with the OS "reduce motion" setting, transforms become instant state changes.
    <MotionConfig reducedMotion="user">
      <RealtimeBridge />
      <AppHeader theme={theme} onToggleTheme={toggle} />
      <main className="mx-auto max-w-[1400px] px-4 py-4 sm:px-6 sm:py-6">
        <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={location.pathname}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.15 }}
          >
            <Routes location={location}>
              <Route path="/" element={<TradePage />} />
              <Route path="/portfolio" element={<PortfolioPage />} />
              <Route path="/history" element={<HistoryPage />} />
              <Route path="*" element={<TradePage />} />
            </Routes>
          </motion.div>
        </AnimatePresence>
      </main>
      <footer className="mx-auto max-w-[1400px] px-4 pb-8 text-xs text-muted sm:px-6">{t('app.footer')}</footer>
      <Toaster
        theme={theme}
        dir={isRtl ? 'rtl' : 'ltr'}
        position={isRtl ? 'bottom-left' : 'bottom-right'}
        toastOptions={{
          style: {
            background: 'var(--deck)',
            color: 'var(--fog)',
            border: '1px solid var(--rule)',
            fontFamily: 'var(--font-sans)',
          },
        }}
      />
    </MotionConfig>
  )
}
