// A searchable single-select for the Documents filter bar. Fluent's Combobox lets the user TYPE,
// but it does NOT filter the options for you — this wraps that: it filters the options by the typed
// text and keeps the selected value in sync. Reused for creator / participant / status / version.

import { useState } from 'react';
import { Combobox, Option, Field } from '@fluentui/react-components';

export interface ComboOption {
  value: string;
  text: string;
}

export function FilterCombobox(props: {
  label: string;
  placeholder?: string;
  className?: string;
  selected: string; // the currently selected option value
  options: readonly ComboOption[]; // first item is usually the "any" reset option
  onSelect: (value: string) => void;
}): JSX.Element {
  const { options, selected } = props;
  const selectedText = options.find((o) => o.value === selected)?.text ?? '';
  // undefined = not typing → show the selected text; a string = the user is filtering.
  const [query, setQuery] = useState<string | undefined>(undefined);
  const filter = (query ?? '').trim().toLowerCase();
  const shown = query === undefined || filter === ''
    ? options
    : options.filter((o) => o.text.toLowerCase().includes(filter));

  return (
    <Field label={props.label} className={props.className}>
      <Combobox
        placeholder={props.placeholder}
        value={query === undefined ? selectedText : query}
        selectedOptions={[selected]}
        onChange={(e) => setQuery(e.target.value)}
        onOptionSelect={(_e, data) => { props.onSelect(data.optionValue ?? ''); setQuery(undefined); }}
        onBlur={() => setQuery(undefined)} // typed-but-not-selected → revert to the current selection
      >
        {shown.map((o) => <Option key={o.value} value={o.value}>{o.text}</Option>)}
      </Combobox>
    </Field>
  );
}
