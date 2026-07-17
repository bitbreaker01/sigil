// Persistent header (doc 05 §4/§8): Sigil brand, primary nav, language toggle, current user.
// Responsive by design — the nav is inline on desktop and collapses into a menu on phones
// (no horizontal scrolling). Purely presentational (container-presentational pattern).

import {
  makeStyles, tokens, Text, Button, Avatar, Toolbar, ToolbarButton,
  Menu, MenuTrigger, MenuPopover, MenuList, MenuItem,
} from '@fluentui/react-components';
import { Translate24Regular, ShieldCheckmark24Filled, Navigation24Regular } from '@fluentui/react-icons';
import type { Screen } from '../lib/navigation';

const MOBILE = '@media (max-width: 640px)';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingInline: tokens.spacingHorizontalM,
    height: '56px',
    maxWidth: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, cursor: 'pointer', flexShrink: 0, minWidth: 0 },
  brandName: { [MOBILE]: { display: 'none' } }, // logo-only on phones to save room
  logo: { color: tokens.colorBrandForeground1, flexShrink: 0 },
  navInline: { display: 'flex', gap: tokens.spacingHorizontalXS, [MOBILE]: { display: 'none' } },
  navMenu: { display: 'none', [MOBILE]: { display: 'inline-flex' } },
  right: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
});

export interface HeaderProps {
  appName: string;
  userName: string;
  navLabels: { dashboard: string; create: string; verify: string; signature: string };
  toggleLangLabel: string;
  menuLabel: string;
  currentScreen: Screen;
  onNavigate: (p: Screen) => void;
  onToggleLang: () => void;
}

export function Header(props: HeaderProps): JSX.Element {
  const s = useStyles();
  const { navLabels, currentScreen } = props;
  const items: { key: Screen; label: string }[] = [
    { key: 'dashboard', label: navLabels.dashboard },
    { key: 'create', label: navLabels.create },
    { key: 'verify', label: navLabels.verify },
    { key: 'onboarding', label: navLabels.signature },
  ];

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
        <Text className={s.brandName} weight="semibold" size={400}>{props.appName}</Text>
      </div>

      <Toolbar className={s.navInline} aria-label={props.appName}>
        {items.map((it) => (
          <ToolbarButton key={it.key} appearance={currentScreen === it.key ? 'primary' : 'subtle'} onClick={() => props.onNavigate(it.key)}>
            {it.label}
          </ToolbarButton>
        ))}
      </Toolbar>

      <div className={s.right}>
        <Button appearance="subtle" icon={<Translate24Regular />} aria-label={props.toggleLangLabel} onClick={props.onToggleLang} />
        <Avatar name={props.userName} size={32} color="brand" />
        <div className={s.navMenu}>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button appearance="subtle" icon={<Navigation24Regular />} aria-label={props.menuLabel} />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {items.map((it) => (
                  <MenuItem key={it.key} onClick={() => props.onNavigate(it.key)}>{it.label}</MenuItem>
                ))}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      </div>
    </header>
  );
}
