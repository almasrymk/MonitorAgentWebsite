// Serves a built single-page app (portal or admin) under its base path, the way the reverse proxy does in production:
// files as they are, and index.html for every other path so the app's router can handle it.
// Usage: node e2e/serve.mjs <app> <port>
import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { extname, join, normalize } from 'node:path';

const [app, port] = process.argv.slice(2);
const root = join(import.meta.dirname, '..', 'dist', app, 'browser');
const base = `/${app}/`;
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.json': 'application/json' };

createServer((req, res) => {
  const path = decodeURIComponent((req.url ?? '/').split('?')[0]);
  if (!path.startsWith(base)) {
    res.writeHead(302, { location: base }).end();
    return;
  }
  let file = normalize(join(root, path.slice(base.length)));
  if (!file.startsWith(root) || !existsSync(file) || statSync(file).isDirectory()) file = join(root, 'index.html');
  res.writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' });
  createReadStream(file).pipe(res);
}).listen(Number(port), () => console.log(`${app} on ${port}`));
