import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { API_BASE_URL, DICTIONARY } from 'shared';
import { routes } from './app.routes';
import { forwardVisitor } from './forward-visitor';
import { SITE_TEXTS } from './texts';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'enabled' })),
    provideClientHydration(withEventReplay()),
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([forwardVisitor]),
    ),
    { provide: DICTIONARY, useValue: SITE_TEXTS },
    // Absolute, so requests match the ones the server rendered with and are answered from the transferred state.
    { provide: API_BASE_URL, useFactory: () => inject(DOCUMENT).location.origin },
  ],
};
