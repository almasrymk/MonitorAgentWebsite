import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { REQUEST } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from 'shared';
import { forwardVisitor } from './forward-visitor';

describe('forwardVisitor', () => {
  function setUp(incoming: Request | null) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([forwardVisitor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api:8080' },
        { provide: REQUEST, useValue: incoming },
      ],
    });
    return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
  }

  it('passes the visitor address on to the API while rendering on the server', () => {
    const { http, backend } = setUp(new Request('https://site.test/', { headers: { 'X-Forwarded-For': '203.0.113.7' } }));
    http.get('http://api:8080/api/v1/catalog/plans').subscribe();
    http.get('https://elsewhere.test/x').subscribe();
    expect(backend.expectOne('http://api:8080/api/v1/catalog/plans').request.headers.get('X-Forwarded-For')).toBe('203.0.113.7');
    expect(backend.expectOne('https://elsewhere.test/x').request.headers.has('X-Forwarded-For')).toBe(false);
  });

  it('adds nothing in the browser', () => {
    const { http, backend } = setUp(null);
    http.get('http://api:8080/api/v1/catalog/plans').subscribe();
    expect(backend.expectOne('http://api:8080/api/v1/catalog/plans').request.headers.has('X-Forwarded-For')).toBe(false);
  });
});
