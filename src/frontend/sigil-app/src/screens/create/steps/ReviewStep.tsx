// Step 4 (doc 05 §4.2): review → send or save draft. "Send" is blocked (with the who-is-missing
// indicator) until every signer has a zone (RF-28); "Save draft" is allowed incomplete.

import { makeStyles, tokens, Text, Button, MessageBar, MessageBarBody, Divider, Spinner } from '@fluentui/react-components';
import { useT } from '../../../i18n/useT';
import type { CreateWizard } from '../useCreateWizard';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', justifyContent: 'space-between', gap: tokens.spacingHorizontalM },
  key: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', marginTop: tokens.spacingVerticalM },
});

export function ReviewStep({ wizard }: { wizard: CreateWizard }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { draft, submit } = wizard;

  const row = (k: string, v: string) => (
    <div className={s.row}><Text className={s.key}>{k}</Text><Text weight="semibold">{v}</Text></div>
  );

  return (
    <div className={s.root}>
      <Text size={500} weight="semibold">{t('create.reviewHeading')}</Text>

      {row(t('create.sumName'), draft.name || '—')}
      {row(t('create.sumRouting'), draft.routing === 'sequential' ? t('create.seq') : t('create.par'))}
      {row(t('create.sumSigners'), String(draft.participants.length))}
      {row(t('create.sumZones'), String(draft.zones.length))}
      {row(t('create.sumExpiration'), draft.expirationDays !== undefined ? t('create.days', { count: draft.expirationDays }) : t('create.sumNoExpiration'))}

      <Divider />

      {wizard.missingZones.length > 0 && (
        <MessageBar intent="warning">
          <MessageBarBody>{t('create.blockedByZones', { names: wizard.missingZones.map((p) => p.name).join(', ') })}</MessageBarBody>
        </MessageBar>
      )}
      {submit.phase === 'error' && <MessageBar intent="error"><MessageBarBody>{t('create.submitError')}</MessageBarBody></MessageBar>}
      {submit.phase === 'done' && (
        <MessageBar intent="success"><MessageBarBody>{submit.sent ? t('create.sentOk') : t('create.draftOk')}</MessageBarBody></MessageBar>
      )}

      {submit.phase === 'submitting' ? (
        <Spinner label={t('create.submitting')} />
      ) : submit.phase !== 'done' && (
        <div className={s.actions}>
          <Button appearance="primary" disabled={!wizard.canSend} onClick={wizard.submitSend}>{t('create.send')}</Button>
          <Button disabled={!wizard.canSaveDraft} onClick={wizard.submitDraft}>{t('create.saveDraft')}</Button>
        </div>
      )}
    </div>
  );
}
