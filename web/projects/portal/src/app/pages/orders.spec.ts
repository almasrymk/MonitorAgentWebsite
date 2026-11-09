import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Api, ApiError, DICTIONARY, I18n, Order } from 'shared';
import { PORTAL_TEXTS } from '../texts';
import { OrderPage, pay } from './orders';

type FakeApi = { get: ReturnType<typeof vi.fn>; post: ReturnType<typeof vi.fn> };

const order: Order = {
  id: 'o1', number: 'MA-261105-ABC234', productName: 'Monitor Agent', planName: 'Pro', total: 1200, currency: 'EGP',
  status: 'PendingPayment', placedAt: '2026-11-05T09:00:00Z', paidAt: null, licenseNumber: null, payments: [],
};
const instructions = {
  reference: order.number, amount: 1200, currency: 'EGP', accountHolder: 'Example Trading (test)', bankName: 'Example Bank',
  accountNumber: null, iban: 'XX00TEST0000000000000000', swift: null, instructions: null,
};

function setUp(options: { providers?: string[]; bank?: boolean; current?: Order } = {}): FakeApi {
  const api: FakeApi = { get: vi.fn(), post: vi.fn() };
  api.get.mockImplementation((path: string) => {
    if (path === '/api/v1/orders/o1') return Promise.resolve(options.current ?? order);
    if (path === '/api/v1/payments/providers') return Promise.resolve(options.providers ?? []);
    if (path === '/api/v1/orders/o1/bank-transfer')
      return options.bank === false
        ? Promise.reject(new ApiError(503, 'BANK_TRANSFER_UNAVAILABLE', 'x'))
        : Promise.resolve(instructions);
    return Promise.resolve(undefined);
  });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: Api, useValue: api }, { provide: DICTIONARY, useValue: PORTAL_TEXTS }],
  });
  TestBed.inject(I18n).use('en');
  return api;
}

async function render() {
  const fixture = TestBed.createComponent(OrderPage);
  fixture.componentRef.setInput('id', 'o1');
  fixture.detectChanges();
  await settle(fixture);
  return { fixture, host: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  for (let i = 0; i < 20; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const button = (host: HTMLElement, text: string) =>
  [...host.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);

describe('OrderPage bank transfer (E5)', () => {
  it('shows the account and the order number as the reference to write on the transfer', async () => {
    setUp();
    const { host } = await render();
    expect(host.querySelector('[data-testid="transfer-reference"]')?.textContent).toContain(order.number);
    expect(host.textContent).toContain('XX00TEST0000000000000000');
    // No online provider configured: there is nothing to "pay now".
    expect(button(host, 'Pay now')).toBeUndefined();
  });

  it('reports the transfer and shows that it is being checked', async () => {
    const api = setUp();
    const { fixture, host } = await render();
    const page = fixture.componentInstance as unknown as { transferReference: string; transferNote: string; submitTransfer(o: Order): Promise<void> };
    page.transferReference = ' TRF-889 ';
    page.transferNote = '';
    api.post.mockResolvedValue({ ...order, status: 'PendingVerification' });
    api.get.mockImplementation((path: string) =>
      Promise.resolve(path === '/api/v1/orders/o1' ? { ...order, status: 'PendingVerification' } : undefined),
    );
    await page.submitTransfer(order);
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/orders/o1/bank-transfer', { reference: 'TRF-889', note: null });
    expect(host.querySelector('[data-testid="verifying"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="bank-transfer"]')).toBeNull();
  });

  it('hides bank transfer when it is not configured', async () => {
    setUp({ bank: false, providers: ['sandbox'] });
    const { host } = await render();
    expect(host.querySelector('[data-testid="bank-transfer"]')).toBeNull();
    expect(button(host, 'Pay now')).toBeDefined();
  });

  it('checkout and renewal go to the order page when only bank transfer is offered', async () => {
    const api = setUp();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await pay(TestBed.inject(Api), router, 'o1');
    expect(navigate).toHaveBeenCalledWith(['/orders', 'o1']);
    expect(api.post).not.toHaveBeenCalled();
  });
});
