// Async single-select user picker for the Documents filter bar (creator / other participant). Uses a
// plain Fluent Input (whose onChange is reliable — Fluent's Combobox fights a controlled value and
// swallows keystrokes) plus a results dropdown, the same proven pattern as the create wizard's people
// picker. Server-side paging means we can't populate these from a loaded set, so they search on type.

import { useEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Field, Input, Button } from '@fluentui/react-components';
import { SearchRegular, DismissRegular } from '@fluentui/react-icons';
import { sigilApi } from '../../api';
import type { UserSummary } from '../../api/SigilApi';

const useStyles = makeStyles({
  wrap: { position: 'relative' },
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

export function UserSearchCombobox(props: {
  label: string;
  anyLabel: string; // shown as placeholder and as the reset target
  className?: string;
  selectedId: string; // '' = none
  onSelect: (id: string, name: string) => void;
}): JSX.Element {
  const s = useStyles();
  const [query, setQuery] = useState('');
  const [display, setDisplay] = useState(''); // name of the currently selected user
  const [results, setResults] = useState<UserSummary[]>([]);
  const [open, setOpen] = useState(false);
  const reqId = useRef(0);

  // Debounced search; a request counter drops out-of-order responses.
  useEffect(() => {
    const q = query.trim();
    if (!q) { setResults([]); return; }
    const mine = ++reqId.current;
    const id = setTimeout(() => {
      void sigilApi.searchUsers(q).then((u) => { if (mine === reqId.current) setResults(u); });
    }, 300);
    return () => clearTimeout(id);
  }, [query]);

  // Selected: show the picked name with a clear (×) that resets the filter.
  if (props.selectedId) {
    return (
      <Field label={props.label} className={props.className}>
        <Input
          readOnly
          value={display || props.anyLabel}
          contentAfter={
            <Button
              size="small" appearance="subtle" icon={<DismissRegular />} aria-label={props.anyLabel}
              onClick={() => { props.onSelect('', props.anyLabel); setDisplay(''); setQuery(''); }}
            />
          }
        />
      </Field>
    );
  }

  return (
    <Field label={props.label} className={props.className}>
      <div className={s.wrap}>
        <Input
          value={query}
          placeholder={props.anyLabel}
          contentBefore={<SearchRegular />}
          onChange={(_e, d) => { setQuery(d.value); setOpen(true); }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)} // let a result's mousedown land first
        />
        {open && results.length > 0 && (
          <div className={s.menu}>
            {results.map((u) => (
              <button
                key={u.id} type="button" className={s.item}
                // mousedown (not click) fires before the input blur closes the menu.
                onMouseDown={(e) => {
                  e.preventDefault();
                  props.onSelect(u.id, u.name);
                  setDisplay(u.name);
                  setQuery('');
                  setOpen(false);
                }}
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
