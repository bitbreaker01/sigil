// Step 1 (doc 05 §4.2): the request header + PDF. Cheap validation (extension/size) happens
// BEFORE encoding base64 (doc 04 §3.4 — never encode 27 MB just to reject it). Once accepted,
// the file is encoded and its page count read (needed to validate zone pages downstream).

import { useRef, useState } from 'react';
import { makeStyles, tokens, Field, Input, Textarea, Button, Spinner, Text, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { DocumentPdfRegular, ArrowUpload20Regular } from '@fluentui/react-icons';
import { useT } from '../../../i18n/useT';
import { validatePdf } from '../../../api/validations';
import { bytesToBase64 } from '../../../api/binaries';
import { readPdfPageCount } from '../../../pdf/readPageCount';
import type { CreateWizard } from '../useCreateWizard';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  pdfBox: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, padding: tokens.spacingVerticalM, borderRadius: tokens.borderRadiusMedium, backgroundColor: tokens.colorNeutralBackground3 },
  grow: { flexGrow: 1 },
});

export function PdfStep({ wizard }: { wizard: CreateWizard }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const [processing, setProcessing] = useState(false);
  const [error, setError] = useState<string | undefined>(undefined);
  const inputRef = useRef<HTMLInputElement>(null);

  const onPick = async (file: File | undefined) => {
    if (!file) return;
    setError(undefined);
    const check = validatePdf(file, wizard.limits.maxPdfKb);
    if (!check.ok) { setError(check.errors[0]); return; }
    setProcessing(true);
    try {
      const base64 = await bytesToBase64(new Uint8Array(await file.arrayBuffer()));
      const pageCount = await readPdfPageCount(base64);
      wizard.setPdf({ file, base64, pageCount });
      if (!wizard.draft.name.trim()) wizard.setName(file.name.replace(/\.pdf$/i, ''));
    } catch {
      setError('common.genericError');
    } finally {
      setProcessing(false);
    }
  };

  return (
    <div className={s.root}>
      <Text size={500} weight="semibold">{t('create.pdfHeading')}</Text>

      <input ref={inputRef} type="file" accept="application/pdf" hidden
        onChange={(e) => { void onPick(e.target.files?.[0]); e.target.value = ''; }} />

      {processing ? (
        <Spinner label={t('create.pdfProcessing')} />
      ) : wizard.draft.pdf ? (
        <div className={s.pdfBox}>
          <DocumentPdfRegular fontSize={28} />
          <div className={s.grow}>
            <Text weight="semibold">{wizard.draft.pdf.file.name}</Text>
            <br />
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              {t('create.pdfPages', { count: wizard.draft.pdf.pageCount })}
            </Text>
          </div>
          <Button icon={<ArrowUpload20Regular />} onClick={() => inputRef.current?.click()}>{t('create.pdfReplace')}</Button>
        </div>
      ) : (
        <Button appearance="primary" icon={<ArrowUpload20Regular />} onClick={() => inputRef.current?.click()}>
          {t('create.pdfSelect')}
        </Button>
      )}

      {error && <MessageBar intent="error"><MessageBarBody>{t(error)}</MessageBarBody></MessageBar>}

      <Field label={t('create.name')}>
        <Input value={wizard.draft.name} onChange={(_e, d) => wizard.setName(d.value)} placeholder={t('create.namePh')} />
      </Field>
      <Field label={t('create.message')}>
        <Textarea value={wizard.draft.message} onChange={(_e, d) => wizard.setMessage(d.value)} placeholder={t('create.messagePh')} resize="vertical" />
      </Field>
      <Field label={t('create.expiration')} hint={t('create.expirationHint')}>
        <Input type="number" min={1} value={wizard.draft.expirationDays?.toString() ?? ''}
          onChange={(_e, d) => wizard.setExpirationDays(d.value ? Number(d.value) : undefined)} />
      </Field>
    </div>
  );
}
