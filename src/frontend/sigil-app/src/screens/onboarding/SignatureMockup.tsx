// A lightweight "how it looks on a document" mockup for onboarding (RF-01): renders the signature
// inside a 3:1 signature zone (the normalized ratio) over a fake document page, so the user can
// judge whether their signature reads well before committing — without needing a real PDF.

import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { useT } from '../../i18n/useT';

const useStyles = makeStyles({
  page: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow8,
    padding: tokens.spacingHorizontalL,
    maxWidth: '420px',
    width: '100%',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  lines: { display: 'flex', flexDirection: 'column', gap: '8px' },
  line: { height: '8px', borderRadius: '4px', backgroundColor: tokens.colorNeutralBackground4 },
  sigBlock: { marginTop: tokens.spacingVerticalXL, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: '2px' },
  // 3:1 zone, the signature stretched to fill exactly as the sealing engine embeds it.
  sigZone: { width: '46%', aspectRatio: '3 / 1', display: 'flex', alignItems: 'flex-end' },
  sig: { width: '100%', height: '100%', objectFit: 'contain' },
  sigLine: { width: '46%', borderTop: `1px solid ${tokens.colorNeutralForeground3}`, paddingTop: '2px' },
});

const WIDTHS = ['92%', '100%', '85%', '96%', '70%'];

export function SignatureMockup(props: { signature: string }): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  return (
    <div className={s.page}>
      <div className={s.lines}>
        {WIDTHS.map((w, i) => <div key={i} className={s.line} style={{ width: w }} />)}
      </div>
      <div className={s.sigBlock}>
        <div className={s.sigZone}><img className={s.sig} src={`data:image/png;base64,${props.signature}`} alt={t('onboarding.mockupSignatory')} /></div>
        <div className={s.sigLine} />
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{t('onboarding.mockupSignatory')}</Text>
      </div>
    </div>
  );
}
