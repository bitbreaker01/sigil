// "How it looks on a document" mockups for onboarding (RF-01). Renders the signature inside a 3:1
// zone (the normalized ratio) over a few DIFFERENT fake document layouts, so the user can judge
// whether their signature reads well across contexts — without needing real PDFs.

import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';

const useStyles = makeStyles({
  row: { display: 'flex', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  page: {
    flex: '1 1 220px',
    minWidth: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow8,
    padding: tokens.spacingHorizontalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  lines: { display: 'flex', flexDirection: 'column', gap: '6px' },
  line: { height: '7px', borderRadius: '4px', backgroundColor: tokens.colorNeutralBackground4 },
  sigBlock: { marginTop: tokens.spacingVerticalL, display: 'flex', flexDirection: 'column', gap: '2px' },
  sigZone: { aspectRatio: '3 / 1', display: 'flex', alignItems: 'flex-end' },
  sig: { width: '100%', height: '100%', objectFit: 'contain' },
  sigLine: { borderTop: `1px solid ${tokens.colorNeutralForeground3}`, paddingTop: '2px' },
  boxed: { border: `1px dashed ${tokens.colorNeutralStroke1}`, borderRadius: tokens.borderRadiusSmall, padding: '6px' },
  caption: { color: tokens.colorNeutralForeground3 },
});

// per-variant: line widths, how many, signature horizontal placement, zone width, boxed style
const VARIANTS = [
  { lines: ['92%', '100%', '85%', '96%', '72%'], align: 'flex-end', zoneW: '48%', boxed: false },
  { lines: ['100%', '90%', '100%', '60%'], align: 'center', zoneW: '60%', boxed: true },
  { lines: ['80%', '100%', '95%', '88%', '100%', '65%'], align: 'flex-start', zoneW: '44%', boxed: false },
] as const;

function Mockup(props: { signature: string; variant: 0 | 1 | 2 }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const v = VARIANTS[props.variant];
  return (
    <div className={s.page}>
      <div className={s.lines}>{v.lines.map((w, i) => <div key={i} className={s.line} style={{ width: w }} />)}</div>
      <div className={s.sigBlock} style={{ alignItems: v.align }}>
        <div className={`${s.sigZone} ${v.boxed ? s.boxed : ''}`} style={{ width: v.zoneW }}>
          <img className={s.sig} src={`data:image/png;base64,${props.signature}`} alt={t('onboarding.mockupSignatory')} />
        </div>
        {!v.boxed && <div className={s.sigLine} style={{ width: v.zoneW }} />}
        <Text size={100} className={s.caption}>{t('onboarding.mockupSignatory')}</Text>
      </div>
    </div>
  );
}

export function SignatureMockup(props: { signature: string }): JSX.Element {
  const s = useStyles();
  return (
    <div className={s.row}>
      <Mockup signature={props.signature} variant={0} />
      <Mockup signature={props.signature} variant={1} />
      <Mockup signature={props.signature} variant={2} />
    </div>
  );
}
