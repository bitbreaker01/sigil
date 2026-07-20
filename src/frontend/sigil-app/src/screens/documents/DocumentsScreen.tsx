// Documents screen (Phase 1): one place to search / filter / sort all the documents the user is
// involved in (created + signed). Client-side over TransactionView; "other participants" and
// "my signature version" filters are Phase 2 (need backend data). Opened from the nav, or from the
// signature history pre-filtered to a version's documents.

import {
  makeStyles, tokens, Card, Text, Input, Dropdown, Option, Field, Button, Spinner,
  MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { transactionStateOf } from '../../domain/states';
import { TransactionCard } from '../dashboard/TransactionCard';
import { useDocuments } from './useDocuments';
import { FilterCombobox, type ComboOption } from './FilterCombobox';
import type { DocumentSort } from './documentsModel';

const SORTS: DocumentSort[] = ['createdDesc', 'createdAsc', 'sentDesc', 'sentAsc', 'completedDesc', 'completedAsc', 'nameAsc', 'nameDesc'];

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  filters: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalM, alignItems: 'flex-end' },
  search: { flexGrow: 1, minWidth: '220px' },
  field: { minWidth: '160px' },
  chip: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  meta: { color: tokens.colorNeutralForeground3 },
  empty: { color: tokens.colorNeutralForeground3, paddingBlock: tokens.spacingVerticalXL, textAlign: 'center' },
});

export default function DocumentsScreen(props: {
  onOpen: (txId: string) => void;
  initialDocIds?: readonly string[];
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const d = useDocuments(props.initialDocIds);
  const now = Date.now();

  const statusText = (v: number) => {
    const name = transactionStateOf(v);
    return name ? t(`transactionState.${name}`) : String(v);
  };

  // Option lists for the searchable comboboxes; the first entry resets the filter.
  const creatorOpts: ComboOption[] = [
    { value: '', text: t('documents.anyCreator') },
    ...d.creators.map((c) => ({ value: c.id, text: c.name })),
  ];
  const participantOpts: ComboOption[] = [
    { value: '', text: t('documents.anyParticipant') },
    ...d.participants.map((p) => ({ value: p.id, text: p.name })),
  ];
  const statusOpts: ComboOption[] = [
    { value: 'all', text: t('documents.anyStatus') },
    ...d.statuses.map((st) => ({ value: String(st), text: statusText(st) })),
  ];
  const versionOpts: ComboOption[] = [
    { value: 'all', text: t('documents.anySignatureVersion') },
    ...d.signatureVersions.map((v) => ({ value: String(v), text: t('documents.versionLabel', { n: v }) })),
  ];

  return (
    <Card className={s.root}>
      <Text size={600} weight="semibold">{t('documents.title')}</Text>

      {d.hasDocIdFilter && (
        <div className={s.chip}>
          <MessageBar intent="info"><MessageBarBody>{t('documents.fromVersion')}</MessageBarBody></MessageBar>
          <Button appearance="subtle" size="small" icon={<DismissRegular />} onClick={d.clearDocIds}>{t('documents.showAll')}</Button>
        </div>
      )}

      <div className={s.filters}>
        <Field label={t('documents.search')} className={s.search}>
          <Input contentBefore={<SearchRegular />} value={d.filters.text}
            onChange={(_e, data) => d.setText(data.value)} placeholder={t('documents.searchPlaceholder')} />
        </Field>

        <FilterCombobox label={t('documents.creator')} placeholder={t('documents.anyCreator')} className={s.field}
          selected={d.filters.creatorId} options={creatorOpts} onSelect={(v) => d.setCreator(v)} />

        <FilterCombobox label={t('documents.participant')} placeholder={t('documents.anyParticipant')} className={s.field}
          selected={d.filters.participantId} options={participantOpts} onSelect={(v) => d.setParticipant(v)} />

        <FilterCombobox label={t('documents.status')} placeholder={t('documents.anyStatus')} className={s.field}
          selected={String(d.filters.status)} options={statusOpts}
          onSelect={(v) => d.setStatus(v === 'all' ? 'all' : Number(v))} />

        {d.signatureVersions.length > 0 && (
          <FilterCombobox label={t('documents.signatureVersion')} placeholder={t('documents.anySignatureVersion')} className={s.field}
            selected={String(d.filters.signatureVersion)} options={versionOpts}
            onSelect={(v) => d.setSignatureVersion(v === 'all' ? 'all' : Number(v))} />
        )}

        <Field label={t('documents.sort')} className={s.field}>
          <Dropdown value={t(`documents.sort_${d.filters.sort}`)} selectedOptions={[d.filters.sort]}
            onOptionSelect={(_e, data) => d.setSort((data.optionValue as DocumentSort) ?? 'sentDesc')}>
            {SORTS.map((so) => <Option key={so} value={so}>{t(`documents.sort_${so}`)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      {d.loading ? <Spinner label={t('common.loading')} />
        : d.error ? <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>
          : (
            <>
              <Text size={200} className={s.meta}>{t('documents.count', { shown: d.results.length, total: d.total })}</Text>
              {d.results.length === 0
                ? <div className={s.empty}>{t('documents.noneMatch')}</div>
                : (
                  <div className={s.list}>
                    {d.results.map((tx) => (
                      <TransactionCard key={tx.id} tx={tx} now={now} onOpen={() => props.onOpen(tx.id)} />
                    ))}
                  </div>
                )}
            </>
          )}
    </Card>
  );
}
