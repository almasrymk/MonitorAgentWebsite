import { TestBed } from '@angular/core/testing';
import { apiError, customer, setUp, settle } from '../testing';
import { safeReturnUrl, SignInPage } from './sign-in';

type Page = {
  email: string; password: string; remember: boolean; code: string;
  step(): string; error(): string; done(): string; unconfirmed(): boolean; useRecovery: { set(v: boolean): void };
  signIn(): Promise<void>; verify(): Promise<void>; resend(): Promise<void>; switchLanguage(): void;
};

describe('safeReturnUrl', () => {
  it('follows only paths inside the app', () => {
    expect(safeReturnUrl('/licenses?x=1')).toBe('/licenses?x=1');
    expect(safeReturnUrl(undefined)).toBe('/');
    expect(safeReturnUrl('')).toBe('/');
    expect(safeReturnUrl('https://evil.test')).toBe('/');
    expect(safeReturnUrl('//evil.test')).toBe('/');
  });
});

describe('SignInPage', () => {
  function create(returnUrl?: string) {
    const env = setUp();
    env.api.get.mockImplementation((path: string) => Promise.resolve(path.endsWith('/me') ? customer : undefined));
    const fixture = TestBed.createComponent(SignInPage);
    if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
    fixture.detectChanges();
    return { ...env, fixture, page: fixture.componentInstance as unknown as Page };
  }

  it('signs in and goes back where the person was', async () => {
    const { api, router, page } = create('/orders');
    api.post.mockResolvedValueOnce({ requiresMfa: false });
    page.email = 'mona@example.test';
    page.password = 'secret password';
    page.remember = true;
    await page.signIn();
    expect(api.post).toHaveBeenCalledWith('/api/v1/auth/login', { email: 'mona@example.test', password: 'secret password', rememberMe: true });
    expect(page.password).toBe('');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/orders');
  });

  it('asks for the code when two-step verification is on, or a recovery code', async () => {
    const { api, router, page, fixture } = create();
    api.post.mockResolvedValueOnce({ requiresMfa: true });
    await page.signIn();
    await settle(fixture);
    expect(page.step()).toBe('mfa');
    expect(fixture.nativeElement.querySelector('#code').getAttribute('autocomplete')).toBe('one-time-code');

    page.code = '123 456';
    await page.verify();
    expect(api.post).toHaveBeenLastCalledWith('/api/v1/auth/login/mfa', { code: '123456', rememberMe: false });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');

    page.useRecovery.set(true);
    page.code = ' abcd-efgh ';
    await page.verify();
    expect(api.post).toHaveBeenLastCalledWith('/api/v1/auth/login/recovery-code', { code: 'abcd-efgh' });
  });

  it('goes back to the password when the code step expired', async () => {
    const { api, page } = create();
    api.post.mockResolvedValueOnce({ requiresMfa: true });
    await page.signIn();
    api.post.mockRejectedValueOnce(apiError(400, 'MFA_SESSION_EXPIRED'));
    await page.verify();
    expect(page.step()).toBe('password');
    expect(page.error()).not.toBe('');
  });

  it('offers to resend the confirmation email to an unconfirmed account', async () => {
    const { api, page, fixture } = create();
    api.post.mockRejectedValueOnce(apiError(403, 'EMAIL_NOT_CONFIRMED'));
    page.email = 'new@example.test';
    await page.signIn();
    await settle(fixture);
    expect(page.unconfirmed()).toBe(true);
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();

    await page.resend();
    expect(api.post).toHaveBeenLastCalledWith('/api/v1/auth/resend-confirmation', { email: 'new@example.test' });
    expect(page.error()).toBe('');
    expect(page.done()).not.toBe('');
  });

  it('shows the error for a wrong password and stays on the form', async () => {
    const { api, router, page } = create();
    api.post.mockRejectedValueOnce(apiError(401, 'INVALID_CREDENTIALS'));
    await page.signIn();
    expect(page.step()).toBe('password');
    expect(page.error()).not.toBe('');
    expect(page.unconfirmed()).toBe(false);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('switches language', () => {
    const { page, i18n } = create();
    page.switchLanguage();
    expect(i18n.lang()).toBe('ar');
    localStorage.clear();
  });
});
