// Step 3 (doc 05 §6.3, RF-28): the zone editor. Signers place zones by drawing on the rendered
// PDF (no default position). Provides, besides drag/resize, numeric x/y/w/h inputs (accessibility
// + precision) and a per-signer checklist that blocks the step until every signer has a zone.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  makeStyles, tokens, Text, Button, Spinner, Field, Input, Badge, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { ChevronLeftRegular, ChevronRightRegular, CheckmarkCircleFilled, ErrorCircleRegular } from '@fluentui/react-icons';
import { useT } from '../../../i18n/useT';
import { clampCoord } from '../../../pdf/zoneGeometry';
import { usePdfDocument } from '../../../pdf/usePdfDocument';
import { PdfPage, type RenderedSize } from '../../../pdf/PdfPage';
import { ZoneOverlay, type SignerStyle } from '../../../pdf/ZoneOverlay';
import type { CreateWizard } from '../useCreateWizard';

const PALETTE = ['#0f6cbd', '#c50f1f', '#0e7a0b', '#8764b8', '#c19c00', '#038387'];

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  layout: { display: 'flex', gap: tokens.spacingHorizontalL, flexWrap: 'wrap' },
  viewer: { flexGrow: 1, minWidth: '320px' },
  side: { width: '260px', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  chips: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalXS },
  chip: { cursor: 'pointer', border: '2px solid transparent' },
  chipOn: { border: `2px solid ${tokens.colorNeutralForeground1}` },
  pager: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, justifyContent: 'center' },
  checkRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  coords: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: tokens.spacingHorizontalS },
  hint: { color: tokens.colorNeutralForeground3 },
});

export function ZonesStep({ wizard }: { wizard: CreateWizard }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const pdf = usePdfDocument(wizard.draft.pdf?.base64);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState<RenderedSize | undefined>(undefined);
  const [armed, setArmed] = useState<string | undefined>(undefined);
  const [selectedId, setSelectedId] = useState<string | undefined>(undefined);
  const [width, setWidth] = useState(680);
  const viewerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = viewerRef.current;
    if (!el) return;
    // Match the canvas to the actual container width (never wider than it), so the pointer↔canvas
    // mapping stays 1:1 even on very narrow screens (no hard floor above the container).
    const measure = () => setWidth(Math.max(1, Math.min(el.clientWidth, 900)));
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const styleMap = useMemo(() => {
    const m = new Map<string, SignerStyle>();
    wizard.draft.participants.forEach((p, i) => m.set(p.userId, { label: p.name, color: PALETTE[i % PALETTE.length]! }));
    return m;
  }, [wizard.draft.participants]);

  const onRendered = useCallback((sz: RenderedSize) => setSize(sz), []);
  const selected = wizard.draft.zones.find((z) => z.id === selectedId);

  // Numeric path (accessibility, §6.3): clampCoord enforces [0,100] AND fit against the sibling
  // dimension so x+w / y+h can never exceed 100 (matches the drag path).
  const setCoord = (key: 'x' | 'y' | 'w' | 'h', value: string) => {
    if (!selected) return;
    wizard.updateZone(selected.id, { [key]: clampCoord(selected, key, Number(value)) });
  };

  return (
    <div className={s.root}>
      <Text size={500} weight="semibold">{t('create.zonesHeading')}</Text>
      <Text size={200} className={s.hint}>{t('create.zonesIntro')}</Text>

      <div className={s.layout}>
        <div className={s.viewer} ref={viewerRef}>
          {pdf.phase === 'loading' && <Spinner label={t('create.pdfProcessing')} />}
          {pdf.phase === 'error' && <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>}
          {pdf.phase === 'ready' && (
            <>
              <div className={s.pager}>
                <Button appearance="subtle" icon={<ChevronLeftRegular />} aria-label={t('create.prev')} disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} />
                <Text>{t('create.pageOf', { n: page, total: pdf.pageCount })}</Text>
                <Button appearance="subtle" icon={<ChevronRightRegular />} aria-label={t('create.nextPage')} disabled={page >= pdf.pageCount} onClick={() => setPage((p) => Math.min(pdf.pageCount, p + 1))} />
              </div>
              <PdfPage doc={pdf.doc} pageNumber={page} width={width} onRendered={onRendered}>
                {size && (
                  <ZoneOverlay
                    page={page}
                    zones={wizard.draft.zones}
                    size={size}
                    styles={styleMap}
                    armedSignerId={armed}
                    selectedZoneId={selectedId}
                    onSelect={setSelectedId}
                    onAdd={(z) => { const id = wizard.addZone(z); setSelectedId(id); }}
                    onUpdate={(id, r) => wizard.updateZone(id, r)}
                    onRemove={(id) => { wizard.removeZone(id); setSelectedId(undefined); }}
                  />
                )}
              </PdfPage>
            </>
          )}
        </div>

        <div className={s.side}>
          <Field label={t('create.armPrompt')}>
            <div className={s.chips}>
              {wizard.draft.participants.map((p, i) => (
                <Badge key={p.userId} appearance="filled" size="large"
                  className={`${s.chip} ${armed === p.userId ? s.chipOn : ''}`}
                  style={{ backgroundColor: PALETTE[i % PALETTE.length] }}
                  onClick={() => setArmed(armed === p.userId ? undefined : p.userId)}>
                  {p.name}
                </Badge>
              ))}
            </div>
          </Field>
          {armed && <Text size={200} className={s.hint}>{t('create.drawHint')}</Text>}

          <Text weight="semibold">{t('create.checklist')}</Text>
          {wizard.draft.participants.map((p) => {
            const count = wizard.draft.zones.filter((z) => z.userId === p.userId).length;
            return (
              <div key={p.userId} className={s.checkRow}>
                {count > 0
                  ? <CheckmarkCircleFilled style={{ color: tokens.colorStatusSuccessForeground1 }} />
                  : <ErrorCircleRegular style={{ color: tokens.colorStatusDangerForeground1 }} />}
                <Text style={{ flexGrow: 1 }}>{p.name}</Text>
                <Text size={200} className={s.hint}>{count > 0 ? t('create.zonesCount', { count }) : t('create.missing')}</Text>
              </div>
            );
          })}

          <Text weight="semibold">{t('create.selected')}</Text>
          {selected ? (
            <div className={s.coords}>
              <Field label={t('create.coordX')}><Input type="number" value={selected.x.toString()} onChange={(_e, d) => setCoord('x', d.value)} /></Field>
              <Field label={t('create.coordY')}><Input type="number" value={selected.y.toString()} onChange={(_e, d) => setCoord('y', d.value)} /></Field>
              <Field label={t('create.coordW')}><Input type="number" value={selected.w.toString()} onChange={(_e, d) => setCoord('w', d.value)} /></Field>
              <Field label={t('create.coordH')}><Input type="number" value={selected.h.toString()} onChange={(_e, d) => setCoord('h', d.value)} /></Field>
            </div>
          ) : (
            <Text size={200} className={s.hint}>{t('create.noSelection')}</Text>
          )}
          {selected && <Button appearance="subtle" onClick={() => { wizard.removeZone(selected.id); setSelectedId(undefined); }}>{t('create.removeZone')}</Button>}
        </div>
      </div>
    </div>
  );
}
