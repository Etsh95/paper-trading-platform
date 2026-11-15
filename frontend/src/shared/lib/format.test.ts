import { describe, expect, it } from 'vitest'
import { D } from './decimal'
import { formatDecimal, formatMoney, formatPercent } from './format'

describe('format', () => {
  it('formats decimal strings exactly, without float artefacts', () => {
    expect(formatDecimal('0.30000000000000004', 'en', 2, 20)).toBe('0.30000000000000004')
    expect(formatDecimal('12345678901234.12345678', 'en', 8)).toBe('12,345,678,901,234.12345678')
    expect(formatMoney(D('0.1').plus('0.2'), 'en')).toBe('0.30')
  })

  it('keeps Western digits in Arabic', () => {
    expect(formatMoney('1234.5', 'ar')).toMatch(/1.?234.50/)
    expect(formatMoney('1234.5', 'ar')).not.toMatch(/[٠-٩]/)
  })

  it('shows an explicit sign for signed values', () => {
    expect(formatMoney('12.5', 'en', true)).toBe('+12.50')
    expect(formatMoney('-12.5', 'en', true)).toBe('-12.50')
    expect(formatMoney('0', 'en', true)).toBe('0.00')
    expect(formatPercent(D('0.01234'), 'en')).toBe('+1.23%')
  })
})
