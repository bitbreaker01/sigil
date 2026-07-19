// Shared PDF viewer (doc 05 §6.1) used by the zone editor and the sign screen. Owns:
//  - ROBUST width measurement via a callback ref (an element-state effect that runs whenever the
//    canvas container (re)appears — fixes the "blank on first entry / must re-enter" bug where a
//    useRef+useLayoutEffect([]) missed the element because it mounted after a loading state).
//  - ZOOM: the page renders at baseWidth×zoom inside a scrollable wrapper, so zooming in enlarges
//    the canvas and you pan by scrolling (needed to place zones precisely). Coordinates stay
//    zoom-independent because the overlay maps against the RENDERED size, not the base width.
//  - Page navigation.
// The overlay is a render-prop given the current page + rendered pixel size.

import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { makeStyles, tokens, Button, Text } from '@fluentui/react-components';
import {
  ChevronLeftRegular, ChevronRightRegular, ZoomInRegular, ZoomOutRegular, ArrowResetRegular,
} from '@fluentui/react-icons';
import { useT } from '../i18n/useT';
import { PdfPage, type RenderedSize } from './PdfPage';
import type { PdfDoc } from './pdfjs';

const ZOOM_STEPS = [1, 1.5, 2, 3];
// Upper bound for the page's base (100%) render width. Raised so wide displays actually use the
// available space (the measured container width still drives it, so phones render small). Zoom on top.
const MAX_BASE_PX = 1100;

const useStyles = makeStyles({
  // width:100% so the viewer fills its container (even under a flex `align-items:center` parent),
  // instead of shrinking to the canvas's own content width.
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, minWidth: 0, width: '100%' },
  bar: { display: 'flex', alignItems: 'center', justifyContent: 'center', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
  sep: { width: '1px', alignSelf: 'stretch', backgroundColor: tokens.colorNeutralStroke2, marginInline: tokens.spacingHorizontalXS },
  // Scrolls when the zoomed page is larger than the container — never widens the page itself.
  scroll: { maxWidth: '100%', overflow: 'auto', display: 'flex', justifyContent: 'center' },
});

export function PdfViewer(props: {
  doc: PdfDoc;
  pageCount: number;
  onFirstRender?: () => void;
  onSize?: (size: RenderedSize) => void; // current rendered pixel size (for callers that map coords outside the overlay)
  children?: (ctx: { page: number; size: RenderedSize }) => React.ReactNode; // optional overlay
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const { onFirstRender, onSize } = props;
  const [anchor, setAnchor] = useState<HTMLDivElement | null>(null);
  const [baseWidth, setBaseWidth] = useState(0);
  const [zoomIdx, setZoomIdx] = useState(0);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState<RenderedSize | undefined>(undefined);
  const rendered = useRef(false);

  useLayoutEffect(() => {
    if (!anchor) return;
    const measure = () => setBaseWidth(Math.max(1, Math.min(anchor.clientWidth, MAX_BASE_PX)));
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(anchor);
    return () => ro.disconnect();
  }, [anchor]);

  const onRendered = useCallback((sz: RenderedSize) => {
    setSize(sz);
    onSize?.(sz);
    if (!rendered.current) { rendered.current = true; onFirstRender?.(); }
  }, [onFirstRender, onSize]);

  const zoom = ZOOM_STEPS[zoomIdx]!;
  const width = Math.round(baseWidth * zoom);

  return (
    <div className={s.root} ref={setAnchor}>
      <div className={s.bar}>
        <Button appearance="subtle" size="small" icon={<ChevronLeftRegular />} aria-label={t('viewer.prevPage')} disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} />
        <Text size={200}>{t('viewer.pageOf', { n: page, total: props.pageCount })}</Text>
        <Button appearance="subtle" size="small" icon={<ChevronRightRegular />} aria-label={t('viewer.nextPage')} disabled={page >= props.pageCount} onClick={() => setPage((p) => Math.min(props.pageCount, p + 1))} />
        <span className={s.sep} />
        <Button appearance="subtle" size="small" icon={<ZoomOutRegular />} aria-label={t('viewer.zoomOut')} disabled={zoomIdx <= 0} onClick={() => setZoomIdx((z) => Math.max(0, z - 1))} />
        <Text size={200}>{Math.round(zoom * 100)}%</Text>
        <Button appearance="subtle" size="small" icon={<ZoomInRegular />} aria-label={t('viewer.zoomIn')} disabled={zoomIdx >= ZOOM_STEPS.length - 1} onClick={() => setZoomIdx((z) => Math.min(ZOOM_STEPS.length - 1, z + 1))} />
        {zoomIdx > 0 && <Button appearance="subtle" size="small" icon={<ArrowResetRegular />} aria-label={t('viewer.resetZoom')} onClick={() => setZoomIdx(0)} />}
      </div>

      <div className={s.scroll}>
        {width > 0 && (
          <PdfPage doc={props.doc} pageNumber={page} width={width} onRendered={onRendered}>
            {size && props.children?.({ page, size })}
          </PdfPage>
        )}
      </div>
    </div>
  );
}
