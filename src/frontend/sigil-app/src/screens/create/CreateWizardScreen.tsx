// Create-request wizard shell: a 4-step stepper over useCreateWizard. Each step is
// a presentational component; this shell owns navigation (Back/Next) and the step gating. On a
// successful submit it shows the outcome and lets the user leave (onExit).

import { makeStyles, tokens, Card, Text, Button, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';
import { useCreateWizard } from './useCreateWizard';
import { WIZARD_STEPS, type WizardStep } from './createWizardModel';
import { PdfStep } from './steps/PdfStep';
import { ParticipantsStep } from './steps/ParticipantsStep';
import { ZonesStep } from './steps/ZonesStep';
import { ReviewStep } from './steps/ReviewStep';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  stepper: { display: 'flex', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
  step: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`, borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground3 },
  stepOn: { backgroundColor: tokens.colorBrandBackground2, color: tokens.colorBrandForeground2, fontWeight: tokens.fontWeightSemibold },
  num: { display: 'inline-flex', width: '20px', height: '20px', borderRadius: '50%', backgroundColor: tokens.colorNeutralBackground1, alignItems: 'center', justifyContent: 'center', fontSize: tokens.fontSizeBase100 },
  footer: { display: 'flex', justifyContent: 'space-between', marginTop: tokens.spacingVerticalM },
  errors: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
});

const STEP_LABEL: Record<WizardStep, string> = {
  pdf: 'create.steps.pdf',
  participants: 'create.steps.participants',
  zones: 'create.steps.zones',
  review: 'create.steps.review',
};

export default function CreateWizardScreen(props: { onExit: () => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const wizard = useCreateWizard(() => { /* success is shown in-place by ReviewStep */ });

  const idx = WIZARD_STEPS.indexOf(wizard.step);
  const isLast = wizard.step === 'review';
  const done = wizard.submit.phase === 'done';

  return (
    <Card className={s.card}>
      <div className={s.stepper}>
        {WIZARD_STEPS.map((step, i) => (
          <div key={step} className={`${s.step} ${i === idx ? s.stepOn : ''}`}>
            <span className={s.num}>{i + 1}</span>
            <Text>{t(STEP_LABEL[step])}</Text>
          </div>
        ))}
      </div>

      {wizard.step === 'pdf' && <PdfStep wizard={wizard} />}
      {wizard.step === 'participants' && <ParticipantsStep wizard={wizard} />}
      {wizard.step === 'zones' && <ZonesStep wizard={wizard} />}
      {wizard.step === 'review' && <ReviewStep wizard={wizard} />}

      {/* Blocking reasons for the current step (i18n keys). Not shown on review (its own UI). */}
      {!isLast && !wizard.canAdvanceStep && wizard.errors.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <div className={s.errors}>{wizard.errors.map((e) => <Text key={e}>{t(e)}</Text>)}</div>
          </MessageBarBody>
        </MessageBar>
      )}

      <div className={s.footer}>
        <Button appearance="subtle" onClick={idx === 0 ? props.onExit : wizard.goBack}>
          {idx === 0 ? t('common.close') : t('create.back')}
        </Button>
        {done ? (
          <Button appearance="primary" onClick={props.onExit}>{t('common.close')}</Button>
        ) : !isLast ? (
          <Button appearance="primary" disabled={!wizard.canAdvanceStep} onClick={wizard.goNext}>{t('create.next')}</Button>
        ) : null}
      </div>
    </Card>
  );
}
