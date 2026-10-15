import { compareVersions } from 'shared';
import { isStaff } from './app.routes';
import { ADMIN_TEXTS } from './texts';

describe('admin', () => {
  it('has every text in both languages', () => {
    expect(Object.keys(ADMIN_TEXTS.ar).sort()).toEqual(Object.keys(ADMIN_TEXTS.en).sort());
  });

  it('names every security event type', () => {
    const types = ['Registered', 'EmailConfirmed', 'SignedIn', 'SignInFailed', 'LockedOut', 'SignedOut', 'SessionsRevoked', 'PasswordChanged',
      'PasswordResetRequested', 'PasswordReset', 'MfaEnabled', 'MfaDisabled', 'RecoveryCodesRegenerated', 'RecoveryCodeUsed'];
    expect(types.filter((t) => !ADMIN_TEXTS.en[`security.type.${t}`])).toEqual([]);
  });

  it('lets only staff roles in', () => {
    const me = { id: '1', email: 'a@b.c', fullName: 'A', emailConfirmed: true, mfaEnabled: true };
    expect(isStaff({ ...me, roles: ['Finance'] })).toBe(true);
    expect(isStaff({ ...me, roles: ['Customer'] })).toBe(false);
    expect(isStaff(null)).toBe(false);
  });

  it('compares versions by number, not text', () => {
    expect(compareVersions('2.10.0', '2.9.9')).toBeGreaterThan(0);
    expect(compareVersions('1.0.0', '1.0.0')).toBe(0);
    expect(compareVersions('1.2.3', '1.3.0')).toBeLessThan(0);
  });
});
