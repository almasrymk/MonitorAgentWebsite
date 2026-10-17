import { HttpInterceptorFn } from '@angular/common/http';
import { inject, REQUEST } from '@angular/core';
import { API_BASE_URL } from 'shared';

/**
 * While rendering on the server, API calls come from the site's own container. Passing on the visitor's address
 * (which the edge put in X-Forwarded-For) keeps the API's per-visitor rate limits per visitor instead of one shared
 * budget for every page the site renders. The API only believes the header from its trusted proxy network.
 * In the browser there is no incoming request, so nothing is added.
 */
export const forwardVisitor: HttpInterceptorFn = (request, next) => {
  const visitor = inject(REQUEST, { optional: true })?.headers.get('x-forwarded-for');
  if (!visitor || !request.url.startsWith(inject(API_BASE_URL))) return next(request);
  return next(request.clone({ setHeaders: { 'X-Forwarded-For': visitor } }));
};
