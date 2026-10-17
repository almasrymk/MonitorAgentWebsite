import { TestBed } from '@angular/core/testing';
import { Session } from './session';
import { AppShell } from './shell';
import { customer, setUp, settle } from './testing';

describe('AppShell', () => {
  it('shows the menu and who is signed in, switches language and signs out', async () => {
    const { api, router, i18n } = setUp();
    api.get.mockResolvedValue(customer);
    await TestBed.inject(Session).load();
    const fixture = TestBed.createComponent(AppShell);
    fixture.componentRef.setInput('nav', [
      { path: '/', label: 'nav.overview' },
      { path: '/account', label: 'nav.account' },
    ]);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    expect(host.querySelectorAll('nav a').length).toBe(2);
    expect(host.textContent).toContain('mona@example.test');

    (host.querySelector('.topbar .link-button') as HTMLElement).click();
    expect(i18n.lang()).toBe('ar');
    localStorage.clear();

    (host.querySelector('.topbar .btn') as HTMLElement).click();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/auth/logout');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/sign-in');
  });
});
