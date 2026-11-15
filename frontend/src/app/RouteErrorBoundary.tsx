import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * Catches render errors in a page, including a lazily loaded page chunk that fails to download (typically a tab
 * left open across a deploy, whose old chunk no longer exists). Offers a reload instead of a blank screen.
 * Keyed by route in the shell, so navigating elsewhere clears the error.
 */
export class RouteErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Page failed to render', error, info.componentStack)
  }

  render() {
    return this.state.failed ? <PageError /> : this.props.children
  }
}

function PageError() {
  const { t } = useTranslation()
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border border-rule bg-deck p-6">
      <h1 className="text-lg font-semibold">{t('errors.pageTitle')}</h1>
      <p className="text-sm text-muted">{t('errors.pageBody')}</p>
      <button
        type="button"
        onClick={() => window.location.reload()}
        className="rounded-lg bg-lantern px-4 py-2 text-sm font-semibold text-lantern-ink hover:brightness-110"
      >
        {t('errors.reload')}
      </button>
    </div>
  )
}
