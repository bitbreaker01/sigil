// Verify screen. Presentational: consumes useVerify.
// Two paths: (a) deep link with txId → shows the certificate; (b) the user drags/picks
// the PDF → local SHA-256 → verdict Green (intact) / Red (altered) / Gray (not found).
// The file NEVER leaves the browser: only the 64-hex hash travels.

import { useRef, useState, type DragEvent } from 'react';
import {
  makeStyles,
  tokens,
  Card,
  Text,
  Button,
  Spinner,
  Divider,
} from '@fluentui/react-components';
import {
  ShieldCheckmark48Filled,
  Warning48Filled,
  QuestionCircle48Regular,
  ArrowDownload20Regular,
} from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { downloadBase64 } from '../../api/binaries';
import { useVerify, type Certificate } from './useVerify';

const useStyles = makeStyles({
  card: { padding: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  intro: { color: tokens.colorNeutralForeground2 },
  dropzone: {
    border: `2px dashed ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    padding: tokens.spacingVerticalXXL,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    cursor: 'pointer',
    transition: 'border-color 120ms, background-color 120ms',
  },
  dropzoneActive: { border: `2px dashed ${tokens.colorBrandStroke1}`, backgroundColor: tokens.colorBrandBackground2 },
  verdict: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalXL,
    borderRadius: tokens.borderRadiusLarge,
    textAlign: 'center',
  },
  green: { backgroundColor: tokens.colorStatusSuccessBackground1, color: tokens.colorStatusSuccessForeground1 },
  red: { backgroundColor: tokens.colorStatusDangerBackground1, color: tokens.colorStatusDangerForeground1 },
  gray: { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2 },
  row: { display: 'flex', justifyContent: 'space-between', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  hash: { fontFamily: tokens.fontFamilyMonospace, wordBreak: 'break-all', fontSize: tokens.fontSizeBase200 },
  signer: { display: 'flex', flexDirection: 'column' },
});

export default function VerifyScreen(props: { initialTxId?: string | undefined }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { state, verifyFile, initialTxId } = useVerify(props.initialTxId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);

  const choose = (file: File | undefined) => {
    if (file) void verifyFile(file, initialTxId);
  };

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragging(false);
    choose(e.dataTransfer.files?.[0]);
  };

  return (
    <Card className={s.card}>
      <Text size={600} weight="semibold">{t('verify.title')}</Text>
      <Text className={s.intro}>{t('verify.intro')}</Text>

      {state.phase === 'initial' && (
        <div
          className={`${s.dropzone} ${dragging ? s.dropzoneActive : ''}`}
          onClick={() => inputRef.current?.click()}
          onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          role="button"
          tabIndex={0}
          onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') inputRef.current?.click(); }}
        >
          <QuestionCircle48Regular />
          <Text weight="semibold">{t('verify.dropHere')}</Text>
          <Button appearance="primary">{t('verify.selectPdf')}</Button>
        </div>
      )}

      {state.phase === 'computing' && <Spinner label={t('verify.computing')} />}

      {state.phase === 'error' && (
        <div className={`${s.verdict} ${s.gray}`}>
          <Warning48Filled />
          <Text weight="semibold">{t(state.message)}</Text>
        </div>
      )}

      {state.phase === 'result' && (
        <Result certificate={state.certificate} onOther={() => inputRef.current?.click()} />
      )}

      <input
        ref={inputRef}
        type="file"
        accept="application/pdf"
        hidden
        onChange={(e) => { choose(e.target.files?.[0]); e.target.value = ''; }}
      />
    </Card>
  );
}

function Result(props: { certificate: Certificate; onOther: () => void }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const c = props.certificate;

  // Visual verdict: green intact / red altered / gray not found or certificate only.
  const verdictClass = !c.found ? s.gray : c.isIntact === false ? s.red : c.isIntact === true ? s.green : s.gray;

  const downloadTsr = () => {
    if (c.tokenBase64) void downloadBase64(c.tokenBase64, `${c.ledgerNumber ?? 'sigil'}.tsr`, 'application/timestamp-reply');
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div className={`${s.verdict} ${verdictClass}`}>
        {!c.found ? (
          <>
            <QuestionCircle48Regular />
            <Text size={500} weight="bold">{t('verify.notFound')}</Text>
          </>
        ) : c.isIntact === false ? (
          <>
            <Warning48Filled />
            <Text size={500} weight="bold">{t('verify.altered')}</Text>
            <Text>{t('verify.alteredDetail')}</Text>
          </>
        ) : c.isIntact === true ? (
          <>
            <ShieldCheckmark48Filled />
            <Text size={500} weight="bold">{t('verify.intact')}</Text>
            <Text>{t('verify.intactDetail')}</Text>
          </>
        ) : (
          <>
            <ShieldCheckmark48Filled />
            <Text size={500} weight="bold">{t('verify.certificate')}</Text>
          </>
        )}
      </div>

      {c.found && (
        <>
          <Divider>{t('verify.certificate')}</Divider>

          {c.ledgerNumber && (
            <div className={s.row}>
              <Text weight="semibold">{t('verify.ledgerNumber')}</Text>
              <Text>{c.ledgerNumber}</Text>
            </div>
          )}

          {c.sealedOnUtc && (
            <div className={s.row}>
              <Text weight="semibold">{t('verify.sealedOn', { date: new Date(c.sealedOnUtc).toLocaleString() })}</Text>
            </div>
          )}

          {c.finalHashHex && (
            <div>
              <Text weight="semibold">{t('verify.finalHash')}</Text>
              {/* Lowercase to match `sha256sum` output (the hint) for an at-a-glance manual check. */}
              <div className={s.hash}>{c.finalHashHex.toLowerCase()}</div>
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{t('verify.hashHint')}</Text>
            </div>
          )}

          {c.historyIntact !== undefined && (
            <Text style={{ color: c.historyIntact ? tokens.colorStatusSuccessForeground1 : tokens.colorStatusDangerForeground1 }}>
              {c.historyIntact ? t('verify.historyIntact') : t('verify.historyAnomalies')}
            </Text>
          )}

          {c.signers.length > 0 && (
            <div>
              <Text weight="semibold">{t('verify.signers')}</Text>
              {c.signers.map((f, i) => (
                <div key={i} className={s.signer}>
                  <Text>{f.name} · {f.email}</Text>
                  {f.signedOnUtc && (
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                      {new Date(f.signedOnUtc).toLocaleString()}
                    </Text>
                  )}
                </div>
              ))}
            </div>
          )}

          {c.tokenBase64 && (
            <Button icon={<ArrowDownload20Regular />} onClick={downloadTsr}>
              {t('verify.downloadToken')}
            </Button>
          )}
        </>
      )}

      <Button appearance="secondary" onClick={props.onOther}>{t('verify.verifyAnother')}</Button>
    </div>
  );
}
