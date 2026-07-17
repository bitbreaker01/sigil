// Read-only zone overlay for the Sign screen (doc 05 §4.3): the signer SEES where their signature
// will land — their zones highlighted with a label, everyone else's shown neutral. No interaction
// (this is consent, not editing). Coordinates via the shared %-contract on the rendered canvas.

import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { percentToPx } from '../../api/coordinates';
import type { ZoneView } from '../../api/SigilApi';
import type { RenderedSize } from '../../pdf/PdfPage';

const useStyles = makeStyles({
  layer: { position: 'absolute', inset: 0, pointerEvents: 'none' },
  zone: { position: 'absolute', boxSizing: 'border-box', borderRadius: tokens.borderRadiusSmall },
  mine: { border: `2px solid ${tokens.colorBrandStroke1}`, backgroundColor: 'rgba(15,108,189,0.18)' },
  other: { border: `2px dashed ${tokens.colorNeutralStroke1}`, backgroundColor: 'rgba(0,0,0,0.05)' },
  label: {
    position: 'absolute', top: '-18px', left: 0, whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase100, padding: '0 4px', borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorBrandBackground, color: tokens.colorNeutralForegroundOnBrand,
  },
});

export function SignZoneOverlay(props: {
  page: number;
  myZones: readonly ZoneView[];
  otherZones: readonly ZoneView[];
  size: RenderedSize;
  myLabel: string;
}): JSX.Element {
  const s = useStyles();
  const rect = (z: ZoneView) => percentToPx({ x: z.x, y: z.y, w: z.w, h: z.h }, props.size.width, props.size.height);
  return (
    <div className={s.layer}>
      {props.otherZones.filter((z) => z.page === props.page).map((z) => {
        const px = rect(z);
        return <div key={z.id} className={`${s.zone} ${s.other}`} style={{ left: px.xPx, top: px.yPx, width: px.wPx, height: px.hPx }} />;
      })}
      {props.myZones.filter((z) => z.page === props.page).map((z) => {
        const px = rect(z);
        return (
          <div key={z.id} className={`${s.zone} ${s.mine}`} style={{ left: px.xPx, top: px.yPx, width: px.wPx, height: px.hPx }}>
            <Text className={s.label}>{props.myLabel}</Text>
          </div>
        );
      })}
    </div>
  );
}
