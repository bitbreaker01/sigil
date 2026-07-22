// Detail screen (doc 05 §4.4, RF-05/24/27/30, RNF-04): state + expiration, participant progress,
// event timeline, and the reject/cancel reason. Actions are gated by role/state — download the
// final PDF (completed), Cancel (creator, RF-30), Retry sealing (creator, sealing error). The tx
// polls while sealing (§5.1). Identity gating is a UI hint only; the backend enforces (doc 04 §3.3).

import { useState } from 'react';
import {
  makeStyles, tokens, Card, Text, Button, Spinner, Badge, Divider, Textarea, TabList, Tab,
  MessageBar, MessageBarBody, MessageBarActions,
  Dialog, DialogSurface, DialogBody, DialogTitle, DialogContent, DialogActions, DialogTrigger,
  type BadgeProps, type SelectTabData,
} from '@fluentui/react-components';
import { ArrowLeftRegular, ArrowDownloadRegular, ArrowClockwiseRegular, DismissCircleRegular, DocumentRegular, ShieldCheckmarkRegular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { transactionStateOf, type TransactionState } from '../../domain/states';
import { isSealing, isCompleted } from '../dashboard/dashboardModel';
import { DocumentView } from '../../pdf/DocumentView';
import { useDetail } from './useDetail';
import { terminationReason, canCancel, canRetry, canDownloadFinal } from './detailModel';
import { Timeline } from './Timeline';
import { ParticipantProgress } from './ParticipantProgress';

const BADGE: Record<TransactionState, NonNullable<BadgeProps['color']>> = {
  draft: 'informative', pendingSignature: 'brand', partiallySigned: 'brand', sealing: 'warning',
  completed: 'success', rejected: 'danger', expired: 'severe', sealingError: 'danger', cancelled: 'subtle',
};

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  card: { padding: tokens.spacingVerticalL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  // On phones the title eats the width and the actions get squeezed into a clipped right column;
  // stack them so the actions get the full width below the title.
  head: {
    display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: tokens.spacingHorizontalM,
    '@media (max-width: 640px)': { flexDirection: 'column', alignItems: 'stretch' },
  },
  titleRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', minWidth: 0 },
  meta: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', flexShrink: 0 },
  sectionTitle: { marginTop: tokens.spacingVerticalS },
});

export default function DetailScreen(props: { txId: string; onBack: () => void; onVerify: (txId: string) => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const d = useDetail(props.txId);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [showDoc, setShowDoc] = useState(false);
  const [docVersion, setDocVersion] = useState<'final' | 'content'>('final'); // which version the viewer shows

  if (d.loading) return <Spinner label={t('common.loading')} />;
  if (d.notFound || !d.tx) {
    return (
      <Card className={s.card}>
        <Text>{t('detail.notFound')}</Text>
        <Button icon={<ArrowLeftRegular />} onClick={props.onBack}>{t('detail.back')}</Button>
      </Card>
    );
  }

  const tx = d.tx;
  const stateName = transactionStateOf(tx.state);
  const reasonText = terminationReason(d.events);

  const confirmCancel = () => {
    setCancelOpen(false);
    void d.cancel(reason);
    setReason('');
  };

  return (
    <div className={s.root}>
      <Button appearance="subtle" icon={<ArrowLeftRegular />} onClick={props.onBack}>{t('detail.back')}</Button>

      {d.actionError && (
        <MessageBar intent="error">
          <MessageBarBody>{t('dashboard.actionError')}</MessageBarBody>
          <MessageBarActions><Button appearance="transparent" onClick={d.dismissActionError}>{t('common.close')}</Button></MessageBarActions>
        </MessageBar>
      )}
      {d.sealingCapped && (
        <MessageBar intent="info">
          <MessageBarBody>{t('dashboard.stillProcessing')}</MessageBarBody>
          <MessageBarActions><Button onClick={d.refresh}>{t('dashboard.refresh')}</Button></MessageBarActions>
        </MessageBar>
      )}

      <Card className={s.card}>
        <div className={s.head}>
          <div className={s.titleRow}>
            <Text size={600} weight="semibold">{tx.name}</Text>
            {stateName && <Badge appearance="tint" color={BADGE[stateName]}>{t(`transactionState.${stateName}`)}</Badge>}
          </div>
          <div className={s.actions}>
            <Button appearance="subtle" icon={<DocumentRegular />} onClick={() => setShowDoc((v) => !v)}>
              {showDoc ? t('detail.hideDocument') : t('detail.viewDocument')}
            </Button>
            {canDownloadFinal(tx.state) && (
              <>
                <Button icon={<ArrowDownloadRegular />} onClick={() => void d.download('final', t('detail.versionSigned'))}>{t('detail.downloadSigned')}</Button>
                <Button appearance="subtle" icon={<ArrowDownloadRegular />} onClick={() => void d.download('content', t('detail.versionOriginal'))}>{t('detail.downloadOriginal')}</Button>
              </>
            )}
            {isCompleted(tx.state) && (
              <Button icon={<ShieldCheckmarkRegular />} onClick={() => props.onVerify(tx.id)}>{t('detail.verifyDocument')}</Button>
            )}
            {canRetry(d.isCreator, tx.state) && (
              <Button appearance="primary" icon={<ArrowClockwiseRegular />} onClick={() => void d.retrySealing()}>{t('dashboard.retrySealing')}</Button>
            )}
            {canCancel(d.isCreator, tx.state) && (
              <Button appearance="secondary" icon={<DismissCircleRegular />} onClick={() => setCancelOpen(true)}>{t('detail.cancel')}</Button>
            )}
          </div>
        </div>

        {tx.creatorName && <Text size={200} className={s.meta}>{t('dashboard.sentBy', { name: tx.creatorName })}</Text>}
        {tx.expiresOn && (
          <Text size={200} className={s.meta}>{t('detail.expires', { date: new Date(tx.expiresOn).toLocaleDateString() })}</Text>
        )}
        {isSealing(tx.state) && <Text size={200} className={s.meta}>{t('dashboard.sealingNote')}</Text>}

        {reasonText && (
          <MessageBar intent="warning"><MessageBarBody>{t('detail.reason', { reason: reasonText })}</MessageBarBody></MessageBar>
        )}
      </Card>

      {showDoc && (
        <Card className={s.card}>
          {isCompleted(tx.state) && (
            <TabList
              selectedValue={docVersion}
              onTabSelect={(_e, data: SelectTabData) => setDocVersion(data.value as 'final' | 'content')}
            >
              <Tab value="final">{t('detail.versionSigned')}</Tab>
              <Tab value="content">{t('detail.versionOriginal')}</Tab>
            </TabList>
          )}
          <DocumentView txId={tx.id} documentType={isCompleted(tx.state) ? docVersion : 'content'} />
        </Card>
      )}

      <Card className={s.card}>
        <Text weight="semibold">{t('detail.participants')}</Text>
        <ParticipantProgress participants={d.participants} routing={tx.routing} />
      </Card>

      <Card className={s.card}>
        <Text weight="semibold">{t('detail.timeline')}</Text>
        <Divider />
        <Timeline events={d.events} />
      </Card>

      <Dialog open={cancelOpen} onOpenChange={(_e, data) => { setCancelOpen(data.open); if (!data.open) setReason(''); }}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>{t('detail.cancel')}</DialogTitle>
            <DialogContent>
              <Text>{t('detail.cancelConfirm')}</Text>
              <Textarea style={{ marginTop: 12, width: '100%' }} value={reason} placeholder={t('detail.cancelReasonPh')} onChange={(_e, data) => setReason(data.value)} />
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary">{t('detail.keep')}</Button>
              </DialogTrigger>
              <Button appearance="primary" onClick={confirmCancel}>{t('detail.confirmCancel')}</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
