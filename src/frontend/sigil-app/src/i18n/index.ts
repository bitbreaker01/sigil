// i18n configuration (doc 05 §7): es/en BUNDLED (no remote loading — CSP forbids fetch).
// Initial language: navigator.language (default en, verified fact: getContext doesn't expose
// language), toggle persisted in localStorage.

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { es } from './es';
import { en } from './en';

const LANG_KEY = 'sigil.lang';

export function initialLang(): 'es' | 'en' {
  const saved = typeof localStorage !== 'undefined' ? localStorage.getItem(LANG_KEY) : null;
  if (saved === 'es' || saved === 'en') return saved;
  const browser = typeof navigator !== 'undefined' ? navigator.language : 'en';
  return browser.toLowerCase().startsWith('es') ? 'es' : 'en';
}

export function saveLang(lang: 'es' | 'en'): void {
  if (typeof localStorage !== 'undefined') localStorage.setItem(LANG_KEY, lang);
}

export function initI18n(): typeof i18n {
  if (!i18n.isInitialized) {
    void i18n.use(initReactI18next).init({
      resources: {
        es: { translation: es },
        en: { translation: en },
      },
      lng: initialLang(),
      fallbackLng: 'en',
      interpolation: { escapeValue: false }, // React already escapes
      returnNull: false,
    });
  }
  return i18n;
}

export { es, en };
