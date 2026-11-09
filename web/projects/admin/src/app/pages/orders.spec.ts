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

describe('OrdersPage bank transfers (E5)', () => {
  const waiting: Order = {
    ...base, id: 'o5', number: 'ORD-5', status: 'PendingVerification', paidAt: null, licenseNumber: null, refundedAmount: 0,
    payments: [{ id: 'p1', provider: 'bank-transfer', amount: 100, currency: 'USD', status: 'Pending', createdAt: base.placedAt,
      failureReason: null, reference: 'TRF-123', note: 'Sent Monday' }],
  };
  type TransferPage = Page & { rejectReason: string; startReject(o: Order): void; approve(o: Order): Promise<void>; reject(o: Order): Promise<void> };

  it('shows the reported reference with approve and reject, and nothing for other orders', async () => {
    const { host } = await create([waiting, base]);
    const transfer = host.querySelector('[data-testid="transfer"]');
    expect(transfer?.textContent).toContain('TRF-123');
    expect(transfer?.textContent).toContain('Sent Monday');
    expect(buttons(host, 'Approve transfer')).toHaveLength(1);
    expect(buttons(host, 'Reject')).toHaveLength(1);
  });

  it('approves only after confirmation and reloads', async () => {
    const { api, page } = await create([waiting]);
    api.post.mockResolvedValue({ ...waiting, status: 'Fulfilled' });
    await (page as TransferPage).approve(waiting);
    expect(api.post).toHaveBeenCalledWith('/api/v1/admin/orders/o5/bank-transfer/approve', {});
    expect(page.message()).toBe('Transfer approved for ORD-5. The invoice and license are issued.');
    expect(api.get).toHaveBeenCalledTimes(2);

    vi.mocked(window.confirm).mockReturnValue(false);
    api.post.mockClear();
    await (page as TransferPage).approve(waiting);
    expect(api.post).not.toHaveBeenCalled();
  });

  it('rejects with the reason the customer will see', async () => {
    const { api, page } = await create([waiting]);
    const transferPage = page as TransferPage;
    transferPage.startReject(waiting);
    transferPage.rejectReason = ' Not on the statement ';
    api.post.mockResolvedValue({ ...waiting, status: 'PendingPayment' });
    await transferPage.reject(waiting);
    expect(api.post).toHaveBeenCalledWith('/api/v1/admin/orders/o5/bank-transfer/reject', { reason: 'Not on the statement' });
    expect(page.message()).toBe('Transfer rejected for ORD-5. The order is waiting for payment again.');
  });

  it('explains an approval that lost a race', async () => {
    const { api, page } = await create([waiting]);
    api.post.mockRejectedValue(new ApiError(409, 'NO_PAYMENT_TO_VERIFY', 'x'));
    await (page as TransferPage).approve(waiting);
    expect(page.failed()).toBe(true);
    expect(page.message()).toBe(ADMIN_TEXTS.en['error.NO_PAYMENT_TO_VERIFY']);
  });
});

describe('OrdersPage disputes (B3)', () => {
  it('records a dispute with a reason and hides the refund form while it is open', async () => {
    const { api, fixture, host } = await create([{ ...base, refundedAmount: 0 }]);
    vi.spyOn(window, 'prompt').mockReturnValue(' Bank recall 4471 ');
    api.post.mockResolvedValue({ ...base, dispute: 'Open' });

    buttons(host, 'Record dispute')[0].click();
    await settle(fixture);

    expect(api.post).toHaveBeenCalledWith('/api/v1/admin/orders/o1/dispute', { reason: 'Bank recall 4471' });
  });

  it('closes an open dispute as lost only with the chargeback reference', async () => {
    const { api, fixture, host } = await create([{ ...base, dispute: 'Open' }]);
    expect(buttons(host, 'Record refund')).toHaveLength(0);
    expect(host.querySelector('[data-testid="dispute"]')?.textContent).toContain('Disputed');
    const prompt = vi.spyOn(window, 'prompt').mockReturnValue('');
    buttons(host, 'Dispute lost')[0].click();
    await settle(fixture);
    expect(api.post).not.toHaveBeenCalled();

    prompt.mockReturnValue('CB-42');
    api.post.mockResolvedValue({ ...base, dispute: 'Lost', status: 'Refunded' });
    buttons(host, 'Dispute lost')[0].click();
    await settle(fixture);
    expect(api.post).toHaveBeenCalledWith('/api/v1/admin/orders/o1/dispute/resolve', { won: false, reference: 'CB-42' });
    expect(host.textContent).toContain('Decide on the license from the customer page');
  });
});
