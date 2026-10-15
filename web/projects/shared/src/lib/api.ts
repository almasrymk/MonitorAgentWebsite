import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, InjectionToken } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Where the API is. Empty in the browser: the apps and the API share one origin (the session and CSRF cookies are
 * __Host- cookies). The site's server sets an absolute address for server-side rendering.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', { factory: () => '' });

/** An API failure: the stable error code from ProblemDetails, or a transport problem. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }

  static from(error: unknown): ApiError {
    if (error instanceof ApiError) return error;
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { code?: string; title?: string; detail?: string } | null;
      if (error.status === 0) return new ApiError(0, 'NETWORK', 'The server could not be reached.');
      return new ApiError(error.status, body?.code ?? `HTTP_${error.status}`, body?.detail ?? body?.title ?? error.message);
    }
    return new ApiError(0, 'UNKNOWN', String(error));
  }
}

/**
 * Small promise-based wrapper over HttpClient. Angular copies the XSRF-TOKEN cookie into X-XSRF-TOKEN on unsafe
 * requests, which is what the API checks.
 */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  get<T>(path: string): Promise<T> {
    return this.run(this.http.get<T>(this.base + path));
  }

  post<T = void>(path: string, body: unknown = {}): Promise<T> {
    return this.run(this.http.post<T>(this.base + path, body));
  }

  put<T = void>(path: string, body: unknown): Promise<T> {
    return this.run(this.http.put<T>(this.base + path, body));
  }

  delete<T = void>(path: string): Promise<T> {
    return this.run(this.http.delete<T>(this.base + path));
  }

  private async run<T>(request: import('rxjs').Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw ApiError.from(error);
    }
  }
}
