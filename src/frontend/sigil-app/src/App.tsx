// App shell: header + internal state-based routing (doc 05 §3). The INITIAL route comes from
// the query params (once); from then on, React state. Screens are loaded lazily so the initial
// bundle doesn't drag in pdf.js (only sign/create/verify load it).

import { Suspense, lazy, useEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Spinner } from '@fluentui/react-components';
import { useAppContext } from './app/PowerProvider';
import { useT } from './i18n/useT';
import { Header } from './components/Header';
import { parseRoute, type Screen, type Route } from './lib/navigation';

const Dashboard = lazy(() => import('./screens/dashboard/DashboardScreen'));
const Detail = lazy(() => import('./screens/detail/DetailScreen'));
const Sign = lazy(() => import('./screens/sign/SignScreen'));
const Onboarding = lazy(() => import('./screens/onboarding/OnboardingScreen'));
const Verify = lazy(() => import('./screens/verify/VerifyScreen'));
const CreateWizard = lazy(() => import('./screens/create/CreateWizardScreen'));
const Documents = lazy(() => import('./screens/documents/DocumentsScreen'));
const Placeholder = lazy(() => import('./screens/Placeholder'));

const useStyles = makeStyles({
  layout: { minHeight: '100vh', backgroundColor: tokens.colorNeutralBackground2 },
  // Content is width-capped and centered; on phones it's full-width (padding only) since the cap is
  // above any phone width. PDF-heavy screens (create/sign/detail) get a wider cap so the document
  // uses the available space instead of being squeezed into a narrow column.
  main: { maxWidth: '960px', margin: '0 auto', padding: tokens.spacingVerticalL, width: '100%', boxSizing: 'border-box' },
  mainWide: { maxWidth: '1400px' },
  center: { display: 'flex', justifyContent: 'center', paddingTop: tokens.spacingVerticalXXXL },
});

// Screens whose main content is a PDF viewer — they benefit from the full width of large displays.
const WIDE_SCREENS: ReadonlySet<Screen> = new Set(['create', 'sign', 'detail', 'documents']);

export function App(): JSX.Element {
  const s = useStyles();
  const { queryParams, user, ready } = useAppContext();
  const { t, lang, changeLang } = useT();

  const [route, setRoute] = useState<Route>(() => parseRoute(queryParams));
  // The hosted Power Apps player provides the deep-link params (screen/txId — e.g. from a verify
  // QR) via getContext AFTER the first render, so the initial route (from the iframe URL) misses
  // them. Apply the deep link once the context is ready — only if it points somewhere other than
  // the default, so we never clobber a user who already navigated.
  const appliedDeepLink = useRef(false);
  useEffect(() => {
    if (!ready || appliedDeepLink.current) return;
    appliedDeepLink.current = true;
    const r = parseRoute(queryParams);
    if (r.screen !== 'dashboard' || r.txId) setRoute(r);
  }, [ready, queryParams]);
  // When onboarding is opened mid-flow (e.g. from Sign without a Master Signature), remember where
  // to return so the user lands back on that screen after configuring — doc 05 §4.3 "auto-return".
  const [returnTo, setReturnTo] = useState<Route | undefined>(undefined);

  const navigate = (screen: Screen, txId?: string, signatureVersion?: number) => {
    if (screen !== 'onboarding') setReturnTo(undefined); // drop any stale sign→onboarding return target
    const next: Route = { screen };
    if (txId) next.txId = txId;
    if (signatureVersion != null) next.signatureVersion = signatureVersion;
    setRoute(next);
  };
  const openOnboarding = (ret?: Route) => { setReturnTo(ret); setRoute({ screen: 'onboarding' }); };
  const leaveOnboarding = () => { const r = returnTo; setReturnTo(undefined); setRoute(r ?? { screen: 'dashboard' }); };

  return (
    <div className={s.layout}>
      <Header
        appName={t('app.name')}
        userName={user.fullName ?? '—'}
        navLabels={{ dashboard: t('nav.dashboard'), documents: t('nav.documents'), create: t('nav.create'), verify: t('nav.verify'), signature: t('nav.signature') }}
        toggleLangLabel={t('app.languageToggle')}
        langCode={lang.toUpperCase()}
        menuLabel={t('nav.menu')}
        currentScreen={route.screen}
        onNavigate={(p) => navigate(p)}
        onToggleLang={changeLang}
      />
      <main className={`${s.main} ${WIDE_SCREENS.has(route.screen) ? s.mainWide : ''}`}>
        <Suspense fallback={<div className={s.center}><Spinner label={t('common.loading')} /></div>}>
          {renderScreen(route, navigate, { openOnboarding, leaveOnboarding })}
        </Suspense>
      </main>
    </div>
  );
}

interface Nav { openOnboarding: (ret?: Route) => void; leaveOnboarding: () => void }

function renderScreen(route: Route, navigate: (p: Screen, txId?: string, signatureVersion?: number) => void, nav: Nav): JSX.Element {
  switch (route.screen) {
    case 'onboarding':
      return <Onboarding onBack={nav.leaveOnboarding} onOpenDocuments={(version) => navigate('documents', undefined, version)} />;
    case 'documents':
      return <Documents onOpen={(txId) => navigate('detail', txId)} initialSignatureVersion={route.signatureVersion} />;
    case 'verify':
      return <Verify initialTxId={route.txId} />;
    case 'create':
      return <CreateWizard onExit={() => navigate('dashboard')} />;
    case 'dashboard':
      return <Dashboard onNavigate={navigate} />;
    case 'detail':
      return route.txId
        ? <Detail txId={route.txId} onBack={() => navigate('dashboard')} onVerify={(txId) => navigate('verify', txId)} />
        : <Placeholder screen={route.screen} onGoToOnboarding={() => navigate('onboarding')} onGoToVerify={() => navigate('verify')} />;
    case 'sign':
      return route.txId
        ? <Sign txId={route.txId} onSigned={(txId) => navigate('detail', txId)} onNeedOnboarding={() => nav.openOnboarding(route)} onBack={() => navigate('dashboard')} />
        : <Placeholder screen={route.screen} onGoToOnboarding={() => nav.openOnboarding()} onGoToVerify={() => navigate('verify')} />;
    default:
      return <Placeholder screen={route.screen} onGoToOnboarding={() => navigate('onboarding')} onGoToVerify={() => navigate('verify')} />;
  }
}
