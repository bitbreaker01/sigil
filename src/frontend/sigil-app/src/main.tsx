import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import { FluentProvider, webLightTheme, Toaster, tokens } from '@fluentui/react-components';
import { TOASTER_ID } from './app/toast';
import { QueryClientProvider } from '@tanstack/react-query';
import { I18nextProvider } from 'react-i18next';
import { PowerProvider } from './app/PowerProvider';
import { createQueryClient } from './app/queryClient';
import { initI18n } from './i18n';
import { App } from './App';

const i18n = initI18n();
const queryClient = createQueryClient();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      {/* Fill the viewport with the shell gray: FluentProvider's default is colorNeutralBackground1
          (white) and auto-height, so on mobile (100vh vs dynamic viewport + overscroll) the white
          showed below the content. Making it 100dvh + the shell gray removes the white gap for good. */}
      <FluentProvider theme={webLightTheme} style={{ minHeight: '100dvh', backgroundColor: tokens.colorNeutralBackground2 }}>
        <QueryClientProvider client={queryClient}>
          <PowerProvider>
            <App />
            <Toaster toasterId={TOASTER_ID} />
          </PowerProvider>
        </QueryClientProvider>
      </FluentProvider>
    </I18nextProvider>
  </StrictMode>,
);
