import { provideHttpClient, withXsrfConfiguration, withXhr } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { DICTIONARY, I18n, Session, SIGN_IN_OPTIONS, startLanguage } from 'shared';
import { routes } from './app.routes';
import { ADMIN_TEXTS } from './texts';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withXhr(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
    ),
    { provide: DICTIONARY, useValue: ADMIN_TEXTS },
    // Staff accounts are created by an admin, and reset their password through the portal.
    { provide: SIGN_IN_OPTIONS, useValue: { selfService: false } },
    provideAppInitializer(() => {
      startLanguage(inject(I18n));
      return inject(Session)
        .refreshCsrf()
        .catch(() => undefined);
    }),
  ],
};
