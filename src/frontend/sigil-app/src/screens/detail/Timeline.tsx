// Event timeline (doc 05 §4.4, RNF-04): the transaction's history from sanic_sigil_tbl_event,
// chronological. Labels come from i18n by the event's logical name; free-text details (e.g. a
// rejection reason) are shown as-is (they arrive already localized from the backend).

import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';
import type { EventView } from '../../api/SigilApi';
import { eventLabelKey } from './detailModel';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  row: { display: 'flex', gap: tokens.spacingHorizontalM },
  rail: { display: 'flex', flexDirection: 'column', alignItems: 'center', paddingTop: '4px' },
  dot: { width: '10px', height: '10px', borderRadius: '50%', backgroundColor: tokens.colorBrandBackground, flexShrink: 0 },
  line: { flexGrow: 1, width: '2px', backgroundColor: tokens.colorNeutralStroke2, marginTop: '2px' },
  body: { display: 'flex', flexDirection: 'column', paddingBottom: tokens.spacingVerticalS },
  meta: { color: tokens.colorNeutralForeground3 },
});

export function Timeline({ events }: { events: readonly EventView[] }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  return (
    <div className={s.list}>
      {events.map((e, i) => {
        const key = eventLabelKey(e.type);
        return (
          <div key={e.id} className={s.row}>
            <div className={s.rail}>
              <div className={s.dot} />
              {i < events.length - 1 && <div className={s.line} />}
            </div>
            <div className={s.body}>
              <Text weight="semibold">{key ? t(key) : String(e.type)}</Text>
              <Text size={200} className={s.meta}>
                {[e.actorName, new Date(e.occurredOn).toLocaleString()].filter(Boolean).join(' · ')}
              </Text>
              {e.details && <Text size={200}>{e.details}</Text>}
            </div>
          </div>
        );
      })}
    </div>
  );
}
