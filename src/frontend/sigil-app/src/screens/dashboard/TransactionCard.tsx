// A transaction card for the dashboard lists (doc 05 §4.1): name, sender, state badge, optional
// due-date urgency (RF-27), and a slot for the context's CTAs. Presentational — all labels via i18n.

import { makeStyles, tokens, Card, Text, Badge, Link, type BadgeProps } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';
import { transactionStateOf, type TransactionState } from '../../domain/states';
import type { TransactionView } from '../../api/SigilApi';
import { dueLevel, type DueLevel } from './dashboardModel';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalM, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  head: { display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: tokens.spacingHorizontalM },
  title: { fontWeight: tokens.fontWeightSemibold, textAlign: 'left' },
  meta: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
});

type BadgeColor = NonNullable<BadgeProps['color']>;

const BADGE: Record<TransactionState, BadgeColor> = {
  draft: 'informative',
  pendingSignature: 'brand',
  partiallySigned: 'brand',
  sealing: 'warning',
  completed: 'success',
  rejected: 'danger',
  expired: 'severe',
  sealingError: 'danger',
  cancelled: 'subtle',
};

const DUE_COLOR: Record<DueLevel, BadgeColor> = {
  overdue: 'danger', today: 'important', soon: 'warning', none: 'informative',
};

export function TransactionCard(props: {
  tx: TransactionView;
  now: number;
  showDue?: boolean;
  onOpen?: () => void; // open the detail screen
  children?: React.ReactNode; // CTAs
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { tx } = props;
  const stateName = transactionStateOf(tx.state);
  const due = dueLevel(tx.expiresOn, props.now);

  return (
    <Card className={s.card}>
      <div className={s.head}>
        {props.onOpen
          ? <Link appearance="subtle" onClick={props.onOpen} className={s.title}>{tx.name}</Link>
          : <Text weight="semibold">{tx.name}</Text>}
        {stateName && <Badge appearance="tint" color={BADGE[stateName]}>{t(`transactionState.${stateName}`)}</Badge>}
      </div>
      {tx.creatorName && <Text size={200} className={s.meta}>{t('dashboard.sentBy', { name: tx.creatorName })}</Text>}
      {props.showDue && tx.expiresOn && (
        <Badge appearance="tint" color={DUE_COLOR[due]}>
          {due === 'overdue' ? t('dashboard.overdue')
            : due === 'today' ? t('dashboard.dueToday')
            : t('dashboard.dueDate', { date: new Date(tx.expiresOn).toLocaleDateString() })}
        </Badge>
      )}
      {props.children && <div className={s.actions}>{props.children}</div>}
    </Card>
  );
}
