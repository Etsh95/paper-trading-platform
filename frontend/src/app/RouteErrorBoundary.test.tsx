import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import '@/shared/i18n'
import { RouteErrorBoundary } from './RouteErrorBoundary'

function FailingPage(): never {
  throw new Error('Failed to fetch dynamically imported module')
}

describe('RouteErrorBoundary', () => {
  it('shows a reload option instead of a blank screen when a page fails to load', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    render(
      <RouteErrorBoundary>
        <FailingPage />
      </RouteErrorBoundary>,
    )

    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument()
  })
})
