// App shell: header + internal state-based routing (doc 05 §3). The INITIAL route comes from
// the query params (once); from then on, React state. Screens are loaded lazily so the initial
// bundle doesn't drag in pdf.js (only sign/create/verify load it).

import { Suspense, lazy, useMemo, useState } from 'react';
import { makeStyles, tokens, Spinner } from '@fluentui/react-components';
import { useAppContext } from './app/PowerProvider';
import { useT } from './i18n/useT';
import { Header } from './components/Header';
import { parseRoute, type Screen, type Route } from './lib/navigation';

const Onboarding = lazy(() => import('./screens/onboarding/OnboardingScreen'));
const Verify = lazy(() => import('./screens/verify/VerifyScreen'));
const CreateWizard = lazy(() => import('./screens/create/CreateWizardScreen'));
const Placeholder = lazy(() => import('./screens/Placeholder'));

const useStyles = makeStyles({
  layout: { minHeight: '100vh', backgroundColor: tokens.colorNeutralBackground2 },
  main: { maxWidth: '960px', margin: '0 auto', padding: tokens.spacingVerticalL },
  center: { display: 'flex', justifyContent: 'center', paddingTop: tokens.spacingVerticalXXXL },
});

export function App(): JSX.Element {
  const s = useStyles();
  const { queryParams, user } = useAppContext();
  const { t, changeLang } = useT();

  const initialRoute = useMemo(() => parseRoute(queryParams), [queryParams]);
  const [route, setRoute] = useState<Route>(initialRoute);

  const navigate = (screen: Screen, txId?: string) =>
    setRoute(txId ? { screen, txId } : { screen });

  return (
    <div className={s.layout}>
      <Header
        appName={t('app.name')}
        userName={user.fullName ?? '—'}
        navLabels={{ dashboard: t('nav.dashboard'), create: t('nav.create'), verify: t('nav.verify') }}
        toggleLangLabel={t('app.languageToggle')}
        currentScreen={route.screen}
        onNavigate={(p) => navigate(p)}
        onToggleLang={changeLang}
      />
      <main className={s.main}>
        <Suspense fallback={<div className={s.center}><Spinner label={t('common.loading')} /></div>}>
          {renderScreen(route, navigate)}
        </Suspense>
      </main>
    </div>
  );
}

function renderScreen(route: Route, navigate: (p: Screen, txId?: string) => void): JSX.Element {
  switch (route.screen) {
    case 'onboarding':
      return <Onboarding onBack={() => navigate('dashboard')} />;
    case 'verify':
      return <Verify initialTxId={route.txId} />;
    case 'create':
      return <CreateWizard onExit={() => navigate('dashboard')} />;
    default:
      // dashboard / sign / detail arrive in the next batches
      return <Placeholder screen={route.screen} onGoToOnboarding={() => navigate('onboarding')} onGoToVerify={() => navigate('verify')} />;
  }
}
