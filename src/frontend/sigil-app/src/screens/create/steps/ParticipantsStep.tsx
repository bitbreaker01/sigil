// Step 2: signers + routing. Routing decides whether the list order matters
// (sequential = signing order, auto-derived by position). The people picker searches the seam
// (mock: fake directory; real: Dataverse systemuser). Duplicates are prevented by the hook.

import { useEffect, useState } from 'react';
import {
  makeStyles, tokens, Field, Input, Radio, RadioGroup, Text, Button, Spinner, Avatar, Badge,
} from '@fluentui/react-components';
import { PersonAddRegular, AddRegular, ArrowUpRegular, ArrowDownRegular, DismissRegular } from '@fluentui/react-icons';
import { useT } from '../../../i18n/useT';
import { sigilApi } from '../../../api';
import type { UserSummary } from '../../../api/SigilApi';
import type { Routing } from '../../../domain/states';
import type { CreateWizard } from '../useCreateWizard';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  hint: { color: tokens.colorNeutralForeground3 },
  results: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, maxHeight: '180px', overflowY: 'auto' },
  row: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, padding: tokens.spacingVerticalXS },
  grow: { flexGrow: 1 },
});

export function ParticipantsStep({ wizard }: { wizard: CreateWizard }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<UserSummary[]>([]);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    if (!query.trim()) { setResults([]); return; }
    let cancelled = false;
    setSearching(true);
    sigilApi.searchUsers(query)
      .then((r) => { if (!cancelled) setResults(r); })
      .catch(() => { if (!cancelled) setResults([]); })
      .finally(() => { if (!cancelled) setSearching(false); });
    return () => { cancelled = true; };
  }, [query]);

  const added = new Set(wizard.draft.participants.map((p) => p.userId));
  const candidates = results.filter((u) => !added.has(u.id));

  return (
    <div className={s.root}>
      <Field label={t('create.routing')}>
        <RadioGroup value={wizard.draft.routing} onChange={(_e, d) => wizard.setRouting(d.value as Routing)} layout="horizontal">
          <Radio value="sequential" label={t('create.seq')} />
          <Radio value="parallel" label={t('create.par')} />
        </RadioGroup>
      </Field>
      <Text size={200} className={s.hint}>{wizard.draft.routing === 'sequential' ? t('create.seqHint') : t('create.parHint')}</Text>

      <Field label={t('create.addSignerHeading')}>
        <Input value={query} onChange={(_e, d) => setQuery(d.value)} placeholder={t('create.searchPh')} contentBefore={<PersonAddRegular />} />
      </Field>
      {searching && <Spinner size="tiny" label={t('create.searching')} />}
      {query.trim() && !searching && candidates.length === 0 && <Text size={200} className={s.hint}>{t('create.noResults')}</Text>}
      <div className={s.results}>
        {candidates.map((u) => (
          <div key={u.id} className={s.row}>
            <Avatar name={u.name} size={28} />
            <div className={s.grow}>
              <Text>{u.name}</Text>{u.email && <><br /><Text size={200} className={s.hint}>{u.email}</Text></>}
            </div>
            <Button size="small" icon={<AddRegular />} aria-label={t('create.addSignerHeading')} onClick={() => wizard.addParticipant(u)} />
          </div>
        ))}
      </div>

      <Text weight="semibold">{t('create.participantsHeading', { count: wizard.draft.participants.length })}</Text>
      {wizard.draft.participants.length === 0 && <Text size={200} className={s.hint}>{t('create.noParticipants')}</Text>}
      {wizard.draft.participants.map((p) => (
        <div key={p.userId} className={s.row}>
          <Avatar name={p.name} size={28} />
          <div className={s.grow}>
            <Text>{p.name}</Text>
            {wizard.draft.routing === 'sequential' && p.order !== undefined && (
              <> <Badge appearance="tint" size="small">{t('create.order', { n: p.order })}</Badge></>
            )}
          </div>
          {wizard.draft.routing === 'sequential' && (
            <>
              <Button size="small" appearance="subtle" icon={<ArrowUpRegular />} aria-label={t('create.moveUp')} onClick={() => wizard.moveParticipant(p.userId, -1)} />
              <Button size="small" appearance="subtle" icon={<ArrowDownRegular />} aria-label={t('create.moveDown')} onClick={() => wizard.moveParticipant(p.userId, 1)} />
            </>
          )}
          <Button size="small" appearance="subtle" icon={<DismissRegular />} aria-label={t('create.removeSigner')} onClick={() => wizard.removeParticipant(p.userId)} />
        </div>
      ))}
    </div>
  );
}
