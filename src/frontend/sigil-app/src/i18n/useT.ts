// Translation hook + language toggle. Uses react-i18next under the hood; `t` accepts keys
// with dot notation (e.g. 'onboarding.title') and interpolation ({{date}}).

import { useTranslation } from 'react-i18next';
import { saveLang } from './index';

export function useT() {
  const { t, i18n } = useTranslation();
  const lang = (i18n.language.startsWith('es') ? 'es' : 'en') as 'es' | 'en';
  const changeLang = () => {
    const next = lang === 'es' ? 'en' : 'es';
    void i18n.changeLanguage(next);
    saveLang(next);
  };
  return { t, lang, changeLang };
}
