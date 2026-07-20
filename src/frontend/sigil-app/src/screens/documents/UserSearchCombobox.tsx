// Async single-select user picker for the Documents filter bar (creator / other participant). Since
// server-side paging means we no longer hold the full doc set, these filters can't be populated from
// loaded rows — instead they search Dataverse users on the fly (sigilApi.searchUsers), debounced.

import { useEffect, useRef, useState } from 'react';
import { Combobox, Option, Field } from '@fluentui/react-components';
import { sigilApi } from '../../api';
import type { UserSummary } from '../../api/SigilApi';

export function UserSearchCombobox(props: {
  label: string;
  anyLabel: string; // the reset option ("Any creator" / "Anyone")
  className?: string;
  selectedId: string; // '' = none
  onSelect: (id: string, name: string) => void;
}): JSX.Element {
  const [text, setText] = useState('');
  const [display, setDisplay] = useState(''); // name of the currently selected user
  const [results, setResults] = useState<UserSummary[]>([]);
  const [typing, setTyping] = useState(false);
  const reqId = useRef(0);

  // Debounced search; a request counter drops out-of-order responses.
  useEffect(() => {
    if (!typing) return;
    const q = text.trim();
    const mine = ++reqId.current;
    const id = setTimeout(() => {
      void sigilApi.searchUsers(q).then((users) => { if (mine === reqId.current) setResults(users); });
    }, 300);
    return () => clearTimeout(id);
  }, [text, typing]);

  const value = typing ? text : (props.selectedId ? display : '');

  return (
    <Field label={props.label} className={props.className}>
      <Combobox
        freeform
        placeholder={props.anyLabel}
        value={value}
        selectedOptions={props.selectedId ? [props.selectedId] : ['']}
        onChange={(e) => { setText(e.target.value); setTyping(true); }}
        onOptionSelect={(_e, data) => {
          const val = data.optionValue ?? '';
          setTyping(false);
          setText('');
          if (!val) { setDisplay(''); props.onSelect('', props.anyLabel); return; }
          const u = results.find((r) => r.id === val);
          setDisplay(u?.name ?? '');
          props.onSelect(val, u?.name ?? '');
        }}
        onBlur={() => { setTyping(false); setText(''); }}
      >
        <Option value="">{props.anyLabel}</Option>
        {results.map((u) => <Option key={u.id} value={u.id}>{u.name}</Option>)}
      </Combobox>
    </Field>
  );
}
