// Documents screen (Phase 3): server-side search over every document the user is involved in
// (created + signed). Filters/sort/paging run in the backend (SearchDocuments); results load one
// page at a time. Creator/participant filters search users on the fly; status/version are fixed
// lists. Opened from the nav, or from the signature history pre-filtered to a version.

import {
  makeStyles, tokens, Card, Text, Input, Dropdown, Option, Field, Button, Spinner, Badge,
  MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { useQuery } from '@tanstack/react-query';
import { useT } from '../../i18n/useT';
import { sigilApi } from '../../api';
import { TRANSACTION_STATE } from '../../domain/states';
import { TransactionCard } from '../dashboard/TransactionCard';
import { useDocuments } from './useDocuments';
import { FilterCombobox, type ComboOption } from './FilterCombobox';
import { UserSearchCombobox } from './UserSearchCombobox';
import { MultiUserSearch } from './MultiUserSearch';
import type { DocumentSort } from './documentsModel';

const SORTS: DocumentSort[] = ['createdDesc', 'createdAsc', 'sentDesc', 'sentAsc', 'completedDesc', 'completedAsc', 'nameAsc', 'nameDesc'];

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  filters: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalM, alignItems: 'flex-end' },
  search: { flexGrow: 1, minWidth: '220px' },
  field: { minWidth: '160px' },
  chip: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  // Selected-participant chips live on their own full-width row so the filter grid stays aligned.
  chips: { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: tokens.spacingHorizontalS },
  chipTag: { cursor: 'pointer' },
  list: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  meta: { color: tokens.colorNeutralForeground3 },
  more: { display: 'flex', justifyContent: 'center', paddingBlock: tokens.spacingVerticalM },
  empty: { color: tokens.colorNeutralForeground3, paddingBlock: tokens.spacingVerticalXL, textAlign: 'center' },
});

export default function DocumentsScreen(props: {
  onOpen: (txId: string) => void;
  initialSignatureVersion?: number;
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const d = useDocuments(props.initialSignatureVersion);
  const now = Date.now();

  // The caller's own signature versions drive that dropdown (server-side we don't have a loaded set).
  const history = useQuery({ queryKey: ['masterSignatureHistory'], queryFn: () => sigilApi.getMasterSignatureHistory() });
  const versions = history.data ?? [];

  const statusOpts: ComboOption[] = [
    { value: 'all', text: t('documents.anyStatus') },
    ...Object.entries(TRANSACTION_STATE).map(([value, name]) => ({ value, text: t(`transactionState.${name}`) })),
  ];
  const versionOpts: ComboOption[] = [
    { value: 'all', text: t('documents.anySignatureVersion') },
    ...versions.map((v) => ({ value: String(v.version), text: t('documents.versionLabel', { n: v.version }) })),
  ];

  return (
    <Card className={s.root}>
      <Text size={600} weight="semibold">{t('documents.title')}</Text>

      {d.hasVersionFilter && (
        <div className={s.chip}>
          <MessageBar intent="info"><MessageBarBody>{t('documents.fromVersion')}</MessageBarBody></MessageBar>
          <Button appearance="subtle" size="small" icon={<DismissRegular />} onClick={d.clearVersionFilter}>{t('documents.showAll')}</Button>
        </div>
      )}

      <div className={s.filters}>
        <Field label={t('documents.search')} className={s.search}>
          <Input contentBefore={<SearchRegular />} value={d.filters.text}
            onChange={(_e, data) => d.setText(data.value)} placeholder={t('documents.searchPlaceholder')} />
        </Field>

        <UserSearchCombobox label={t('documents.creator')} anyLabel={t('documents.anyCreator')} className={s.field}
          selectedId={d.filters.creatorId} onSelect={(id) => d.setCreator(id)} />

        <MultiUserSearch label={t('documents.participant')} placeholder={t('documents.anyParticipant')} className={s.field}
          selected={d.filters.participants} onAdd={d.addParticipant} />

        <FilterCombobox label={t('documents.status')} placeholder={t('documents.anyStatus')} className={s.field}
          selected={String(d.filters.status)} options={statusOpts}
          onSelect={(v) => d.setStatus(v === 'all' ? 'all' : Number(v))} />

        {versionOpts.length > 1 && (
          <FilterCombobox label={t('documents.signatureVersion')} placeholder={t('documents.anySignatureVersion')} className={s.field}
            selected={String(d.filters.signatureVersion)} options={versionOpts}
            onSelect={(v) => d.setSignatureVersion(v === 'all' ? 'all' : Number(v))} />
        )}

        <Field label={t('documents.sort')} className={s.field}>
          <Dropdown value={t(`documents.sort_${d.filters.sort}`)} selectedOptions={[d.filters.sort]}
            onOptionSelect={(_e, data) => d.setSort((data.optionValue as DocumentSort) ?? 'createdDesc')}>
            {SORTS.map((so) => <Option key={so} value={so}>{t(`documents.sort_${so}`)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      {d.filters.participants.length > 0 && (
        <div className={s.chips}>
          <Text size={200} className={s.meta}>{t('documents.mustInclude')}</Text>
          {d.filters.participants.map((p) => (
            <Badge key={p.id} appearance="tint" color="brand" className={s.chipTag}
              icon={<DismissRegular />} iconPosition="after"
              onClick={() => d.removeParticipant(p.id)}>
              {p.name}
            </Badge>
          ))}
        </div>
      )}

      {d.loading ? <Spinner label={t('common.loading')} />
        : d.error ? <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>
          : (
            <>
              <Text size={200} className={s.meta}>{t('documents.count', { shown: d.rows.length, total: d.total })}</Text>
              {d.rows.length === 0
                ? <div className={s.empty}>{t('documents.noneMatch')}</div>
                : (
                  <>
                    <div className={s.list}>
                      {d.rows.map((tx) => (
                        <TransactionCard key={tx.id} tx={tx} now={now} onOpen={() => props.onOpen(tx.id)} />
                      ))}
                    </div>
                    {d.hasMore && (
                      <div className={s.more}>
                        <Button appearance="secondary" disabled={d.loadingMore} onClick={d.loadMore}>
                          {d.loadingMore ? t('common.loading') : t('documents.loadMore')}
                        </Button>
                      </div>
                    )}
                  </>
                )}
            </>
          )}
    </Card>
  );
}
