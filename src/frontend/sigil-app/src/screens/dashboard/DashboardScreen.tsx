// Dashboard (doc 05 §4.1, RF-22/24): three tabs over useDashboard. First-run banner nudges the
// user to set up their Master Signature; sealing errors surface prominently with a retry CTA;
// "My participations" offers a completed-only filter with direct download of the final PDF (RF-24).

import { useState } from 'react';
import {
  makeStyles, tokens, Card, Text, Button, Spinner, Switch,
  TabList, Tab, MessageBar, MessageBarBody, MessageBarActions,
} from '@fluentui/react-components';
import { AddRegular, ArrowClockwiseRegular, ArrowDownloadRegular, SignatureRegular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import type { Screen } from '../../lib/navigation';
import { isCompleted, isSealing } from './dashboardModel';
import { useDashboard } from './useDashboard';
import { TransactionCard } from './TransactionCard';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  topbar: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  // Tabs never widen the page; if they don't fit they swipe (scrollbar hidden — the standard
  // mobile tab-strip pattern, not a visible bar).
  tabs: {
    minWidth: 0,
    maxWidth: '100%',
    overflowX: 'auto',
    scrollbarWidth: 'none',
    '::-webkit-scrollbar': { display: 'none' },
  },
  toolbar: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  empty: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: tokens.spacingVerticalM },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  hint: { color: tokens.colorNeutralForeground3 },
});

type TabKey = 'pending' | 'requests' | 'participations';

export default function DashboardScreen(props: { onNavigate: (screen: Screen, txId?: string) => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const d = useDashboard();
  const [tab, setTab] = useState<TabKey>('pending');
  const [onlyCompleted, setOnlyCompleted] = useState(false);
  const now = Date.now();

  const download = (tx: Parameters<typeof d.downloadFinal>[0]) => (
    <Button icon={<ArrowDownloadRegular />} onClick={() => void d.downloadFinal(tx)}>{t('common.download')}</Button>
  );

  const emptyState = (message: string) => (
    <Card className={s.empty}>
      <Text>{message}</Text>
      <Button appearance="primary" icon={<AddRegular />} onClick={() => props.onNavigate('create')}>{t('dashboard.createFirst')}</Button>
    </Card>
  );

  return (
    <div className={s.root}>
      {d.firstRun && (
        <MessageBar intent="warning">
          <MessageBarBody>{t('dashboard.setUpSignature')}</MessageBarBody>
          <MessageBarActions>
            <Button onClick={() => props.onNavigate('onboarding')}>{t('dashboard.goToOnboarding')}</Button>
          </MessageBarActions>
        </MessageBar>
      )}

      <div className={s.topbar}>
        <TabList className={s.tabs} selectedValue={tab} onTabSelect={(_e, data) => setTab(data.value as TabKey)}>
          <Tab value="pending">{t('dashboard.pendingTab')}</Tab>
          <Tab value="requests">{t('dashboard.myRequestsTab')}</Tab>
          <Tab value="participations">{t('dashboard.myParticipationsTab')}</Tab>
        </TabList>
        <div className={s.toolbar}>
          {d.isSealingActive && <Button icon={<ArrowClockwiseRegular />} onClick={d.refreshAll}>{t('dashboard.refresh')}</Button>}
          <Button appearance="primary" icon={<AddRegular />} onClick={() => props.onNavigate('create')}>{t('nav.create')}</Button>
        </div>
      </div>

      {d.actionError && (
        <MessageBar intent="error">
          <MessageBarBody>{t('dashboard.actionError')}</MessageBarBody>
          <MessageBarActions><Button appearance="transparent" onClick={d.dismissActionError}>{t('common.close')}</Button></MessageBarActions>
        </MessageBar>
      )}
      {d.sealingCapped && (
        <MessageBar intent="info">
          <MessageBarBody>{t('dashboard.stillProcessing')}</MessageBarBody>
          <MessageBarActions><Button onClick={d.refreshAll}>{t('dashboard.refresh')}</Button></MessageBarActions>
        </MessageBar>
      )}

      {d.loading ? (
        <Spinner label={t('common.loading')} />
      ) : d.error ? (
        <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>
      ) : tab === 'pending' ? (
        d.pending.length === 0 ? emptyState(t('dashboard.emptyPending')) : (
          <div className={s.list}>
            {d.pending.map(({ tx }) => (
              <TransactionCard key={tx.id} tx={tx} now={now} showDue onOpen={() => props.onNavigate('detail', tx.id)}>
                <Button appearance="primary" icon={<SignatureRegular />} onClick={() => props.onNavigate('sign', tx.id)}>
                  {t('dashboard.reviewAndSign')}
                </Button>
              </TransactionCard>
            ))}
          </div>
        )
      ) : tab === 'requests' ? (
        d.requests.length === 0 ? emptyState(t('dashboard.emptyMyRequests')) : (
          <div className={s.list}>
            {d.sealingErrors.length > 0 && (
              <div className={s.section}>
                <MessageBar intent="error"><MessageBarBody>{t('dashboard.needsAttention')}</MessageBarBody></MessageBar>
                {d.sealingErrors.map((tx) => (
                  <TransactionCard key={tx.id} tx={tx} now={now} onOpen={() => props.onNavigate('detail', tx.id)}>
                    <Button appearance="primary" icon={<ArrowClockwiseRegular />} onClick={() => void d.retrySealing(tx.id)}>
                      {t('dashboard.retrySealing')}
                    </Button>
                  </TransactionCard>
                ))}
              </div>
            )}
            {d.requests.filter((tx) => !d.sealingErrors.includes(tx)).map((tx) => (
              <TransactionCard key={tx.id} tx={tx} now={now} onOpen={() => props.onNavigate('detail', tx.id)}>
                {isCompleted(tx.state) && download(tx)}
                {isSealing(tx.state) && <Text size={200} className={s.hint}>{t('dashboard.sealingNote')}</Text>}
              </TransactionCard>
            ))}
          </div>
        )
      ) : (
        (() => {
          const list = onlyCompleted ? d.participations.filter((tx) => isCompleted(tx.state)) : d.participations;
          return (
            <div className={s.list}>
              <Switch checked={onlyCompleted} onChange={(_e, data) => setOnlyCompleted(!!data.checked)} label={t('dashboard.onlyCompleted')} />
              {list.length === 0 ? emptyState(t('dashboard.emptyParticipations')) : list.map((tx) => (
                <TransactionCard key={tx.id} tx={tx} now={now} onOpen={() => props.onNavigate('detail', tx.id)}>
                  {isCompleted(tx.state) && download(tx)}
                </TransactionCard>
              ))}
            </div>
          );
        })()
      )}
    </div>
  );
}
