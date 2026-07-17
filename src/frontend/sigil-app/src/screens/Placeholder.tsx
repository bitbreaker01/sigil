// Placeholder for the screens arriving in the next batches (dashboard, sign, detail,
// create). It offers access to what is ALREADY implemented (onboarding, verify) so the app
// can be navigated in dev. It's replaced screen by screen.

import { makeStyles, tokens, Card, Text, Button } from '@fluentui/react-components';
import { useT } from '../i18n/useT';
import type { Screen } from '../lib/navigation';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, alignItems: 'flex-start' },
});

export default function Placeholder(props: {
  screen: Screen;
  onGoToOnboarding: () => void;
  onGoToVerify: () => void;
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  return (
    <Card className={s.card}>
      <Text size={500} weight="semibold">{t(`nav.${props.screen === 'create' ? 'create' : 'dashboard'}`)}</Text>
      <Text>Screen under construction (next F3 batch).</Text>
      <div style={{ display: 'flex', gap: 8 }}>
        <Button appearance="primary" onClick={props.onGoToOnboarding}>{t('dashboard.goToOnboarding')}</Button>
        <Button onClick={props.onGoToVerify}>{t('nav.verify')}</Button>
      </div>
    </Card>
  );
}
