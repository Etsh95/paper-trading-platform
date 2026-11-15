import type { Decimal } from './decimal'

export type AppLanguage = 'en' | 'ar'

// Arabic UI keeps Western digits ("-u-nu-latn") so prices read identically in both languages.
const locales: Record<AppLanguage, string> = { en: 'en-US', ar: 'ar-u-nu-latn' }

const cache = new Map<string, Intl.NumberFormat>()

function formatter(lang: AppLanguage, options: Intl.NumberFormatOptions) {
  const key = `${lang}|${JSON.stringify(options)}`
  let f = cache.get(key)
  if (!f) {
    f = new Intl.NumberFormat(locales[lang], options)
    cache.set(key, f)
  }
  return f
}

type Numeric = string | Decimal

/**
 * Formats a decimal *string* exactly. Intl.NumberFormat accepts numeric strings and formats them without
 * converting to a double, so 0.1 + 0.2 style artefacts never reach the screen.
 */
export function formatDecimal(value: Numeric, lang: AppLanguage, minFraction: number, maxFraction = minFraction, signed = false): string {
  const text = typeof value === 'string' ? value : value.toFixed()
  return formatter(lang, {
    minimumFractionDigits: minFraction,
    maximumFractionDigits: maxFraction,
    signDisplay: signed ? 'exceptZero' : 'auto',
  }).format(text as unknown as number) // the lib typing lags behind the spec, which accepts strings
}

export const formatMoney = (value: Numeric, lang: AppLanguage, signed = false) => formatDecimal(value, lang, 2, 2, signed)

export const formatPercent = (fraction: Decimal, lang: AppLanguage) =>
  formatter(lang, { style: 'percent', minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: 'exceptZero' })
    .format(fraction.toFixed(8) as unknown as number)

export function formatTime(iso: string, lang: AppLanguage) {
  return new Intl.DateTimeFormat(lang === 'ar' ? 'ar-u-nu-latn' : 'en-GB', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(iso))
}

/** Trend direction of a signed value, for colouring. */
export const signOf = (value: Numeric): 'up' | 'down' | 'flat' => {
  const s = typeof value === 'string' ? Math.sign(Number(value)) : value.comparedTo(0)
  return s > 0 ? 'up' : s < 0 ? 'down' : 'flat'
}
