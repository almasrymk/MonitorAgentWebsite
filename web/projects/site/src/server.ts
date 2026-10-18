import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import compression from 'compression';
import express from 'express';
import { createHash, randomBytes } from 'node:crypto';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

app.disable('x-powered-by');
app.use(compression());

/** The one inline event handler Angular writes: it switches the deferred stylesheet on once it has loaded. */
const stylesheetOnload = `'sha256-${createHash('sha256').update("this.media='all'").digest('base64')}'`;

/**
 * Security headers for every response. Pages get a fresh nonce per request for the scripts the server renders inline
 * (hydration's event contract and state); nothing else may run inline.
 */
function securityHeaders(res: express.Response, nonce?: string): void {
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  res.setHeader('Permissions-Policy', 'camera=(), microphone=(), geolocation=(), payment=()');
  res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  if (nonce) {
    res.setHeader(
      'Content-Security-Policy',
      [
        "default-src 'self'",
        `script-src 'self' 'nonce-${nonce}' 'unsafe-hashes' ${stylesheetOnload}`,
        // Angular adds component styles at run time; styles can't run code, so inline styles are allowed.
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self'",
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'",
      ].join('; '),
    );
  }
}

/** Puts the request's nonce on every script tag the server rendered. */
export function addNonce(html: string, nonce: string): string {
  return html.replace(/<script(?![^>]*\snonce=)/g, `<script nonce="${nonce}"`);
}

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
    setHeaders: (res) => securityHeaders(res),
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then(async (response) => {
      if (!response) return next();
      const nonce = randomBytes(16).toString('base64');
      securityHeaders(res, nonce);
      if (!response.headers.get('content-type')?.startsWith('text/html')) return writeResponseToNodeResponse(response, res);
      const html = addNonce(await response.text(), nonce);
      const headers = new Headers(response.headers);
      headers.delete('content-length');
      return writeResponseToNodeResponse(new Response(html, { status: response.status, headers }), res);
    })
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
