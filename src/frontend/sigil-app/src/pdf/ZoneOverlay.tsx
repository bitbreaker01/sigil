// Zone editor overlay (doc 05 §6.3, RF-28). Absolutely positioned over the rendered PDF page.
// Signers place their signature zones by DRAWING a rectangle (no default position, RF-28) and
// then dragging/resizing it. Everything is kept in the shared %-coordinate contract (zoom
// independent) — px is only a transient view computed from the rendered canvas size.

import { useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { makeStyles, tokens, Button } from '@fluentui/react-components';
import { Delete16Regular } from '@fluentui/react-icons';
import { pxToPercent, percentToPx, type RectPct } from '../api/coordinates';
import { moveZone, resizeZone } from './zoneGeometry';
import type { WizardZone } from '../screens/create/createWizardModel';
import type { RenderedSize } from './PdfPage';

export interface SignerStyle {
  label: string;
  color: string;
}

const useStyles = makeStyles({
  layer: { position: 'absolute', inset: 0, touchAction: 'none' },
  zone: {
    position: 'absolute',
    boxSizing: 'border-box',
    border: '2px solid',
    cursor: 'move',
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
  },
  label: {
    fontSize: tokens.fontSizeBase100,
    padding: '1px 4px',
    color: tokens.colorNeutralForegroundOnBrand,
    whiteSpace: 'nowrap',
    borderBottomRightRadius: tokens.borderRadiusSmall,
  },
  handle: {
    position: 'absolute',
    right: '-6px',
    bottom: '-6px',
    width: '12px',
    height: '12px',
    borderRadius: '50%',
    backgroundColor: tokens.colorNeutralBackground1,
    border: '2px solid',
    cursor: 'nwse-resize',
  },
  del: { position: 'absolute', top: '-14px', right: '-14px' },
});

type Drag =
  | { mode: 'draw'; startX: number; startY: number }
  | { mode: 'move'; id: string; startX: number; startY: number; orig: RectPct }
  | { mode: 'resize'; id: string; startX: number; startY: number; orig: RectPct };

export function ZoneOverlay(props: {
  page: number;
  zones: WizardZone[];
  size: RenderedSize;
  styles: Map<string, SignerStyle>;
  armedSignerId?: string | undefined;
  selectedZoneId?: string | undefined;
  onSelect: (id: string | undefined) => void;
  onAdd: (zone: Omit<WizardZone, 'id'>) => void;
  onUpdate: (id: string, rect: RectPct) => void;
  onRemove: (id: string) => void;
}): JSX.Element {
  const s = useStyles();
  const { page, size, armedSignerId } = props;
  const layerRef = useRef<HTMLDivElement>(null);
  const drag = useRef<Drag | undefined>(undefined);
  const [preview, setPreview] = useState<RectPct | undefined>(undefined);

  const pageZones = props.zones.filter((z) => z.page === page);

  const localPx = (e: ReactPointerEvent) => {
    const r = layerRef.current!.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };

  const onLayerPointerDown = (e: ReactPointerEvent) => {
    if (!armedSignerId || e.target !== layerRef.current) return; // draw only on empty area
    const p = localPx(e);
    drag.current = { mode: 'draw', startX: p.x, startY: p.y };
    layerRef.current!.setPointerCapture(e.pointerId);
  };

  const onZonePointerDown = (e: ReactPointerEvent, z: WizardZone) => {
    e.stopPropagation();
    props.onSelect(z.id);
    const p = localPx(e);
    drag.current = { mode: 'move', id: z.id, startX: p.x, startY: p.y, orig: rectOf(z) };
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  };

  const onHandlePointerDown = (e: ReactPointerEvent, z: WizardZone) => {
    e.stopPropagation();
    const p = localPx(e);
    drag.current = { mode: 'resize', id: z.id, startX: p.x, startY: p.y, orig: rectOf(z) };
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  };

  const onPointerMove = (e: ReactPointerEvent) => {
    const d = drag.current;
    if (!d) return;
    const p = localPx(e);
    if (d.mode === 'draw') {
      const xPx = Math.min(d.startX, p.x);
      const yPx = Math.min(d.startY, p.y);
      const wPx = Math.abs(p.x - d.startX);
      const hPx = Math.abs(p.y - d.startY);
      setPreview(pxToPercent({ xPx, yPx, wPx, hPx }, size.width, size.height));
    } else {
      const dxPct = ((p.x - d.startX) / size.width) * 100;
      const dyPct = ((p.y - d.startY) / size.height) * 100;
      props.onUpdate(d.id, d.mode === 'move' ? moveZone(d.orig, dxPct, dyPct) : resizeZone(d.orig, dxPct, dyPct));
    }
  };

  const onPointerUp = (e: ReactPointerEvent) => {
    const d = drag.current;
    drag.current = undefined;
    try { layerRef.current?.releasePointerCapture(e.pointerId); } catch { /* not captured */ }
    if (d?.mode === 'draw' && preview && armedSignerId && preview.w > 0.5 && preview.h > 0.5) {
      props.onAdd({ userId: armedSignerId, page, x: preview.x, y: preview.y, w: preview.w, h: preview.h });
    }
    setPreview(undefined);
  };

  return (
    <div
      ref={layerRef}
      className={s.layer}
      style={{ cursor: armedSignerId ? 'crosshair' : 'default' }}
      onPointerDown={onLayerPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
    >
      {pageZones.map((z) => {
        const px = percentToPx(rectOf(z), size.width, size.height);
        const style = props.styles.get(z.userId);
        const color = style?.color ?? tokens.colorBrandStroke1;
        const selected = z.id === props.selectedZoneId;
        return (
          <div
            key={z.id}
            className={s.zone}
            style={{
              left: px.xPx, top: px.yPx, width: px.wPx, height: px.hPx,
              borderColor: color,
              backgroundColor: hexToRgba(color, selected ? 0.28 : 0.16),
            }}
            onPointerDown={(e) => onZonePointerDown(e, z)}
          >
            <span className={s.label} style={{ backgroundColor: color }}>{style?.label ?? z.userId}</span>
            {selected && (
              <span className={s.del}>
                <Button
                  size="small" appearance="subtle" icon={<Delete16Regular />}
                  aria-label={`remove zone ${style?.label ?? z.userId}`}
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={() => props.onRemove(z.id)}
                />
              </span>
            )}
            <span className={s.handle} style={{ borderColor: color }} onPointerDown={(e) => onHandlePointerDown(e, z)} />
          </div>
        );
      })}

      {preview && (
        <div
          className={s.zone}
          style={{
            left: (preview.x / 100) * size.width,
            top: (preview.y / 100) * size.height,
            width: (preview.w / 100) * size.width,
            height: (preview.h / 100) * size.height,
            borderColor: tokens.colorNeutralForeground3,
            backgroundColor: 'rgba(0,0,0,0.08)',
            borderStyle: 'dashed',
            pointerEvents: 'none',
          }}
        />
      )}
    </div>
  );
}

function rectOf(z: WizardZone): RectPct {
  return { x: z.x, y: z.y, w: z.w, h: z.h };
}

/** #rrggbb → rgba() with alpha (Fluent tokens resolve to hex). Falls back to the input as-is. */
function hexToRgba(hex: string, alpha: number): string {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return hex;
  const n = parseInt(m[1]!, 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
}
