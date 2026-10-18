import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Api, API_BASE_URL, ApiError } from './api';

describe('ApiError', () => {
  it('keeps the stable error code from ProblemDetails', () => {
    const error = ApiError.from(new HttpErrorResponse({ status: 409, error: { code: 'ORDER_NOT_PAYABLE', title: 'Conflict' } }));
    expect(error.status).toBe(409);
    expect(error.code).toBe('ORDER_NOT_PAYABLE');
  });

  it('reports an unreachable server as NETWORK', () => {
    expect(ApiError.from(new HttpErrorResponse({ status: 0 })).code).toBe('NETWORK');
  });

  it('names an error without a code by its status, and anything else UNKNOWN', () => {
    const error = ApiError.from(new HttpErrorResponse({ status: 502, error: null }));
    expect(error.code).toBe('HTTP_502');
    expect(ApiError.from(new Error('x')).code).toBe('UNKNOWN');
    const same = new ApiError(400, 'X', 'x');
    expect(ApiError.from(same)).toBe(same);
  });
});

describe('Api', () => {
  function setUp(base = '') {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: base }],
    });
    return { api: TestBed.inject(Api), http: TestBed.inject(HttpTestingController) };
  }

  it('sends each verb to the API address and returns the body', async () => {
    const { api, http } = setUp('https://api.test');
    const get = api.get<{ ok: boolean }>('/a');
    http.expectOne({ method: 'GET', url: 'https://api.test/a' }).flush({ ok: true });
    expect(await get).toEqual({ ok: true });

    const post = api.post('/b', { x: 1 });
    const request = http.expectOne({ method: 'POST', url: 'https://api.test/b' });
    expect(request.request.body).toEqual({ x: 1 });
    request.flush(null);
    await post;

    const put = api.put('/c', { y: 2 });
    http.expectOne({ method: 'PUT', url: 'https://api.test/c' }).flush(null);
    await put;

    const del = api.delete('/d');
    http.expectOne({ method: 'DELETE', url: 'https://api.test/d' }).flush(null);
    await del;
    http.verify();
  });

  it('turns failures into ApiError with the code', async () => {
    const { api, http } = setUp();
    const call = api.post('/x');
    http.expectOne('/x').flush({ code: 'LAST_OWNER', title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
    await expect(call).rejects.toMatchObject({ status: 409, code: 'LAST_OWNER' });
  });
});
