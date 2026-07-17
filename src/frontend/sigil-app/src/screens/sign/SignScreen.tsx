// Sign screen (doc 05 §4.3, RF-03/04/13/28): full PDF viewer with the signer's zones highlighted,
// "Approve & sign" gated on a successful render (RF-03), "Reject" with a mandatory reason, and a
// hard gate to onboarding when there's no Master Signature. On success a differentiated toast
// (by IsLastSigner) is shown and the user is routed to the detail screen.

import { useState } from 'react';
import {
  makeStyles, tokens, Card, Text, Button, Spinner, Textarea,
  MessageBar, MessageBarBody, MessageBarActions,
  Dialog, DialogSurface, DialogBody, DialogTitle, DialogContent, DialogActions, DialogTrigger,
  useToastController, Toast, ToastTitle,
} from '@fluentui/react-components';
import { ArrowLeftRegular, SignatureRegular, DismissRegular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { TOASTER_ID } from '../../app/toast';
import { usePdfDocument } from '../../pdf/usePdfDocument';
import { PdfViewer } from '../../pdf/PdfViewer';
import { useSign } from './useSign';
import { SignZoneOverlay } from './SignZoneOverlay';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  card: { padding: tokens.spacingVerticalL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  viewer: { display: 'flex', flexDirection: 'column', alignItems: 'center', gap: tokens.spacingVerticalS, minWidth: 0 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', justifyContent: 'flex-end' },
  legend: { display: 'flex', gap: tokens.spacingHorizontalL, color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, flexWrap: 'wrap' },
  swatchMine: { display: 'inline-block', width: '12px', height: '12px', border: `2px solid ${tokens.colorBrandStroke1}`, marginRight: '4px', verticalAlign: 'middle' },
  swatchOther: { display: 'inline-block', width: '12px', height: '12px', border: `2px dashed ${tokens.colorNeutralStroke1}`, marginRight: '4px', verticalAlign: 'middle' },
});

export default function SignScreen(props: { txId: string; onSigned: (txId: string) => void; onNeedOnboarding: () => void; onBack: () => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const sign = useSign(props.txId);
  const { dispatchToast } = useToastController(TOASTER_ID);
  const pdf = usePdfDocument(sign.documentBase64);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [reason, setReason] = useState('');

  const onApprove = async () => {
    const isLast = await sign.approve();
    if (isLast !== null) {
      dispatchToast(<Toast><ToastTitle>{t(isLast ? 'sign.signedLast' : 'sign.signedOk')}</ToastTitle></Toast>, { intent: 'success' });
      props.onSigned(props.txId);
    }
  };
  const onReject = async () => {
    setRejectOpen(false);
    const ok = await sign.reject(reason.trim());
    setReason('');
    if (ok) {
      dispatchToast(<Toast><ToastTitle>{t('sign.reject')}</ToastTitle></Toast>, { intent: 'warning' });
      props.onSigned(props.txId);
    }
  };

  if (sign.loading) return <Spinner label={t('common.loading')} />;
  if (sign.notFound || !sign.tx) {
    return (
      <Card className={s.card}>
        <Text>{t('detail.notFound')}</Text>
        <Button icon={<ArrowLeftRegular />} onClick={props.onBack}>{t('detail.back')}</Button>
      </Card>
    );
  }
  if (sign.needsMasterSignature) {
    return (
      <Card className={s.card}>
        <Text size={500} weight="semibold">{t('sign.heading')}</Text>
        <MessageBar intent="warning"><MessageBarBody>{t('sign.noMasterSignature')}</MessageBarBody></MessageBar>
        <div className={s.actions}>
          <Button icon={<ArrowLeftRegular />} onClick={props.onBack}>{t('detail.back')}</Button>
          <Button appearance="primary" onClick={props.onNeedOnboarding}>{t('dashboard.goToOnboarding')}</Button>
        </div>
      </Card>
    );
  }

  return (
    <div className={s.root}>
      <Button appearance="subtle" icon={<ArrowLeftRegular />} onClick={props.onBack}>{t('detail.back')}</Button>

      {sign.actionError && (
        <MessageBar intent="error">
          <MessageBarBody>{t('dashboard.actionError')}</MessageBarBody>
          <MessageBarActions><Button appearance="transparent" onClick={sign.dismissActionError}>{t('common.close')}</Button></MessageBarActions>
        </MessageBar>
      )}

      <Card className={s.card}>
        <Text size={500} weight="semibold">{sign.tx.name}</Text>
        {sign.tx.message && <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{sign.tx.message}</Text>}
        <div className={s.legend}>
          <span><span className={s.swatchMine} />{t('sign.yourZones')}</span>
          {sign.otherZones.length > 0 && <span><span className={s.swatchOther} />{t('sign.otherSigners')}</span>}
        </div>

        <div className={s.viewer}>
          {sign.docLoading || pdf.phase === 'loading' ? <Spinner label={t('common.loading')} />
            : sign.docError || pdf.phase === 'error' ? <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>
            : pdf.phase === 'ready' && (
              <PdfViewer doc={pdf.doc} pageCount={pdf.pageCount} onFirstRender={sign.markRendered}>
                {({ page, size }) => (
                  <SignZoneOverlay page={page} myZones={sign.myZones} otherZones={sign.otherZones} size={size} myLabel={t('sign.yourZones')} masterSignature={sign.masterSignature} />
                )}
              </PdfViewer>
            )}
        </div>

        {!sign.canAct
          ? <MessageBar intent="info"><MessageBarBody>{t('sign.notYourTurn')}</MessageBarBody></MessageBar>
          : !sign.rendered && <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{t('sign.renderRequired')}</Text>}

        <div className={s.actions}>
          <Button appearance="secondary" icon={<DismissRegular />} disabled={sign.submitting || !sign.canAct} onClick={() => setRejectOpen(true)}>{t('sign.reject')}</Button>
          <Button appearance="primary" icon={<SignatureRegular />} disabled={!sign.canApprove} onClick={() => void onApprove()}>{t('sign.approve')}</Button>
        </div>
      </Card>

      <Dialog open={rejectOpen} onOpenChange={(_e, data) => { setRejectOpen(data.open); if (!data.open) setReason(''); }}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>{t('sign.reject')}</DialogTitle>
            <DialogContent>
              <Text>{t('sign.rejectReason')}</Text>
              <Textarea style={{ marginTop: 12, width: '100%' }} value={reason} onChange={(_e, data) => setReason(data.value)} />
              {!reason.trim() && <Text size={200} style={{ color: tokens.colorStatusDangerForeground1 }}>{t('sign.reasonRequired')}</Text>}
            </DialogContent>
            <DialogActions>
              <DialogTrigger disableButtonEnhancement><Button appearance="secondary">{t('detail.keep')}</Button></DialogTrigger>
              <Button appearance="primary" disabled={!reason.trim()} onClick={() => void onReject()}>{t('sign.reject')}</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
