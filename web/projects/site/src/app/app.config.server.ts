import { HTTP_TRANSFER_CACHE_ORIGIN_MAP } from '@angular/common/http';
import { ApplicationConfig, inject, mergeApplicationConfig, REQUEST } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { API_BASE_URL } from 'shared';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

const apiUrl = process.env['API_URL'] ?? 'http://localhost:5080';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Server-side rendering calls the API directly (for example http://api:8080 inside docker compose).
    { provide: API_BASE_URL, useValue: apiUrl },
    // The browser reuses what the server fetched instead of asking again: map the API's address to the visitor's origin,
    // where a reverse proxy serves the same API under /api.
    {
      provide: HTTP_TRANSFER_CACHE_ORIGIN_MAP,
      useFactory: () => {
        const request = inject(REQUEST, { optional: true });
        return request ? { [new URL(apiUrl).origin]: new URL(request.url).origin } : {};
      },
    },
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
