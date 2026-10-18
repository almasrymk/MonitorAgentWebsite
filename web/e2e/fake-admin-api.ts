import { Page, Route } from '@playwright/test';

/** An in-memory stand-in for the admin endpoints, so the admin UI can be driven end to end. */
export class FakeAdminApi {
  signedIn = false;
  /** Whether this browser session was signed in with the second factor (the API's "amr=mfa"). */
  sessionHasMfa = false;
  readonly calls: string[] = [];
  readonly me = { id: 'a1', email: 'admin@example.test', fullName: 'Adel Admin', emailConfirmed: true, mfaEnabled: true, roles: ['Admin'] };

  readonly orders = [
    { id: 'o1', number: 'ORD-2026-000001', productName: 'UBG Monitor', planName: 'Standard', total: 1200, currency: 'EGP', status: 'PendingPayment', placedAt: '2026-10-14T10:00:00Z', paidAt: null, licenseNumber: null, payments: [] },
    { id: 'o2', number: 'ORD-2026-000002', productName: 'UBG Monitor', planName: 'Professional', total: 3000, currency: 'EGP', status: 'Fulfilled', placedAt: '2026-10-13T10:00:00Z', paidAt: '2026-10-13T10:05:00Z', licenseNumber: 'LIC-2026-0007', payments: [] },
  ];
  readonly releases = [
    { id: 'r1', version: '2.3.1', channel: 'stable', notes: null, status: 'Published', createdAt: '2026-09-01T10:00:00Z', publishedAt: '2026-09-01T10:00:00Z', packages: [{ platform: 'win-x64', url: 'https://x.test/a.zip', sha256: 'ab'.repeat(32), size: 48234496 }] },
  ];
  readonly events = [
    { id: 'e1', userId: 'a1aaaaaa-0000-0000-0000-000000000000', type: 'SignedIn', occurredAt: '2026-10-15T09:00:00Z', ipAddress: '203.0.113.7' },
    { id: 'e2', userId: 'b2bbbbbb-0000-0000-0000-000000000000', type: 'SignInFailed', occurredAt: '2026-10-15T08:00:00Z', ipAddress: '198.51.100.4' },
  ];
  uploaded: { channel: string; manifestBase64: string; signature: string } | null = null;

  async install(page: Page): Promise<void> {
    await page.route('**/api/**', (route) => this.handle(route));
  }

  private async handle(route: Route): Promise<void> {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/^\/api\/v1/, '');
    const key = `${request.method()} ${path}`;
    const body = request.postData() ? request.postDataJSON() : {};
    this.calls.push(`${key}${url.search}`);
    const json = (status: number, value?: unknown) =>
      route.fulfill({ status, contentType: 'application/json', body: value === undefined ? '' : JSON.stringify(value) });
    const problem = (status: number, code: string) => json(status, { status, code, title: code });

    if (key === 'GET /auth/csrf') return json(204);
    if (key === 'POST /auth/login') {
      if (body.password !== 'correct horse battery') return problem(400, 'INVALID_CREDENTIALS');
      return json(200, { requiresMfa: this.me.mfaEnabled });
    }
    if (key === 'POST /auth/login/mfa') {
      this.signedIn = true;
      this.sessionHasMfa = true;
      return json(204);
    }
    if (!this.signedIn) return problem(401, 'UNAUTHENTICATED');
    if (key === 'GET /account/me') return json(200, this.me);
    if (key === 'POST /auth/logout') {
      this.signedIn = false;
      return json(204);
    }
    if (key === 'GET /account/mfa/setup') return json(200, { sharedKey: 'JBSW Y3DP EHPK 3PXP', authenticatorUri: 'otpauth://totp/x' });
    if (key === 'POST /account/mfa/enable') {
      this.me.mfaEnabled = true;
      return json(200, { recoveryCodes: ['aaaa-bbbb', 'cccc-dddd'] });
    }
    if (path.startsWith('/admin/') && !this.sessionHasMfa) return problem(403, 'MFA_REQUIRED');

    if (key === 'GET /admin/orders') {
      const status = url.searchParams.get('status');
      const items = this.orders.filter((o) => !status || o.status === status);
      return json(200, { items, page: 1, pageSize: Number(url.searchParams.get('pageSize') ?? 50), totalCount: items.length });
    }
    const manual = key.match(/^POST \/admin\/orders\/(\w+)\/manual-payment$/);
    if (manual) {
      const order = this.orders.find((o) => o.id === manual[1])!;
      Object.assign(order, { status: 'Fulfilled', paidAt: '2026-10-15T11:00:00Z', licenseNumber: 'LIC-2026-0008' });
      return json(200, order);
    }
    if (key === 'GET /admin/releases') return json(200, this.releases);
    if (key === 'POST /admin/releases') {
      this.uploaded = body;
      const release = { id: 'r2', version: '2.4.0', channel: body.channel, notes: null, status: 'Draft', createdAt: '2026-10-15T11:00:00Z', publishedAt: null, packages: this.releases[0].packages };
      this.releases.unshift(release);
      return json(200, release);
    }
    const publish = key.match(/^POST \/admin\/releases\/(\w+)\/(publish|withdraw)$/);
    if (publish) {
      const release = this.releases.find((r) => r.id === publish[1])!;
      Object.assign(release, publish[2] === 'publish' ? { status: 'Published', publishedAt: '2026-10-15T11:05:00Z' } : { status: 'Withdrawn' });
      return json(200, release);
    }
    if (key === 'GET /admin/security-events') {
      const user = url.searchParams.get('userId');
      const items = this.events.filter((e) => !user || e.userId === user);
      return json(200, { items, page: 1, pageSize: 50, totalCount: items.length });
    }
    return problem(404, 'NOT_FOUND');
  }
}
