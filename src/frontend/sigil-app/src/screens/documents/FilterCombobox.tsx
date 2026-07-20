// A searchable single-select over a FIXED local list (status / signature version). Built on a plain
// Fluent Input + a results dropdown — the same reliable pattern as UserSearchCombobox — because
// Fluent's Combobox fights a controlled value and swallows keystrokes.

import { useState } from 'react';
import { makeStyles, tokens, Field, Input } from '@fluentui/react-components';

export interface ComboOption {
  value: string;
  text: string;
}

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

export function FilterCombobox(props: {
  label: string;
  placeholder?: string;
  className?: string;
  selected: string; // the currently selected option value
  options: readonly ComboOption[]; // first item is usually the "any" reset option
  onSelect: (value: string) => void;
}): JSX.Element {
  const s = useStyles();
  const { options, selected } = props;
  const selectedText = options.find((o) => o.value === selected)?.text ?? '';
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);

  const q = query.trim().toLowerCase();
  const shown = open && q ? options.filter((o) => o.text.toLowerCase().includes(q)) : options;

  return (
    <Field label={props.label} className={props.className}>
      <div className={s.wrap}>
        <Input
          value={open ? query : selectedText}
          placeholder={props.placeholder}
          onChange={(_e, d) => { setQuery(d.value); setOpen(true); }}
          onFocus={() => { setQuery(''); setOpen(true); }}
          onBlur={() => setTimeout(() => setOpen(false), 150)} // let a result's mousedown land first
        />
        {open && (
          <div className={s.menu}>
            {shown.map((o) => (
              <button
                key={o.value} type="button" className={s.item}
                onMouseDown={(e) => { e.preventDefault(); props.onSelect(o.value); setQuery(''); setOpen(false); }}
              >
                {o.text}
              </button>
            ))}
          </div>
        )}
      </div>
    </Field>
  );
}
