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
  const creatorName = (id: string) => d.creators.find((c) => c.id === id)?.name ?? id;
  const participantName = (id: string) => d.participants.find((p) => p.id === id)?.name ?? id;

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

        <Field label={t('documents.creator')} className={s.field}>
          <Dropdown value={d.filters.creatorId ? creatorName(d.filters.creatorId) : t('documents.anyCreator')}
            selectedOptions={[d.filters.creatorId]}
            onOptionSelect={(_e, data) => d.setCreator(data.optionValue ?? '')}>
            <Option value="">{t('documents.anyCreator')}</Option>
            {d.creators.map((c) => <Option key={c.id} value={c.id}>{c.name}</Option>)}
          </Dropdown>
        </Field>

        <Field label={t('documents.participant')} className={s.field}>
          <Dropdown value={d.filters.participantId ? participantName(d.filters.participantId) : t('documents.anyParticipant')}
            selectedOptions={[d.filters.participantId]}
            onOptionSelect={(_e, data) => d.setParticipant(data.optionValue ?? '')}>
            <Option value="">{t('documents.anyParticipant')}</Option>
            {d.participants.map((p) => <Option key={p.id} value={p.id}>{p.name}</Option>)}
          </Dropdown>
        </Field>

        <Field label={t('documents.status')} className={s.field}>
          <Dropdown value={d.filters.status === 'all' ? t('documents.anyStatus') : statusText(d.filters.status)}
            selectedOptions={[String(d.filters.status)]}
            onOptionSelect={(_e, data) => d.setStatus(data.optionValue === 'all' ? 'all' : Number(data.optionValue))}>
            <Option value="all">{t('documents.anyStatus')}</Option>
            {d.statuses.map((st) => <Option key={st} value={String(st)}>{statusText(st)}</Option>)}
          </Dropdown>
        </Field>

        {d.signatureVersions.length > 0 && (
          <Field label={t('documents.signatureVersion')} className={s.field}>
            <Dropdown value={d.filters.signatureVersion === 'all' ? t('documents.anySignatureVersion') : t('documents.versionLabel', { n: d.filters.signatureVersion })}
              selectedOptions={[String(d.filters.signatureVersion)]}
              onOptionSelect={(_e, data) => d.setSignatureVersion(data.optionValue === 'all' ? 'all' : Number(data.optionValue))}>
              <Option value="all">{t('documents.anySignatureVersion')}</Option>
              {d.signatureVersions.map((v) => <Option key={v} value={String(v)}>{t('documents.versionLabel', { n: v })}</Option>)}
            </Dropdown>
          </Field>
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
