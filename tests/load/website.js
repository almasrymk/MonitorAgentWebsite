// Load test for the website API (k6). Targets from the plan: p95 under 300 ms, under 0.1% errors.
//
//   k6 run -e BASE_URL=http://localhost:5200 -e SANDBOX_SECRET=... tests/load/website.js
//
// Scenarios run together:
//   agents    update checks from installed agents (10,000 devices checking about every 10 minutes, so ~17/s, and
//             a burst when a version ships)
//   visitors  people browsing the public site's data: releases, published pages, plans
//   webhooks  a wave of signed payment notifications, as a provider retrying after an outage would send them
// Not found is a normal answer here (an empty test database has no releases or pages), so only 5xx, timeouts and
// rate limiting count as failures.
import http from 'k6/http';
import crypto from 'k6/crypto';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:5200';
const SECRET = __ENV.SANDBOX_SECRET || '';
const CATALOG = __ENV.WITH_CATALOG === '1'; // only where a LicensingPlatform is connected

http.setResponseCallback(http.expectedStatuses({ min: 200, max: 299 }, 404));

export const options = {
  scenarios: {
    agents: {
      executor: 'ramping-arrival-rate',
      exec: 'agent',
      startRate: 17,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 400,
      stages: [
        { target: 17, duration: '2m' },
        { target: 170, duration: '1m' }, // a release goes out
        { target: 17, duration: '2m' },
      ],
    },
    visitors: {
      executor: 'constant-arrival-rate',
      exec: 'visitor',
      rate: 30,
      timeUnit: '1s',
      duration: '5m',
      preAllocatedVUs: 30,
      maxVUs: 200,
    },
    ...(SECRET && {
      webhooks: {
        executor: 'constant-arrival-rate',
        exec: 'webhook',
        rate: 50,
        timeUnit: '1s',
        duration: '1m',
        startTime: '2m',
        preAllocatedVUs: 50,
        maxVUs: 200,
      },
    }),
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    'http_req_duration{scenario:agents}': ['p(95)<150'],
    'http_req_duration{scenario:visitors}': ['p(95)<300'],
    'http_req_duration{scenario:webhooks}': ['p(95)<300'],
    checks: ['rate>0.999'],
  },
};

export function agent() {
  const res = http.get(`${BASE}/updates/manifest.json`, { tags: { name: 'manifest' } });
  check(res, { 'manifest answered': (r) => r.status === 200 || r.status === 404 });
}

const pages = ['terms', 'privacy', 'refunds', 'about-us'];

export function visitor() {
  const res = http.get(`${BASE}/api/v1/releases`, { tags: { name: 'releases' } });
  check(res, { 'releases answered': (r) => r.status === 200 });
  const page = pages[Math.floor(Math.random() * pages.length)];
  http.get(`${BASE}/api/v1/content/pages/${page}`, { tags: { name: 'content page' } });
  if (CATALOG) http.get(`${BASE}/api/v1/catalog/plans`, { tags: { name: 'plans' } });
}

export function webhook() {
  // Unknown references are acknowledged and ignored; this measures signature checks and de-duplication under load.
  const body = JSON.stringify({ eventId: `evt_load_${__VU}_${__ITER}`, reference: `load-${__VU}-${__ITER}`, status: 'succeeded', amount: 1200, currency: 'EGP' });
  const signature = crypto.hmac('sha256', SECRET, body, 'hex');
  const res = http.post(`${BASE}/api/v1/payments/notifications/sandbox`, body, {
    headers: { 'Content-Type': 'application/json', 'X-Sandbox-Signature': signature },
    tags: { name: 'webhook' },
  });
  check(res, { 'webhook answered': (r) => r.status < 500 && r.status !== 429 });
}
