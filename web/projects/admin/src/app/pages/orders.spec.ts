import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Api, ApiError, DICTIONARY, I18n, Order } from 'shared';
import { ADMIN_TEXTS } from '../texts';
import { OrdersPage } from './orders';

type Page = {
  refundReference: string; refundAmount: number | null; refundReason: string;
  startRefund(order: Order): void; refund(order: Order): Promise<void>; message(): string; failed(): boolean; refunding(): string | null;
};

const base: Order = {
  id: 'o1', number: 'ORD-1', productName: 'Monitor Agent', planName: 'Pro', total: 100, currency: 'USD', status: 'Fulfilled',
  placedAt: '2026-10-01T00:00:00Z', paidAt: '2026-10-01T00:00:00Z', licenseNumber: 'LIC-1', payments: [], refundedAmount: 40,
};

async function create(orders: Order[]) {
  const api = { get: vi.fn(), post: vi.fn() };
  api.get.mockResolvedValue({ items: orders, page: 1, pageSize: 25, totalCount: orders.length });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: Api, useValue: api }, { provide: DICTIONARY, useValue: ADMIN_TEXTS }],
  });
  TestBed.inject(I18n).use('en');
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  const fixture = TestBed.createComponent(OrdersPage);
  fixture.detectChanges();
  await settle(fixture);
  return { api, fixture, page: fixture.componentInstance as unknown as Page, host: fixture.nativeElement as HTMLElement };
}

async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  for (let i = 0; i < 10; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}

const buttons = (host: HTMLElement, text: string) => [...host.querySelectorAll('button')].filter((b) => b.textContent?.trim() === text);

describe('OrdersPage refunds', () => {
  it('offers a refund only on paid orders with money left to return', async () => {
    const { host } = await create([
      base,
      { ...base, id: 'o2', status: 'PendingPayment', refundedAmount: 0 },
      { ...base, id: 'o3', status: 'Paid', refundedAmount: 100 },
      { ...base, id: 'o4', status: 'Cancelled', refundedAmount: 0 },
    ]);
    expect(buttons(host, 'Record refund')).toHaveLength(1);
    expect(host.querySelector('[data-testid="refunded"]')?.textContent).toContain('refunded');
  });

  it('records a refund for what is left and reloads the list', async () => {
    const { api, fixture, page, host } = await create([base]);
    buttons(host, 'Record refund')[0].click();
    fixture.detectChanges();
    expect(page.refundAmount).toBe(60);
    page.refundReference = ' BANK-77 ';
    page.refundReason = '';
    api.post.mockResolvedValue({ ...base, refundedAmount: 100 });
    await page.refund(base);
    expect(api.post).toHaveBeenCalledWith('/api/v1/admin/orders/o1/refund', { reference: 'BANK-77', amount: 60, reason: null });
    expect(page.refunding()).toBeNull();
    expect(page.message()).toBe('Refund recorded for ORD-1.');
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('explains a refund larger than what is left', async () => {
    const { api, page } = await create([base]);
    page.startRefund(base);
    page.refundReference = 'BANK-78';
    page.refundAmount = 500;
    api.post.mockRejectedValue(new ApiError(400, 'REFUND_TOO_LARGE', 'x'));
    await page.refund(base);
    expect(page.failed()).toBe(true);
    expect(page.message()).toBe(ADMIN_TEXTS.en['error.REFUND_TOO_LARGE']);
    expect(page.refunding()).toBe('o1');
  });

  it('records nothing when the confirmation is declined', async () => {
    const { api, page } = await create([base]);
    vi.mocked(window.confirm).mockReturnValue(false);
    page.startRefund(base);
    page.refundReference = 'BANK-79';
    await page.refund(base);
    expect(api.post).not.toHaveBeenCalled();
  });
});
