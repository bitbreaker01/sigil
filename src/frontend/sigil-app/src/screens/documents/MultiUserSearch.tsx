// Multi-select async user picker for the "other participants" filter (AND semantics — the doc must
// include ALL chosen signers). Chips for the current selection + a search Input that adds more.
// Built on a plain Input + results dropdown (Fluent's Combobox swallows keystrokes when controlled).

import { useEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Field, Input, Badge } from '@fluentui/react-components';
import { SearchRegular, DismissRegular } from '@fluentui/react-icons';
import { sigilApi } from '../../api';
import type { UserSummary } from '../../api/SigilApi';
import type { SelectedUser } from './documentsModel';

const useStyles = makeStyles({
  wrap: { position: 'relative' },
  chips: { display: 'flex', flexWrap: 'wrap', gap: '4px', marginBottom: '4px' },
  chip: { cursor: 'pointer' },
  menu: {
    position: 'absolute', top: '100%', left: 0, right: 0, zIndex: 10, marginTop: '2px',
    maxHeight: '220px', overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow16,
  },
  item: {
    display: 'block', width: '100%', textAlign: 'left', border: 'none', background: 'none',
    padding: `${tokens.spacingVerticalSNudge} ${tokens.spacingHorizontalM}`, cursor: 'pointer',
    color: tokens.colorNeutralForeground1,
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
});

export function MultiUserSearch(props: {
  label: string;
  placeholder: string;
  className?: string;
  selected: SelectedUser[];
  onAdd: (u: SelectedUser) => void;
  onRemove: (id: string) => void;
}): JSX.Element {
  const s = useStyles();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<UserSummary[]>([]);
  const [open, setOpen] = useState(false);
  const reqId = useRef(0);

  useEffect(() => {
    const q = query.trim();
    if (!q) { setResults([]); return; }
    const mine = ++reqId.current;
    const id = setTimeout(() => {
      void sigilApi.searchUsers(q).then((u) => { if (mine === reqId.current) setResults(u); });
    }, 300);
    return () => clearTimeout(id);
  }, [query]);

  const chosen = new Set(props.selected.map((p) => p.id));
  const candidates = results.filter((u) => !chosen.has(u.id));

  return (
    <Field label={props.label} className={props.className}>
      {props.selected.length > 0 && (
        <div className={s.chips}>
          {props.selected.map((p) => (
            <Badge
              key={p.id} appearance="tint" color="brand" className={s.chip}
              icon={<DismissRegular />} iconPosition="after"
              onClick={() => props.onRemove(p.id)}
            >
              {p.name}
            </Badge>
          ))}
        </div>
      )}
      <div className={s.wrap}>
        <Input
          value={query}
          placeholder={props.placeholder}
          contentBefore={<SearchRegular />}
          onChange={(_e, d) => { setQuery(d.value); setOpen(true); }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
        />
        {open && candidates.length > 0 && (
          <div className={s.menu}>
            {candidates.map((u) => (
              <button
                key={u.id} type="button" className={s.item}
                onMouseDown={(e) => { e.preventDefault(); props.onAdd({ id: u.id, name: u.name }); setQuery(''); }}
              >
                {u.name}
              </button>
            ))}
          </div>
        )}
      </div>
    </Field>
  );
}
