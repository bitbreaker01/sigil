// A searchable single-select for the Documents filter bar. Fluent's Combobox lets the user TYPE,
// but it does NOT filter the options for you — this wraps that: a plain controlled input string the
// user fully edits to filter, kept in sync with the selected option. Reused for every filter.

import { useEffect, useState } from 'react';
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

  // The input string. Starts as the selected option's label; the user edits it freely to search.
  const [text, setText] = useState(selectedText);
  // Re-sync when the selection changes from outside (picking an option, or a reset like clearDocIds).
  useEffect(() => { setText(selectedText); }, [selectedText]);

  const q = text.trim().toLowerCase();
  // Show everything until the text actually diverges from the current label (i.e. the user is typing).
  const shown = q === '' || text === selectedText
    ? options
    : options.filter((o) => o.text.toLowerCase().includes(q));

  return (
    <Field label={props.label} className={props.className}>
      <Combobox
        placeholder={props.placeholder}
        value={text}
        selectedOptions={[selected]}
        onChange={(e) => setText(e.target.value)}
        onOptionSelect={(_e, data) => props.onSelect(data.optionValue ?? '')}
        onBlur={() => setText(selectedText)} // typed-but-not-selected → revert to the current selection
      >
        {shown.map((o) => <Option key={o.value} value={o.value}>{o.text}</Option>)}
      </Combobox>
    </Field>
  );
}
