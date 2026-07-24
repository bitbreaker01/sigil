// Participant progress: who has signed, whose turn it is, and the signing order for
// sequential routing. State labels come from i18n by logical name.

import { makeStyles, tokens, Text, Avatar, Badge, type BadgeProps } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';
import type { ParticipantView } from '../../api/SigilApi';
import type { Routing, ParticipantState } from '../../domain/states';
import { PARTICIPANT_STATE } from '../../domain/states';
import { participantLabelKey } from './detailModel';

const COLOR: Record<ParticipantState, NonNullable<BadgeProps['color']>> = {
  pending: 'informative', activeTurn: 'brand', signed: 'success', rejected: 'danger',
};

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  // flexWrap lets the badges drop below on very narrow screens instead of being clipped.
  row: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  // minWidth:0 lets the name column shrink (and wrap) instead of pushing the badges off-screen.
  grow: { flexGrow: 1, minWidth: 0, display: 'flex', flexDirection: 'column', overflowWrap: 'anywhere' },
  badges: { display: 'flex', gap: tokens.spacingHorizontalXS, flexShrink: 0, flexWrap: 'wrap', justifyContent: 'flex-end' },
  meta: { color: tokens.colorNeutralForeground3 },
});

export function ParticipantProgress(props: { participants: readonly ParticipantView[]; routing: Routing }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  return (
    <div className={s.list}>
      {props.participants.map((p) => {
        const name = PARTICIPANT_STATE[p.state];
        const key = participantLabelKey(p.state);
        return (
          <div key={p.id} className={s.row}>
            <Avatar name={p.name ?? p.userId} size={28} />
            <div className={s.grow}>
              <Text>{p.name ?? p.userId}</Text>
              {p.signedOn && <Text size={200} className={s.meta}>{new Date(p.signedOn).toLocaleString()}</Text>}
            </div>
            <div className={s.badges}>
              {props.routing === 'sequential' && p.order !== undefined && (
                <Badge appearance="tint" color="informative">{t('detail.order', { n: p.order })}</Badge>
              )}
              {key && <Badge appearance="tint" color={name ? COLOR[name] : 'informative'}>{t(key)}</Badge>}
            </div>
          </div>
        );
      })}
    </div>
  );
}
