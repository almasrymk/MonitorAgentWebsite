import { Page, Route } from '@playwright/test';

function version(number: number, status: string, titleEn: string, editedBy: string) {
  return {
    number, status, titleEn, titleAr: `${titleEn} (ar)`, descriptionEn: '', descriptionAr: '', bodyEn: `## ${titleEn}\nWe build **Monitor Agent**.`,
    bodyAr: '## عنّا\nبنعمل **Monitor Agent**.', editedBy, editedAt: '2026-10-14T10:00:00Z', reviewedBy: null as string | null,
    reviewedAt: null as string | null, reviewNote: null as string | null, publishedAt: null as string | null,
  };
}

/** An in-memory stand-in for the admin endpoints, so the admin UI can be driven end to end. */
export class FakeAdminApi {
  signedIn = false;
  /** Whether this browser session was signed in with the second factor (the API's "amr=mfa"). */
  sessionHasMfa = false;
  readonly calls: string[] = [];
  readonly me = { id: 'a1', email: 'admin@example.test', fullName: 'Adel Admin', emailConfirmed: true, mfaEnabled: true, roles: ['Admin'] };

  readonly orders = [
    { id: 'o1', number: 'ORD-2026-000001', productName: 'UBG Monitor', planName: 'Standard', total: 1200, currency: 'EGP', status: 'PendingPayment', placedAt: '2026-10-14T10:00:00Z', paidAt: null, licenseNumber: null, payments: [], companyId: 'c1', companyName: 'Nile Traders' },
    { id: 'o2', number: 'ORD-2026-000002', productName: 'UBG Monitor', planName: 'Professional', total: 3000, currency: 'EGP', status: 'Fulfilled', placedAt: '2026-10-13T10:00:00Z', paidAt: '2026-10-13T10:05:00Z', licenseNumber: 'LIC-2026-0007', payments: [], companyId: 'c2', companyName: 'Delta Pharma' },
  ];
  readonly releases = [
    { id: 'r1', version: '2.3.1', channel: 'stable', notes: null, status: 'Published', createdAt: '2026-09-01T10:00:00Z', publishedAt: '2026-09-01T10:00:00Z', packages: [{ platform: 'win-x64', url: 'https://x.test/a.zip', sha256: 'ab'.repeat(32), size: 48234496 }] },
  ];
  readonly events = [
    { id: 'e1', userId: 'a1aaaaaa-0000-0000-0000-000000000000', type: 'SignedIn', occurredAt: '2026-10-15T09:00:00Z', ipAddress: '203.0.113.7' },
    { id: 'e2', userId: 'b2bbbbbb-0000-0000-0000-000000000000', type: 'SignInFailed', occurredAt: '2026-10-15T08:00:00Z', ipAddress: '198.51.100.4' },
  ];
  readonly tickets = [
    {
      id: 't1', number: 'T-261014-ABCDE', subject: 'Agent stops at night', category: 'Technical', status: 'Open', openedAt: '2026-10-14T10:00:00Z',
      lastActivityAt: '2026-10-14T10:00:00Z', companyName: 'Nile Traders',
      messages: [{ id: 'm1', authorName: 'Mona Owner', fromStaff: false, body: 'The service stops every night.', postedAt: '2026-10-14T10:00:00Z' }],
    },
  ];
  readonly suggestions = [
    { id: 's1', title: 'Weekly report', body: 'Email me a summary.', status: 'New', staffNote: null as string | null, createdAt: '2026-10-10T10:00:00Z', companyName: 'Nile Traders' },
  ];
  readonly pages = [
    {
      id: 'p1', slug: 'about-us', publishedVersion: 1 as number | null,
      versions: [
        version(2, 'InReview', 'About Monitor Agent', 'Eman Editor'),
        { ...version(1, 'Published', 'About us', 'Eman Editor'), reviewedBy: 'Rami Reviewer', reviewedAt: '2026-10-01T10:00:00Z', publishedAt: '2026-10-01T10:00:00Z' },
      ],
    },
    { id: 'p2', slug: 'privacy', publishedVersion: null as number | null, versions: [version(1, 'Draft', 'Privacy policy', 'Adel Admin')] },
  ];
  readonly companies = [
    { id: 'c1', name: 'Nile Traders', ownerName: 'Mona Owner', ownerEmail: 'mona@nile.test', members: 2, createdAt: '2026-09-01T10:00:00Z' },
    { id: 'c2', name: 'Delta Pharma', ownerName: 'Omar Owner', ownerEmail: 'omar@delta.test', members: 1, createdAt: '2026-08-01T10:00:00Z' },
  ];
  readonly licenses = [
    { id: 'l1', licenseNumber: 'LIC-2026-0003', productCode: 'UBG', planCode: 'STD', productKeyPrefix: 'UBG-AB', status: 'Active', issuedAt: '2026-09-02T10:00:00Z',
      expiresAt: '2027-09-02T10:00:00Z', maxDevices: 3, activeDevices: 2 },
  ];
  grants: unknown[] = [];
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
      const company = url.searchParams.get('companyId');
      const items = this.orders.filter((o) => (!status || o.status === status) && (!company || o.companyId === company));
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
    if (key === 'GET /admin/support/tickets') {
      const status = url.searchParams.get('status');
      const items = this.tickets.filter((t) => !status || t.status === status).map(({ messages: _, ...t }) => t);
      return json(200, { items, page: 1, pageSize: 25, totalCount: items.length });
    }
    const ticketRoute = key.match(/^(GET|POST) \/admin\/support\/tickets\/(\w+)(\/messages|\/close)?$/);
    if (ticketRoute) {
      const ticket = this.tickets.find((t) => t.id === ticketRoute[2])!;
      if (ticketRoute[3] === '/messages') {
        ticket.messages.push({ id: 'm2', authorName: this.me.fullName, fromStaff: true, body: body.body, postedAt: '2026-10-15T11:00:00Z' });
        ticket.status = 'Answered';
      }
      if (ticketRoute[3] === '/close') ticket.status = 'Closed';
      return json(200, ticket);
    }
    if (key === 'GET /admin/support/suggestions') return json(200, { items: this.suggestions, page: 1, pageSize: 25, totalCount: this.suggestions.length });
    const answer = key.match(/^PUT \/admin\/support\/suggestions\/(\w+)$/);
    if (answer) {
      const suggestion = this.suggestions.find((x) => x.id === answer[1])!;
      Object.assign(suggestion, { status: body.status, staffNote: body.note });
      return json(200, suggestion);
    }
    if (key === 'GET /admin/customers') {
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const items = this.companies.filter((c) => !search || c.name.toLowerCase().includes(search) || c.ownerEmail === search);
      return json(200, { items, page: 1, pageSize: 25, totalCount: items.length });
    }
    const customer = key.match(/^GET \/admin\/customers\/(\w+)(\/licenses)?$/);
    if (customer) {
      const c = this.companies.find((x) => x.id === customer[1]);
      if (!c) return problem(404, 'COMPANY_NOT_FOUND');
      if (customer[2]) return json(200, c.id === 'c1' ? this.licenses : []);
      return json(200, {
        id: c.id, name: c.name, createdAt: c.createdAt,
        members: [
          { userId: 'u1', fullName: c.ownerName, email: c.ownerEmail, role: 'Owner', joinedAt: c.createdAt },
          { userId: 'u2', fullName: 'Tarek Tech', email: 'tarek@nile.test', role: 'Technical', joinedAt: '2026-09-05T10:00:00Z' },
        ],
        pendingInvitations: [{ email: 'new@nile.test', role: 'Viewer', expiresAt: '2026-10-20T10:00:00Z' }],
      });
    }
    if (key === 'GET /catalog/plans') {
      return json(200, [{ id: 'p-std', productCode: 'UBG', productName: 'UBG Monitor', code: 'STD', name: 'Standard', price: 1200, currency: 'EGP', durationDays: 365, maxDevices: 3, features: [] }]);
    }
    if (key === 'POST /admin/licenses/grants') {
      this.grants.push(body);
      this.licenses.push({ ...this.licenses[0], id: 'l2', licenseNumber: 'LIC-2026-0099', activeDevices: 0 });
      return json(200, { grantId: 'g1', licenseNumber: 'LIC-2026-0099', expiresAt: null });
    }
    if (key === 'GET /admin/reports/revenue') {
      return json(200, {
        from: url.searchParams.get('from'), to: url.searchParams.get('to'), truncated: false,
        totals: [{ currency: 'EGP', payments: 4200, refunds: 1200, net: 3000, paymentCount: 2, refundCount: 1 }],
        byMonth: [{ month: '2026-10', currency: 'EGP', net: 3000, paymentCount: 2 }],
        byPlan: [{ productName: 'UBG Monitor', planName: 'Professional', currency: 'EGP', net: 3000, paymentCount: 1 }],
        byProvider: [{ provider: 'paymob', currency: 'EGP', net: 3000, paymentCount: 2 }],
      });
    }
    if (key === 'GET /admin/content/pages') {
      return json(200, this.pages.map((p) => {
        const open = p.versions.find((v) => v.status === 'Draft' || v.status === 'InReview');
        const latest = open ?? p.versions[0];
        return { id: p.id, slug: p.slug, titleAr: latest.titleAr, titleEn: latest.titleEn, publishedVersion: p.publishedVersion, openStatus: open?.status ?? null, updatedAt: latest.editedAt };
      }));
    }
    const pageRoute = key.match(/^(GET|PUT|POST) \/admin\/content\/pages\/(\w+)(\/.*)?$/);
    if (pageRoute) {
      const p = this.pages.find((x) => x.id === pageRoute[2]);
      if (!p) return problem(404, 'PAGE_NOT_FOUND');
      const open = p.versions.find((v) => v.status === 'Draft' || v.status === 'InReview');
      const step = pageRoute[3] ?? '';
      if (step === '/draft') {
        if (open?.status === 'InReview') return problem(409, 'PAGE_IN_REVIEW');
        if (open) Object.assign(open, body, { reviewNote: null, editedBy: this.me.fullName });
        else p.versions.unshift({ ...version(p.versions.length + 1, 'Draft', body.titleEn, this.me.fullName), ...body });
      } else if (step === '/submit') {
        if (!open?.bodyEn) return problem(400, 'PAGE_INCOMPLETE');
        open.status = 'InReview';
      } else if (step === '/approve') {
        if (open?.editedBy === this.me.fullName) return problem(403, 'OWN_VERSION');
        p.versions.filter((v) => v.status === 'Published').forEach((v) => (v.status = 'Superseded'));
        Object.assign(open!, { status: 'Published', reviewedBy: this.me.fullName, publishedAt: '2026-10-15T12:00:00Z' });
        p.publishedVersion = open!.number;
      } else if (step === '/reject') {
        Object.assign(open!, { status: 'Draft', reviewedBy: this.me.fullName, reviewNote: body.note });
      } else if (step === '/withdraw') {
        open!.status = 'Draft';
      }
      return json(200, p);
    }
    if (key === 'GET /admin/security-events') {
      const user = url.searchParams.get('userId');
      const items = this.events.filter((e) => !user || e.userId === user);
      return json(200, { items, page: 1, pageSize: 50, totalCount: items.length });
    }
    return problem(404, 'NOT_FOUND');
  }
}
