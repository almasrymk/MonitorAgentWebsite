import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { DICTIONARY, I18n, Session, startLanguage } from 'shared';
import { routes } from './app.routes';
import { PORTAL_TEXTS } from './texts';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })),
    { provide: DICTIONARY, useValue: PORTAL_TEXTS },
    provideAppInitializer(() => {
      startLanguage(inject(I18n));
      // A CSRF token before anything is posted; failing here only means the first POST will ask again.
      return inject(Session).refreshCsrf().catch(() => undefined);
    }),
  ],
};
