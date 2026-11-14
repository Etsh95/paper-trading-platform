import clsx from 'clsx'
import { Moon, Sun } from 'lucide-react'
import { motion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router'
import { useLanguage } from '../i18n'
import type { Theme } from '../lib/theme'
import { ConnectionBadge } from './ConnectionBadge'

const links = [
  { to: '/', key: 'nav.trade' },
  { to: '/portfolio', key: 'nav.portfolio' },
  { to: '/history', key: 'nav.history' },
] as const

export function AppHeader({ theme, onToggleTheme }: { theme: Theme; onToggleTheme: () => void }) {
  const { t } = useTranslation()
  const { toggle } = useLanguage()

  return (
    <header className="sticky top-0 z-20 border-b border-rule bg-harbor/85 backdrop-blur-md">
      <div className="mx-auto flex h-14 max-w-[1400px] items-center gap-4 px-4 sm:gap-8 sm:px-6">
        <NavLink to="/" className="flex items-center gap-2 font-semibold">
          <Logo />
          <span className="hidden sm:inline">{t('app.name')}</span>
        </NavLink>

        <nav aria-label={t('nav.main')} className="flex h-full items-stretch gap-1">
          {links.map((link) => (
            <NavLink key={link.to} to={link.to} end className="relative flex items-center px-2.5 text-sm font-medium sm:px-3">
              {({ isActive }) => (
                <>
                  <span className={clsx('transition-colors', isActive ? 'text-fog' : 'text-muted hover:text-fog')}>{t(link.key)}</span>
                  {isActive && (
                    <motion.span
                      layoutId="nav-underline"
                      className="absolute inset-x-2 -bottom-px h-0.5 rounded-full bg-lantern"
                      transition={{ type: 'spring', stiffness: 500, damping: 40 }}
                    />
                  )}
                </>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="ms-auto flex items-center gap-2 sm:gap-4">
          <ConnectionBadge />
          <button
            type="button"
            onClick={toggle}
            aria-label={t('prefs.languageLabel')}
            className="rounded-md px-2 py-1 text-sm font-medium text-muted transition-colors hover:bg-hover hover:text-fog"
          >
            {t('prefs.language')}
          </button>
          <button
            type="button"
            onClick={onToggleTheme}
            aria-label={theme === 'dark' ? t('prefs.toLight') : t('prefs.toDark')}
            className="relative grid size-8 place-items-center overflow-hidden rounded-md text-muted transition-colors hover:bg-hover hover:text-fog"
          >
            <motion.span
              key={theme}
              initial={{ rotate: -90, opacity: 0 }}
              animate={{ rotate: 0, opacity: 1 }}
              transition={{ type: 'spring', stiffness: 300, damping: 20 }}
            >
              {theme === 'dark' ? <Sun className="size-4" aria-hidden /> : <Moon className="size-4" aria-hidden />}
            </motion.span>
          </button>
        </div>
      </div>
    </header>
  )
}

function Logo() {
  return (
    <svg viewBox="0 0 32 32" className="size-7" aria-hidden>
      <rect width="32" height="32" rx="7" fill="var(--deck)" stroke="var(--rule)" />
      <path d="M7 21.5 13 15l4.5 4L25 10.5" fill="none" stroke="var(--lantern)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
