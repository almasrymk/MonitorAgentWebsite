import { TestBed } from '@angular/core/testing';
import { ApiError } from './api';
import { I18n } from './i18n';
import { errorText, saveLanguage, Session, startLanguage } from './session';
import { apiError, customer, setUp } from './testing';

describe('Session', () => {
  it('asks who is signed in once, then remembers', async () => {
    const { api } = setUp();
    api.get.mockResolvedValue(customer);
    const session = TestBed.inject(Session);
    expect(await session.load()).toEqual(customer);
    expect(await session.load()).toEqual(customer);
    expect(api.get).toHaveBeenCalledTimes(1);
    await session.load(true);
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('treats 401 and 403 as signed out, and passes other failures on', async () => {
    const { api } = setUp();
    const session = TestBed.inject(Session);
    api.get.mockRejectedValueOnce(apiError(401, 'UNAUTHENTICATED'));
    expect(await session.load()).toBeNull();
    api.get.mockRejectedValueOnce(apiError(403, 'FORBIDDEN'));
    expect(await session.load(true)).toBeNull();
    api.get.mockRejectedValueOnce(apiError(500, 'HTTP_500'));
    await expect(session.load(true)).rejects.toBeInstanceOf(ApiError);
  });

  it('signing in renews the CSRF token and reloads the user', async () => {
    const { api } = setUp();
    api.get.mockImplementation((path: string) => Promise.resolve(path.endsWith('/me') ? customer : undefined));
    const session = TestBed.inject(Session);
    await session.signedIn();
    expect(api.get.mock.calls.map((c) => c[0])).toEqual(['/api/v1/auth/csrf', '/api/v1/account/me']);
    expect(session.me()).toEqual(customer);
  });

  it('signing out forgets the user even when the server call fails', async () => {
    const { api } = setUp();
    api.get.mockResolvedValue(customer);
    const session = TestBed.inject(Session);
    await session.load();
    api.post.mockRejectedValueOnce(apiError(0, 'NETWORK'));
    api.get.mockRejectedValue(apiError(0, 'NETWORK'));
    await expect(session.signOut()).rejects.toBeInstanceOf(ApiError);
    expect(session.me()).toBeNull();
    expect(await session.load()).toBeNull();
  });
});

describe('language choice', () => {
  afterEach(() => localStorage.clear());

  it('is Arabic until one is saved, then remembered', () => {
    setUp();
    const i18n = TestBed.inject(I18n);
    localStorage.clear();
    startLanguage(i18n);
    expect(i18n.lang()).toBe('ar');
    saveLanguage(i18n, 'en');
    expect(localStorage.getItem('ma.lang')).toBe('en');
    i18n.use('ar');
    startLanguage(i18n);
    expect(i18n.lang()).toBe('en');
  });

  it('ignores a value it does not know, and blocked storage', () => {
    setUp();
    const i18n = TestBed.inject(I18n);
    localStorage.setItem('ma.lang', 'fr');
    startLanguage(i18n);
    expect(i18n.lang()).toBe('ar');
    const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    startLanguage(i18n);
    saveLanguage(i18n, 'en');
    expect(i18n.lang()).toBe('en');
    get.mockRestore();
    set.mockRestore();
  });
});

describe('errorText', () => {
  it('uses the text for the error code, or a general one', () => {
    const { i18n } = setUp();
    expect(errorText(i18n, apiError(400, 'WRONG_PASSWORD'))).toBe('Your current password is not right.');
    expect(errorText(i18n, apiError(400, 'SOMETHING_NEW'))).toBe('Something went wrong.');
    expect(errorText(i18n, new Error('boom'))).toBe('Something went wrong.');
  });
});
