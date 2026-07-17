// Master Signature onboarding (doc 05 §4.6, RF-01/02). Presentational: consumes useOnboarding.
// Upload PNG → validate → specific reasons or normalized preview; shows the current one.

import { useRef } from 'react';
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
} from '@fluentui/react-components';
import { ArrowUpload24Regular, CheckmarkCircle24Filled, ArrowLeft20Regular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { useOnboarding } from './useOnboarding';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  intro: { color: tokens.colorNeutralForeground2 },
  preview: {
    maxWidth: '320px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingVerticalM,
  },
  reasons: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
});

export default function OnboardingScreen(props: { onBack: () => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { state, upload, formatError } = useOnboarding();
  const inputRef = useRef<HTMLInputElement>(null);

  const png = (b64: string) => `data:image/png;base64,${b64}`;

  return (
    <Card className={s.card}>
      <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={props.onBack} style={{ alignSelf: 'flex-start' }}>{t('detail.back')}</Button>
      <Text size={600} weight="semibold">{t('onboarding.title')}</Text>
      <Text className={s.intro}>{t('onboarding.intro')}</Text>

      {state.phase === 'loading' && <Spinner label={t('common.loading')} />}

      {state.phase === 'ready' && state.currentSignature && (
        <div>
          <Text weight="semibold">{t('onboarding.currentSignature')}</Text>
          <div className={s.preview}><Image src={png(state.currentSignature)} alt={t('onboarding.currentSignature')} fit="contain" /></div>
          {state.validatedOn && (
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              {t('onboarding.validatedOn', { date: new Date(state.validatedOn).toLocaleDateString() })}
            </Text>
          )}
        </div>
      )}

      {state.phase === 'processing' && <Spinner label={t('onboarding.processing')} />}

      {state.phase === 'success' && (
        <>
          <MessageBar intent="success">
            <MessageBarBody><CheckmarkCircle24Filled /> {t('onboarding.success')}</MessageBarBody>
          </MessageBar>
          <Text weight="semibold">{t('onboarding.normalizedPreview')}</Text>
          <div className={s.preview}><Image src={png(state.normalized)} alt={t('onboarding.normalizedPreview')} fit="contain" /></div>
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
        <MessageBar intent="error"><MessageBarBody>{t(state.message)}</MessageBarBody></MessageBar>
      )}

      {formatError && (
        <MessageBar intent="warning"><MessageBarBody>{t('onboarding.invalidFormat')}</MessageBarBody></MessageBar>
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
