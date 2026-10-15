import { Page, Route } from '@playwright/test';

/**
 * An in-memory stand-in for the parts of the API the portal uses, so each test can drive the UI through a whole
 * journey (sign in, buy, pay) and see the state change. The backend's own API tests check the real rules.
 */
export class FakePortalApi {
  signedIn = false;
  requiresMfa = false;
  hasCompany = true;
  readonly calls: string[] = [];

  readonly me = { id: 'u1', email: 'owner@example.test', fullName: 'Mona Owner', emailConfirmed: true, mfaEnabled: false, roles: ['Customer'] };
  readonly company = {
    id: 'c1',
    name: 'Acme Trading',
    myRole: 'Owner',
    members: [
      { userId: 'u1', email: 'owner@example.test', fullName: 'Mona Owner', role: 'Owner', joinedAt: '2026-09-01T10:00:00Z' },
      { userId: 'u2', email: 'tech@example.test', fullName: 'Tarek Tech', role: 'Technical', joinedAt: '2026-09-02T10:00:00Z' },
    ],
    invitations: [] as { id: string; email: string; role: string; expiresAt: string }[],
  };
  readonly plans = [
    { id: '11111111-1111-1111-1111-111111111111', productCode: 'UBG', productName: 'UBG Monitor', code: 'STD', name: 'Standard', price: 1200, currency: 'EGP', durationDays: 365, maxDevices: 3, features: [] },
  ];
  readonly licenses = [
    { id: 'l1', licenseNumber: 'LIC-2026-0001', productCode: 'UBG', planCode: 'STD', productKeyPrefix: 'UBG-7K2Q', status: 'Active', issuedAt: '2026-09-01T10:00:00Z', expiresAt: '2027-09-01T10:00:00Z', maxDevices: 3, activeDevices: 1 },
  ];
  readonly devices = [
    { id: 'd1', deviceId: 'PC-01', deviceName: 'Front desk', operatingSystem: 'Windows 11', appVersion: '2.4.0', status: 'Active', activatedAt: '2026-09-01T10:00:00Z', lastSeenAt: '2026-10-15T09:00:00Z', online: true },
  ];
  readonly orders: Record<string, unknown>[] = [];
  readonly invoices: Record<string, unknown>[] = [];

  async install(page: Page): Promise<void> {
    await page.route('**/api/**', (route) => this.handle(route));
  }

  private async handle(route: Route): Promise<void> {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/^\/api\/v1/, '');
    const key = `${request.method()} ${path}`;
    const body = request.postData() ? request.postDataJSON() : {};
    this.calls.push(key);
    const json = (status: number, value?: unknown) =>
      route.fulfill({ status, contentType: 'application/json', body: value === undefined ? '' : JSON.stringify(value) });
    const problem = (status: number, code: string) => json(status, { status, code, title: code });

    if (key === 'GET /auth/csrf') return json(204);
    if (key === 'POST /auth/login') {
      if (body.password !== 'correct horse battery') return problem(400, 'INVALID_CREDENTIALS');
      if (this.requiresMfa) return json(200, { requiresMfa: true });
      this.signedIn = true;
      return json(200, { requiresMfa: false });
    }
    if (key === 'POST /auth/login/mfa') {
      if (body.code !== '123456') return problem(400, 'INVALID_MFA_CODE');
      this.signedIn = true;
      return json(204);
    }
    if (key === 'GET /catalog/plans') return json(200, this.plans);
    if (!this.signedIn) return problem(401, 'UNAUTHENTICATED');

    if (key === 'POST /auth/logout') {
      this.signedIn = false;
      return json(204);
    }
    if (key === 'GET /account/me') return json(200, this.me);
    if (key === 'GET /company') return this.hasCompany ? json(200, this.company) : problem(404, 'NO_COMPANY');
    if (key === 'POST /company') {
      this.hasCompany = true;
      this.company.name = body.name;
      return json(201, this.company);
    }
    if (key === 'POST /company/invitations') {
      this.company.invitations.push({ id: `i${this.company.invitations.length + 1}`, email: body.email, role: body.role, expiresAt: '2026-10-22T10:00:00Z' });
      return json(204);
    }
    if (key === 'GET /licenses') return json(200, this.licenses);
    if (key === 'GET /licenses/l1') return json(200, { license: this.licenses[0], devices: this.devices });
    if (key === 'POST /licenses/l1/product-key') return json(200, { productKey: 'UBG-7K2Q-AAAA-BBBB-CCCC' });
    if (key === 'GET /orders') return json(200, this.orders);
    if (key === 'POST /orders') {
      const plan = this.plans.find((p) => p.id === body.planId)!;
      const order = {
        id: `o${this.orders.length + 1}`, number: `ORD-2026-000${this.orders.length + 1}`, productName: plan.productName, planName: plan.name,
        total: plan.price, currency: plan.currency, status: 'PendingPayment', placedAt: '2026-10-15T10:00:00Z', paidAt: null, licenseNumber: null, payments: [],
      };
      this.orders.unshift(order);
      return json(201, order);
    }
    const order = this.orders.find((o) => key.startsWith(`GET /orders/${o['id']}`) || key.startsWith(`POST /orders/${o['id']}/`));
    if (order && key === `GET /orders/${order['id']}`) return json(200, order);
    if (key === 'GET /payments/providers') return json(200, ['sandbox']);
    if (order && key === `POST /orders/${order['id']}/pay`) {
      // The real sandbox page reports back through a signed notification; here the payment simply succeeds.
      Object.assign(order, { status: 'Fulfilled', paidAt: '2026-10-15T10:01:00Z', licenseNumber: 'LIC-2026-0002' });
      (order['payments'] as unknown[]).push({ id: 'p1', provider: 'sandbox', amount: order['total'], currency: 'EGP', status: 'Succeeded', createdAt: '2026-10-15T10:01:00Z', failureReason: null });
      this.invoices.unshift({ id: 'inv1', number: 'INV-2026-000001', orderId: order['id'], companyName: this.company.name, description: 'UBG Monitor Standard', amount: order['total'], currency: 'EGP', issuedAt: '2026-10-15T10:01:00Z' });
      return json(200, { redirectUrl: `/portal/orders/${order['id']}` });
    }
    if (key === 'GET /invoices') return json(200, this.invoices);
    return problem(404, 'NOT_FOUND');
  }
}
