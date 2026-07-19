// Master Signature onboarding (doc 05 §4.6, RF-01/02). Presentational: consumes useOnboarding.
// Upload PNG → validate → specific reasons or normalized preview; shows the current one.

import { useRef, useState } from 'react';
import {
  makeStyles,
  tokens,
  Card,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Image,
  Badge,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
} from '@fluentui/react-components';
import { ArrowUpload24Regular, CheckmarkCircle24Filled, ArrowLeft20Regular, ArrowDownload20Regular, SaveRegular, WarningRegular, Document16Regular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { downloadBase64 } from '../../api/binaries';
import { useOnboarding } from './useOnboarding';
import { SignatureMockup } from './SignatureMockup';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  intro: { color: tokens.colorNeutralForeground2 },
  // 3:1 box (600×200, the Master Signature ratio the backend normalizes to) so you see exactly
  // how your signature will be letterboxed on documents — not its raw aspect.
  preview: {
    width: '100%',
    maxWidth: '360px',
    aspectRatio: '3 / 1',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingVerticalS,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  previewImg: { maxWidth: '100%', maxHeight: '100%' },
  reasons: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  currentBlock: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  history: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, marginTop: tokens.spacingVerticalM },
  historyRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM },
  historyInfo: { flexGrow: 1, minWidth: 0 },
  historyThumb: { width: '96px', aspectRatio: '3 / 1', flexShrink: 0, border: `1px solid ${tokens.colorNeutralStroke2}`, borderRadius: tokens.borderRadiusSmall, backgroundColor: tokens.colorNeutralBackground3, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '2px' },
  thumbImg: { maxWidth: '100%', maxHeight: '100%' },
  meta: { color: tokens.colorNeutralForeground3 },
  previewActions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', alignItems: 'center' },
  // Distinct (green) colour for the commit action, so it doesn't read like just another primary CTA.
  saveBtn: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': { backgroundColor: tokens.colorPaletteGreenForeground1, color: tokens.colorNeutralForegroundOnBrand },
    ':hover:active': { backgroundColor: tokens.colorPaletteGreenForeground1, color: tokens.colorNeutralForegroundOnBrand },
  },
  historyItem: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  docList: { display: 'flex', flexDirection: 'column', gap: '2px', paddingLeft: '108px' }, // aligns under the info, past the 96px thumb + gap
  docRow: { display: 'flex', alignItems: 'center', gap: '4px', color: tokens.colorNeutralForeground2 },
  docsLabel: { paddingLeft: '108px', color: tokens.colorNeutralForeground3 },
});

export default function OnboardingScreen(props: { onBack: () => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { state, history, upload, save, cancelPreview, formatError } = useOnboarding();
  const inputRef = useRef<HTMLInputElement>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const png = (b64: string) => `data:image/png;base64,${b64}`;

  return (
    <Card className={s.card}>
      <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={props.onBack} style={{ alignSelf: 'flex-start' }}>{t('detail.back')}</Button>
      <Text size={600} weight="semibold">{t('onboarding.title')}</Text>
      <Text className={s.intro}>{t('onboarding.intro')}</Text>

      {state.phase === 'loading' && <Spinner label={t('common.loading')} />}

      {state.phase === 'ready' && state.currentSignature && (
        <div className={s.currentBlock}>
          <Text weight="semibold">{t('onboarding.currentSignature')}</Text>
          <div className={s.preview}><Image className={s.previewImg} src={png(state.currentSignature)} alt={t('onboarding.currentSignature')} fit="contain" /></div>
          {state.validatedOn && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              {t('onboarding.validatedOn', { date: new Date(state.validatedOn).toLocaleDateString() })}
            </Text>
          )}
          <Text weight="semibold" style={{ marginTop: 8 }}>{t('onboarding.mockupTitle')}</Text>
          <SignatureMockup signature={state.currentSignature} />
        </div>
      )}

      {state.phase === 'processing' && <Spinner label={t('onboarding.processing')} />}

      {state.phase === 'preview' && (
        <>
          <MessageBar intent="info"><MessageBarBody>{t('onboarding.previewNotice')}</MessageBarBody></MessageBar>
          <Text weight="semibold">{t('onboarding.normalizedPreview')}</Text>
          <div className={s.preview}><Image className={s.previewImg} src={png(state.normalized)} alt={t('onboarding.normalizedPreview')} fit="contain" /></div>
          <Text weight="semibold" style={{ marginTop: 8 }}>{t('onboarding.mockupTitle')}</Text>
          <SignatureMockup signature={state.normalized} />
          <div className={s.previewActions}>
            <Button className={s.saveBtn} icon={<SaveRegular />} onClick={() => setConfirmOpen(true)}>
              {t('onboarding.saveNew')}
            </Button>
            <Button appearance="subtle" onClick={cancelPreview}>{t('common.cancel')}</Button>
          </div>
        </>
      )}

      {/* Irreversible-replacement confirmation (RF-02). */}
      <Dialog open={confirmOpen} onOpenChange={(_e, d) => setConfirmOpen(d.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle><WarningRegular /> {t('onboarding.confirmTitle')}</DialogTitle>
            <DialogContent>{t('onboarding.confirmBody')}</DialogContent>
            <DialogActions>
              <Button appearance="subtle" onClick={() => setConfirmOpen(false)}>{t('common.cancel')}</Button>
              <Button className={s.saveBtn} icon={<SaveRegular />} onClick={() => { setConfirmOpen(false); save(); }}>
                {t('onboarding.confirmSave')}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {state.phase === 'success' && (
        <>
          <MessageBar intent="success">
            <MessageBarBody><CheckmarkCircle24Filled /> {t('onboarding.success')}</MessageBarBody>
          </MessageBar>
          <Text weight="semibold">{t('onboarding.normalizedPreview')}</Text>
          <div className={s.preview}><Image className={s.previewImg} src={png(state.normalized)} alt={t('onboarding.normalizedPreview')} fit="contain" /></div>
          <Text weight="semibold" style={{ marginTop: 8 }}>{t('onboarding.mockupTitle')}</Text>
          <SignatureMockup signature={state.normalized} />
          <Button appearance="primary" onClick={props.onBack} style={{ alignSelf: 'flex-start' }}>{t('common.continue')}</Button>
        </>
      )}

      {state.phase === 'rejected' && (
        <MessageBar intent="error">
          <MessageBarBody>
            <div className={s.reasons}>
              {state.reasons.map((m, i) => (
                <Text key={i}>{m.startsWith('common.') || m.startsWith('onboarding.') ? t(m) : m}</Text>
              ))}
            </div>
          </MessageBarBody>
        </MessageBar>
      )}

      {state.phase === 'error' && (
        <MessageBar intent="error"><MessageBarBody>
          {state.message.startsWith('common.') || state.message.startsWith('onboarding.')
            ? t(state.message)
            : state.message}
        </MessageBarBody></MessageBar>
      )}

      {formatError && (
        <MessageBar intent="warning"><MessageBarBody>{t('onboarding.invalidFormat')}</MessageBarBody></MessageBar>
      )}

      {history.length > 0 && (
        <div className={s.history}>
          <Text weight="semibold">{t('onboarding.historyTitle')}</Text>
          {history.map((v) => (
            <div key={v.version} className={s.historyItem}>
              <div className={s.historyRow}>
                <div className={s.historyThumb}><Image className={s.thumbImg} src={png(v.imageBase64)} alt={t('onboarding.version', { n: v.version })} fit="contain" /></div>
                <div className={s.historyInfo}>
                  <Text weight="semibold">{t('onboarding.version', { n: v.version })}</Text>
                  {v.isActive && <> <Badge appearance="tint" color="success" size="small">{t('onboarding.activeVersion')}</Badge></>}
                  <br />
                  <Text size={200} className={s.meta}>{new Date(v.validatedOn).toLocaleString()}</Text>
                </div>
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ArrowDownload20Regular />}
                  aria-label={t('onboarding.downloadVersion', { n: v.version })}
                  title={t('onboarding.downloadVersion', { n: v.version })}
                  onClick={() => void downloadBase64(v.imageBase64, `firma-v${v.version}.png`, 'image/png')
                    .catch((err: unknown) => console.error('[signature-download]', err))}
                />
              </div>
              {v.documents.length > 0 && (
                <>
                  <Text size={200} className={s.docsLabel}>{t('onboarding.signedWith', { count: v.documents.length })}</Text>
                  <div className={s.docList}>
                    {v.documents.map((d) => (
                      <div key={d.id} className={s.docRow}>
                        <Document16Regular />
                        <Text size={200}>{d.name || t('onboarding.untitledDoc')}</Text>
                      </div>
                    ))}
                  </div>
                </>
              )}
            </div>
          ))}
        </div>
      )}

      <div>
        <input
          ref={inputRef}
          type="file"
          accept="image/png"
          hidden
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) upload(f);
            e.target.value = '';
          }}
        />
        <Button
          appearance="primary"
          icon={<ArrowUpload24Regular />}
          disabled={state.phase === 'processing'}
          onClick={() => inputRef.current?.click()}
        >
          {t('onboarding.upload')}
        </Button>
      </div>
    </Card>
  );
}
