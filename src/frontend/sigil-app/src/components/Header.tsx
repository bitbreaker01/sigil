// Persistent header (doc 05 §4/§8): Sigil name, language toggle, current user.
// Purely presentational — receives everything via props (container-presentational pattern).

import {
  makeStyles,
  tokens,
  Text,
  Button,
  Avatar,
  Toolbar,
  ToolbarButton,
} from '@fluentui/react-components';
import { Translate24Regular, ShieldCheckmark24Filled } from '@fluentui/react-icons';
import type { Screen } from '../lib/navigation';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    paddingInline: tokens.spacingHorizontalM,
    height: '56px',
    maxWidth: '100%', // never push the page wider than the viewport (mobile, doc §8)
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, cursor: 'pointer', flexShrink: 0 },
  logo: { color: tokens.colorBrandForeground1 },
  // The nav can shrink and scroll horizontally WITHIN itself instead of widening the page.
  nav: { display: 'flex', gap: tokens.spacingHorizontalXS, minWidth: 0, overflowX: 'auto', flexShrink: 1 },
  right: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexShrink: 0 },
});

export interface HeaderProps {
  appName: string;
  userName: string;
  navLabels: { dashboard: string; create: string; verify: string };
  toggleLangLabel: string;
  currentScreen: Screen;
  onNavigate: (p: Screen) => void;
  onToggleLang: () => void;
}

export function Header(props: HeaderProps): JSX.Element {
  const s = useStyles();
  const { navLabels, currentScreen } = props;
  return (
    <header className={s.root}>
      <div
        className={s.brand}
        onClick={() => props.onNavigate('dashboard')}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => e.key === 'Enter' && props.onNavigate('dashboard')}
      >
        <ShieldCheckmark24Filled className={s.logo} />
        <Text weight="semibold" size={400}>{props.appName}</Text>
      </div>

      <Toolbar className={s.nav} aria-label={props.appName}>
        <ToolbarButton appearance={currentScreen === 'dashboard' ? 'primary' : 'subtle'} onClick={() => props.onNavigate('dashboard')}>
          {navLabels.dashboard}
        </ToolbarButton>
        <ToolbarButton appearance={currentScreen === 'create' ? 'primary' : 'subtle'} onClick={() => props.onNavigate('create')}>
          {navLabels.create}
        </ToolbarButton>
        <ToolbarButton appearance={currentScreen === 'verify' ? 'primary' : 'subtle'} onClick={() => props.onNavigate('verify')}>
          {navLabels.verify}
        </ToolbarButton>
      </Toolbar>

      <div className={s.right}>
        {/* Icon-only: the label (target language) is the aria-label — keeps the header narrow on phones. */}
        <Button appearance="subtle" icon={<Translate24Regular />} aria-label={props.toggleLangLabel} onClick={props.onToggleLang} />
        <Avatar name={props.userName} size={32} color="brand" />
      </div>
    </header>
  );
}
