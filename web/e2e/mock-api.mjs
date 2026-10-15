// A stand-in for the website API so the UI can be tested without SQL Server or LicensingPlatform.
// Serves fixed catalog and release data; anything else is 404.
import { createServer } from 'node:http';

const plans = [
  { id: '11111111-1111-1111-1111-111111111111', productCode: 'UBG', productName: 'UBG Monitor', code: 'STD', name: 'Standard', price: 1200, currency: 'EGP', durationDays: 365, maxDevices: 3, features: ['Device monitoring', 'Email alerts'] },
  { id: '22222222-2222-2222-2222-222222222222', productCode: 'UBG', productName: 'UBG Monitor', code: 'PRO', name: 'Professional', price: 3000, currency: 'EGP', durationDays: 365, maxDevices: 10, features: ['Everything in Standard', 'Reports'] },
];

const pkg = { platform: 'win-x64', url: 'https://downloads.example.test/MonitorAgent-2.4.0-win-x64.zip', sha256: 'ab'.repeat(32), size: 48234496 };
const releases = [
  { id: 'r2', version: '2.4.0', channel: 'stable', notes: 'Faster startup.\nService alerts for SQL Server.', status: 'Published', createdAt: '2026-10-01T10:00:00Z', publishedAt: '2026-10-01T10:00:00Z', packages: [pkg] },
  { id: 'r1', version: '2.3.1', channel: 'stable', notes: 'Bug fixes.', status: 'Published', createdAt: '2026-09-01T10:00:00Z', publishedAt: '2026-09-01T10:00:00Z', packages: [pkg] },
];

const routes = {
  'GET /api/v1/catalog/plans': plans,
  'GET /api/v1/releases': releases,
};

const port = Number(process.env.MOCK_API_PORT ?? 5099);
createServer((req, res) => {
  const body = routes[`${req.method} ${req.url?.split('?')[0]}`];
  res.setHeader('content-type', 'application/json');
  if (body === undefined) {
    res.statusCode = 404;
    res.end(JSON.stringify({ code: 'NOT_FOUND' }));
    return;
  }
  res.end(JSON.stringify(body));
}).listen(port, () => console.log(`mock api on ${port}`));
