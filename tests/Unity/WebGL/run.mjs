// Serves a WebGL test player built by tests/Unity (see CONTRIBUTING.md) and runs it in headless Chromium. The
// player reports through ConsoleReporter, so this prints the XAPI-TEST lines of the browser console and exits
// with 1 when a test failed or the run did not finish.
//   node run.mjs <player directory> [timeout in minutes, default 15]
// CHROME points at a Chromium binary; without it, Playwright's own (npx playwright-core install chromium --only-shell).
import { createServer } from 'node:http';
import { existsSync, readFileSync } from 'node:fs';
import { extname, join, resolve, sep } from 'node:path';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { chromium } from 'playwright-core';

if (!process.argv[2]) {
  console.error('usage: node run.mjs <player directory> [timeout in minutes]');
  process.exit(2);
}

const root = resolve(process.argv[2]);
const timeout = (Number(process.argv[3]) || 15) * 60_000;
const types = {
  '.html': 'text/html', '.js': 'application/javascript', '.wasm': 'application/wasm', '.css': 'text/css',
  '.png': 'image/png', '.ico': 'image/x-icon', '.json': 'application/json',
};

const server = createServer((request, response) => {
  let file = join(root, decodeURIComponent(new URL(request.url, 'http://localhost').pathname));
  if (file.endsWith(sep)) file = join(file, 'index.html');
  if (!file.startsWith(root) || !existsSync(file)) {
    response.writeHead(404).end();
    return;
  }

  // Decompressed here, so compressed builds load over plain http without Content-Encoding
  let body = readFileSync(file);
  if (file.endsWith('.br')) [body, file] = [brotliDecompressSync(body), file.slice(0, -3)];
  else if (file.endsWith('.gz')) [body, file] = [gunzipSync(body), file.slice(0, -3)];
  response.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream' }).end(body);
});
await new Promise(started => server.listen(0, '127.0.0.1', started));

const browser = await chromium.launch(process.env.CHROME ? { executablePath: process.env.CHROME } : {});
const page = await browser.newPage();
let finish;
const finished = new Promise(resolve => (finish = resolve));
page.on('console', message => {
  const text = message.text();
  if (text.startsWith('XAPI-TEST')) console.log(text.trimEnd());
  const summary = /^XAPI-TEST run finished: (\d+) passed, (\d+) failed/.exec(text);
  if (summary) finish(Number(summary[2]) === 0 ? 0 : 1);
});
page.on('pageerror', error => console.log(`page error: ${error.message}`));

await page.goto(`http://localhost:${server.address().port}/index.html`);
const code = await Promise.race([
  finished,
  new Promise(stop => setTimeout(() => { console.log(`No result after ${timeout / 60_000} minutes`); stop(1); }, timeout)),
]);
await browser.close();
server.close();
process.exit(code);
