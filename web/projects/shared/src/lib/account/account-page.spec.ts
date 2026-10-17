import { TestBed } from '@angular/core/testing';
import { Me } from '../models';
import { Session } from '../session';
import { apiError, customer, setUp, settle } from '../testing';
import { AccountPage } from './account-page';

type Page = {
  current: string; next: string; code: string; disablePassword: string; deletePassword: string;
  message(): string; failed(): boolean; setupKey(): string; codes(): string[]; deleted(): boolean; isCustomer(): boolean;
  changePassword(): Promise<void>; startMfa(): Promise<void>; enableMfa(): Promise<void>; newCodes(): Promise<void>;
  disableMfa(): Promise<void>; revokeAll(): Promise<void>; deleteAccount(): Promise<void>; signInAgain(): Promise<void>;
};

describe('AccountPage', () => {
  async function create(me: Me = customer) {
    const env = setUp();
    env.api.get.mockImplementation((path: string) => {
      if (path.endsWith('/me')) return Promise.resolve(me);
      if (path.endsWith('/mfa/setup')) return Promise.resolve({ sharedKey: 'ABCD EFGH', authenticatorUri: 'otpauth://x' });
      return Promise.resolve(undefined);
    });
    const fixture = TestBed.createComponent(AccountPage);
    fixture.detectChanges();
    await settle(fixture);
    return { ...env, fixture, page: fixture.componentInstance as unknown as Page, host: fixture.nativeElement as HTMLElement };
  }

  it('shows the profile and, for customers only, the data section', async () => {
    const { page, host } = await create();
    expect(host.textContent).toContain('mona@example.test');
    expect(page.isCustomer()).toBe(true);
    expect(host.querySelector('a[href="/api/v1/account/export"]')).not.toBeNull();

    const staff = await createStaff();
    expect(staff.page.isCustomer()).toBe(false);
    expect(staff.host.querySelector('a[href="/api/v1/account/export"]')).toBeNull();
  });

  async function createStaff() {
    TestBed.resetTestingModule();
    return create({ ...customer, roles: ['Customer', 'Support'] });
  }

  it('changes the password and renews the session', async () => {
    const { api, page } = await create();
    page.current = 'old password';
    page.next = 'a much longer new password';
    await page.changePassword();
    expect(api.post).toHaveBeenCalledWith('/api/v1/account/change-password', { currentPassword: 'old password', newPassword: 'a much longer new password' });
    expect(page.current).toBe('');
    expect(page.failed()).toBe(false);
    expect(page.message()).not.toBe('');
  });

  it('shows why a change failed', async () => {
    const { api, page } = await create();
    api.post.mockRejectedValueOnce(apiError(400, 'WRONG_PASSWORD'));
    await page.changePassword();
    expect(page.failed()).toBe(true);
    expect(page.message()).toBe('Your current password is not right.');
  });

  it('turns on two-step verification and shows the recovery codes once', async () => {
    const { api, page, fixture } = await create();
    await page.startMfa();
    expect(page.setupKey()).toBe('ABCD EFGH');
    expect(api.get).toHaveBeenCalledWith('/api/v1/auth/csrf');

    api.post.mockResolvedValueOnce({ recoveryCodes: ['a1', 'b2'] });
    page.code = '123 456';
    await page.enableMfa();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/account/mfa/enable', { code: '123456' });
    expect(page.setupKey()).toBe('');
    expect(page.codes()).toEqual(['a1', 'b2']);
    expect(fixture.nativeElement.querySelector('[data-testid=recovery-codes]').textContent).toContain('b2');

    api.post.mockResolvedValueOnce({ recoveryCodes: ['c3'] });
    await page.newCodes();
    expect(page.codes()).toEqual(['c3']);
  });

  it('turns two-step verification off with the password', async () => {
    const { api, page } = await create({ ...customer, mfaEnabled: true });
    page.disablePassword = 'pw';
    await page.disableMfa();
    expect(api.post).toHaveBeenCalledWith('/api/v1/account/mfa/disable', { password: 'pw' });
    expect(page.disablePassword).toBe('');
    expect(page.codes()).toEqual([]);
  });

  it('signs out everywhere and goes to sign-in', async () => {
    const { api, router, page } = await create();
    await page.revokeAll();
    expect(api.post).toHaveBeenCalledWith('/api/v1/account/sessions/revoke');
    expect(api.post).toHaveBeenCalledWith('/api/v1/auth/logout');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/sign-in');

    await page.signInAgain();
    expect(router.navigateByUrl).toHaveBeenCalledTimes(2);
  });

  it('deletes the account even if the local sign-out fails', async () => {
    const { api, page, fixture } = await create();
    api.post.mockImplementation((path: string) => (path.endsWith('/logout') ? Promise.reject(apiError(401, 'UNAUTHENTICATED')) : Promise.resolve()));
    page.deletePassword = 'pw';
    await page.deleteAccount();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/account/delete', { password: 'pw' });
    expect(page.deleted()).toBe(true);
    expect(TestBed.inject(Session).me()).toBeNull();
    expect(fixture.nativeElement.querySelector('[role=status]')).not.toBeNull();
  });

  it('explains when the last owner tries to leave', async () => {
    const { api, page } = await create();
    api.post.mockRejectedValueOnce(apiError(409, 'LAST_OWNER'));
    await page.deleteAccount();
    expect(page.deleted()).toBe(false);
    expect(page.failed()).toBe(true);
  });
});
