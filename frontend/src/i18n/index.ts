import i18n from 'i18next'
import { initReactI18next, useTranslation } from 'react-i18next'
import type { AppLanguage } from '../lib/format'
import ar from './ar.json'
import en from './en.json'

const STORAGE_KEY = 'paperdesk.lang'

function initialLanguage(): AppLanguage {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'en' || saved === 'ar') return saved
  } catch {
    // storage unavailable
  }
  return navigator.language.toLowerCase().startsWith('ar') ? 'ar' : 'en'
}

/** Keeps <html lang dir> in sync, which flips the whole layout through CSS logical properties. */
function applyDocumentLanguage(lang: AppLanguage) {
  document.documentElement.lang = lang
  document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr'
}

const lang = initialLanguage()
applyDocumentLanguage(lang)

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, ar: { translation: ar } },
  lng: lang,
  fallbackLng: 'en',
  interpolation: { escapeValue: false }, // React escapes already
})

i18n.on('languageChanged', (next) => {
  applyDocumentLanguage(next as AppLanguage)
  try {
    localStorage.setItem(STORAGE_KEY, next)
  } catch {
    // ignore
  }
})

/** Current language plus helpers, typed to the two supported languages. */
export function useLanguage() {
  const { i18n: instance } = useTranslation()
  const language = (instance.resolvedLanguage === 'ar' ? 'ar' : 'en') as AppLanguage
  return {
    language,
    isRtl: language === 'ar',
    toggle: () => void instance.changeLanguage(language === 'ar' ? 'en' : 'ar'),
  }
}

export default i18n
