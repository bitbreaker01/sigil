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
    paddingInline: tokens.spacingHorizontalL,
    height: '56px',
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    position: 'sticky',
    top: 0,
    zIndex: 10,
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, cursor: 'pointer' },
  logo: { color: tokens.colorBrandForeground1 },
  nav: { display: 'flex', gap: tokens.spacingHorizontalXS },
  right: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM },
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
        <Button appearance="subtle" icon={<Translate24Regular />} onClick={props.onToggleLang}>
          {props.toggleLangLabel}
        </Button>
        <Avatar name={props.userName} size={32} color="brand" />
      </div>
    </header>
  );
}
