import { HttpErrorResponse } from '@angular/common/http';
import { ApiError } from './api';

describe('ApiError', () => {
  it('keeps the stable error code from ProblemDetails', () => {
    const error = ApiError.from(new HttpErrorResponse({ status: 409, error: { code: 'ORDER_NOT_PAYABLE', title: 'Conflict' } }));
    expect(error.status).toBe(409);
    expect(error.code).toBe('ORDER_NOT_PAYABLE');
  });

  it('reports an unreachable server as NETWORK', () => {
    expect(ApiError.from(new HttpErrorResponse({ status: 0 })).code).toBe('NETWORK');
  });
});
