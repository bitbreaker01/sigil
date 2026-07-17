import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, webLightTheme, Toaster } from '@fluentui/react-components';
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
      <FluentProvider theme={webLightTheme}>
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
