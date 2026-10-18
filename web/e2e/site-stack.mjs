// Starts the mock API and the built site's SSR server together, for Lighthouse runs (see lighthouserc.json).
import { spawn } from 'node:child_process';

const children = [
  spawn('node', ['e2e/mock-api.mjs'], { stdio: 'inherit' }),
  spawn('node', ['dist/site/server/server.mjs'], {
    stdio: 'inherit',
    env: { ...process.env, PORT: '4400', API_URL: 'http://localhost:5099', NG_ALLOWED_HOSTS: 'localhost' },
  }),
];
setTimeout(() => console.log('site stack listening on 4400'), 1500);
const stop = () => children.forEach((c) => c.kill());
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
