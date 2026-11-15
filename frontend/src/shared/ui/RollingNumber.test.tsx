import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RollingNumber } from './RollingNumber'

describe('RollingNumber', () => {
  it('exposes the plain value to assistive tech once', () => {
    render(<RollingNumber text="100,245.18" />)
    expect(screen.getByText('100,245.18')).toHaveClass('sr-only')
  })
})
