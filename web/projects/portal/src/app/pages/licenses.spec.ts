import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Api, ApiError, DICTIONARY, I18n } from 'shared';
import { PORTAL_TEXTS } from '../texts';
import { LicensePage, LicensesPage } from './licenses';

type FakeApi = { get: ReturnType<typeof vi.fn>; post: ReturnType<typeof vi.fn> };

const plans = [
  { id: 'p-pro', name: 'Pro' },
  { id: 'p-basic', name: 'Basic' },
];
const subscription = {
  id: 's1', planId: 'p-pro', planCode: 'PRO', planName: 'Pro', status: 'Active',
  startDate: '2026-01-01', endDate: '2027-01-01', scheduledPlanId: 'p-basic',
};
const details = {
  license: {
    id: 'l1', licenseNumber: 'LIC-0001', productCode: 'MA', planCode: 'PRO', status: 'Active', productKeyPrefix: 'MA-AB',
    issuedAt: '2026-01-01', expiresAt: '2027-01-01', activeDevices: 1, maxDevices: 5,
  },
  devices: [],
};

function setUp(role: string): FakeApi {
  const api: FakeApi = { get: vi.fn(), post: vi.fn().mockResolvedValue(undefined) };
  api.get.mockImplementation((path: string) => {
    if (path === '/api/v1/company') return Promise.resolve({ myRole: role });
    if (path === '/api/v1/licenses/subscription') return Promise.resolve([subscription]);
    if (path === '/api/v1/catalog/plans') return Promise.resolve(plans);
    if (path === '/api/v1/licenses') return Promise.resolve([]);
    if (path === '/api/v1/licenses/l1') return Promise.resolve(details);
    if (path === '/api/v1/payments/providers') return Promise.resolve(['test']);
    return Promise.resolve(undefined);
  });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: Api, useValue: api }, { provide: DICTIONARY, useValue: PORTAL_TEXTS }],
  });
  TestBed.inject(I18n).use('en');
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  return api;
}

async function render<T>(type: Type<T>, inputs: Record<string, unknown> = {}) {
  const fixture = TestBed.createComponent(type);
  for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
  fixture.detectChanges();
  await settle(fixture);
  return { fixture, host: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  for (let i = 0; i < 10; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

function button(host: HTMLElement, text: string): HTMLButtonElement | undefined {
  return [...host.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);
}

describe('LicensesPage scheduled plan change', () => {
  it('shows the scheduled plan and lets a billing member keep the current one', async () => {
    const api = setUp('Billing');
    const { fixture, host } = await render(LicensesPage);
    expect(host.querySelector('[data-testid="scheduled-change"]')?.textContent).toContain('Changes to Basic at the next renewal');

    button(host, 'Keep Pro')!.click();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/licenses/subscription/s1/change-plan', { planId: 'p-pro' });
    expect(host.textContent).toContain('The scheduled change was cancelled.');
  });

  it('shows the schedule but no action to a technical member', async () => {
    setUp('Technical');
    const { host } = await render(LicensesPage);
    expect(host.querySelector('[data-testid="scheduled-change"]')).not.toBeNull();
    expect(button(host, 'Keep Pro')).toBeUndefined();
  });
});

describe('LicensePage renewal', () => {
  it('places a renewal order for this license and starts payment', async () => {
    const api = setUp('Owner');
    api.post.mockImplementation((path: string) => {
      if (path === '/api/v1/orders/renewals') return Promise.resolve({ id: 'o9' });
      // Stops before the redirect, which the test browser cannot follow.
      return Promise.reject(new ApiError(503, 'PAYMENT_UNAVAILABLE', 'x'));
    });
    const { fixture, host } = await render(LicensePage, { id: 'l1' });

    button(host, 'Renew')!.click();
    await settle(fixture);
    expect(api.post).toHaveBeenNthCalledWith(1, '/api/v1/orders/renewals', { licenseId: 'l1' });
    expect(api.post).toHaveBeenNthCalledWith(2, '/api/v1/orders/o9/pay', { provider: 'test' });
  });

  it('explains a license that cannot be renewed', async () => {
    const api = setUp('Billing');
    api.post.mockRejectedValue(new ApiError(409, 'LICENSE_NOT_RENEWABLE', 'x'));
    const { fixture, host } = await render(LicensePage, { id: 'l1' });

    button(host, 'Renew')!.click();
    await settle(fixture);
    expect(host.querySelector('[role="alert"]')?.textContent).toContain(PORTAL_TEXTS.en['error.LICENSE_NOT_RENEWABLE']);
  });

  it('hides renewal from members who cannot buy', async () => {
    setUp('Viewer');
    const { host } = await render(LicensePage, { id: 'l1' });
    expect(host.textContent).toContain('LIC-0001');
    expect(button(host, 'Renew')).toBeUndefined();
  });
});

describe('LicensesPage free trial (B5)', () => {
  it('asks for a trial code and shows it once', async () => {
    const api = setUp('Owner');
    api.post.mockImplementation((path: string) =>
      path === '/api/v1/licenses/trial'
        ? Promise.resolve({ trialCode: 'ltg_TESTONLYCODE', expiresAt: '2026-11-09T12:00:00Z', productCode: '000001', trialDays: 14 })
        : Promise.resolve(undefined),
    );
    const { fixture, host } = await render(LicensesPage);

    button(host, 'Get a trial code')!.click();
    await settle(fixture);

    expect(api.post).toHaveBeenCalledWith('/api/v1/licenses/trial');
    expect(host.querySelector('[data-testid="trial-code"]')?.textContent).toContain('ltg_TESTONLYCODE');
    expect(host.querySelector('[data-testid="trial-code"]')?.textContent).toContain('14-day trial');
  });

  it('explains when the account already used its trial', async () => {
    const api = setUp('Technical');
    api.post.mockRejectedValue(new ApiError(409, 'TRIAL_ALREADY_USED', 'used'));
    const { fixture, host } = await render(LicensesPage);

    button(host, 'Get a trial code')!.click();
    await settle(fixture);

    expect(host.querySelector('[data-testid="trial-code"]')?.getAttribute('role')).toBe('alert');
    expect(host.textContent).toContain('You have already used your free trial');
  });
});

describe('LicensesPage paid upgrade (INT-014)', () => {
  it('shows the prorated quote for a dearer plan and pays for the upgrade order', async () => {
    const api = setUp('Owner');
    const quote = {
      subscriptionId: 's1', planId: 'p-max', planName: 'Max', price: 240, currency: 'EGP', remainingDays: 73, termEndsAt: '2027-01-01T00:00:00Z',
    };
    api.get.mockImplementation((path: string) => {
      if (path === '/api/v1/company') return Promise.resolve({ myRole: 'Owner' });
      if (path === '/api/v1/licenses/subscription') return Promise.resolve([{ ...subscription, scheduledPlanId: null }]);
      if (path === '/api/v1/catalog/plans') return Promise.resolve([...plans, { id: 'p-max', name: 'Max' }]);
      if (path === '/api/v1/licenses') return Promise.resolve([]);
      if (path === '/api/v1/payments/providers') return Promise.resolve([]);
      if (path.startsWith('/api/v1/orders/upgrades/quote?')) return Promise.resolve(quote);
      return Promise.resolve(undefined);
    });
    api.post.mockImplementation((path: string) => {
      if (path.endsWith('/change-plan')) return Promise.reject(new ApiError(409, 'PLAN_UPGRADE_REQUIRES_PAYMENT', 'needs payment'));
      if (path === '/api/v1/orders/upgrades') return Promise.resolve({ id: 'o9' });
      return Promise.resolve(undefined);
    });
    const { fixture, host } = await render(LicensesPage);
    const select = host.querySelector('select') as HTMLSelectElement;
    select.value = 'p-max';
    select.dispatchEvent(new Event('change'));

    button(host, 'Change plan')!.click();
    await settle(fixture);

    expect(api.get).toHaveBeenCalledWith('/api/v1/orders/upgrades/quote?subscriptionId=s1&planId=p-max');
    const text = host.querySelector('[data-testid="upgrade-quote"]')?.textContent ?? '';
    expect(text).toContain('Max');
    expect(text).toContain('73 days');
    expect(host.querySelector('[role="alert"]')).toBeNull();

    button(host, 'Pay for the upgrade')!.click();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/orders/upgrades', { subscriptionId: 's1', planId: 'p-max' });
    expect(host.querySelector('[data-testid="upgrade-quote"]')).toBeNull();
  });
});
