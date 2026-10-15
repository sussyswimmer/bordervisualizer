#!/usr/bin/env node
// Renders assets/banner.svg to assets/banner.png at the SVG's own size (1280×640, doc 08 §1). The PNG is the README
// header and the repository's social preview image.
//
//   node build/banner.mjs                      renders assets/banner.svg to assets/banner.png
//   node build/banner.mjs <in.svg> <out.png>   renders another file
//
// Needs Node.js 22 or later (for its built-in WebSocket) and a Chromium-based browser, used headless with a throwaway
// profile; nothing is installed and no npm packages are needed. On Windows the preinstalled Microsoft Edge works. It
// looks for Edge or Chrome in their usual folders, then on PATH, then for a Playwright Chromium. To use another
// browser, set BANNER_BROWSER to its executable.
//
// The page is drawn through the DevTools protocol rather than the browser's --screenshot switch: headless windows
// reserve room for browser UI, so --screenshot cuts the bottom of a 1280×640 page. Here the viewport is set to the
// SVG's size exactly, at 1 device pixel per pixel, in sRGB.

import { spawn } from 'node:child_process';
import { accessSync, constants, existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { delimiter, dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const [input = join(root, 'assets', 'banner.svg'), output = join(root, 'assets', 'banner.png')] = process.argv.slice(2);

function fail(message) {
    console.error(`banner: ${message}`);
    process.exit(1);
}

if (typeof WebSocket !== 'function') fail(`needs Node.js 22 or later (this is ${process.version}).`);
if (!existsSync(input)) fail(`${input} not found.`);

// The size comes from the root <svg>'s width and height, in pixels.
const svg = readFileSync(input, 'utf8');
const rootTag = /<svg\b[^>]*>/.exec(svg)?.[0] ?? '';
const width = Number(/\swidth="(\d+)"/.exec(rootTag)?.[1]);
const height = Number(/\sheight="(\d+)"/.exec(rootTag)?.[1]);
if (!(width > 0 && height > 0)) fail(`${input} needs width and height in pixels on its <svg> element.`);

const browser = findBrowser();
if (!browser) {
    fail('no Chromium-based browser found. Install Microsoft Edge or Google Chrome, or set BANNER_BROWSER to the '
        + 'browser executable.');
}

const profile = mkdtempSync(join(tmpdir(), 'banner-'));
const flags = [
    '--headless', '--disable-gpu', '--hide-scrollbars', '--mute-audio', '--no-first-run', '--no-default-browser-check',
    '--disable-extensions', '--disable-sync', '--disable-default-apps', '--disable-background-networking',
    '--disable-component-update', '--force-color-profile=srgb', '--force-device-scale-factor=1',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank',
];
// Chromium refuses to start its sandbox as root (containers, CI).
if (process.platform === 'linux' && process.getuid?.() === 0) flags.unshift('--no-sandbox');

const child = spawn(browser, flags, { stdio: ['ignore', 'ignore', 'pipe'] });
let stderr = '';
child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-4000); });
let exited = false;
let startError = '';
const exit = new Promise(done => {
    child.on('exit', () => { exited = true; done(); });
    child.on('error', error => { startError = error.message; exited = true; done(); });
});

let socket;
let failure;
try {
    socket = await connect();
    const cdp = protocol(socket);
    const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
    const page = (method, params) => cdp.send(method, params, sessionId);

    await page('Page.enable');
    await page('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
    // Anything the SVG leaves uncovered stays transparent, so it shows in the PNG instead of passing for white.
    await page('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
    const loaded = cdp.once('Page.loadEventFired', sessionId);
    const { errorText } = await page('Page.navigate', { url: pathToFileURL(resolve(input)).href });
    if (errorText) throw new Error(`could not open ${input}: ${errorText}`);
    await loaded;
    // Two frames, so the filters have been drawn before the capture.
    await page('Runtime.evaluate', {
        expression: 'new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))',
        awaitPromise: true,
    });
    const { data } = await page('Page.captureScreenshot', {
        format: 'png', clip: { x: 0, y: 0, width, height, scale: 1 }, captureBeyondViewport: false,
    });

    const png = Buffer.from(data, 'base64');
    const size = [png.readUInt32BE(16), png.readUInt32BE(20)];
    if (size[0] !== width || size[1] !== height) throw new Error(`got a ${size[0]}×${size[1]} image, not ${width}×${height}.`);
    writeFileSync(output, png);
    console.log(`banner: ${output} (${width}×${height}, ${Math.round(png.length / 1024)} KB)`);

    await cdp.send('Browser.close').catch(() => {});
} catch (error) {
    failure = error;
} finally {
    socket?.close();
    if (!exited) {
        const timer = setTimeout(() => child.kill(), 5000);
        await exit;
        clearTimeout(timer);
    }
    rmSync(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}

if (failure) {
    if (stderr.trim()) console.error(stderr.trim());
    fail(failure.message);
}

// The browser writes its DevTools port to DevToolsActivePort in the profile once it listens.
async function connect() {
    const file = join(profile, 'DevToolsActivePort');
    const deadline = Date.now() + 30_000;
    let lines = [];
    while (!/^\d+$/.test(lines[0] ?? '') || !(lines[1] ?? '').startsWith('/devtools/')) {
        if (exited) throw new Error(startError ? `could not start ${browser}: ${startError}` : `${browser} exited before it was ready.`);
        if (Date.now() > deadline) throw new Error(`${browser} did not open its DevTools port within 30 s.`);
        await new Promise(done => setTimeout(done, 100));
        lines = existsSync(file) ? readFileSync(file, 'utf8').split('\n').map(line => line.trim()) : [];
    }

    const ws = new WebSocket(`ws://127.0.0.1:${lines[0]}${lines[1]}`);
    await new Promise((done, failed) => {
        ws.onopen = done;
        ws.onerror = () => failed(new Error('could not connect to the browser.'));
    });
    return ws;
}

// A minimal DevTools protocol client: send a command and wait for its reply, or wait once for an event.
function protocol(ws) {
    let lastId = 0;
    const replies = new Map();
    const waiters = [];
    ws.onmessage = ({ data }) => {
        const message = JSON.parse(data);
        const reply = replies.get(message.id);
        if (reply) {
            replies.delete(message.id);
            if (message.error) reply.failed(new Error(`${reply.method}: ${message.error.message}`));
            else reply.done(message.result);
            return;
        }

        for (const waiter of waiters.splice(0)) {
            if (waiter.method === message.method && waiter.sessionId === message.sessionId) waiter.done(message.params);
            else waiters.push(waiter);
        }
    };
    ws.onclose = () => {
        for (const reply of replies.values()) reply.failed(new Error('the browser closed the connection.'));
    };

    return {
        send(method, params = {}, sessionId) {
            const id = ++lastId;
            ws.send(JSON.stringify({ id, method, params, sessionId }));
            return new Promise((done, failed) => replies.set(id, { method, done, failed }));
        },
        once(method, sessionId) {
            return new Promise(done => waiters.push({ method, sessionId, done }));
        },
    };
}

function findBrowser() {
    if (process.env.BANNER_BROWSER) return process.env.BANNER_BROWSER;

    const candidates = [];
    if (process.platform === 'win32') {
        for (const base of [process.env['ProgramFiles(x86)'], process.env.ProgramFiles, process.env.LOCALAPPDATA]) {
            if (!base) continue;
            candidates.push(join(base, 'Microsoft', 'Edge', 'Application', 'msedge.exe'));
            candidates.push(join(base, 'Google', 'Chrome', 'Application', 'chrome.exe'));
        }
    } else if (process.platform === 'darwin') {
        candidates.push('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
            '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
            '/Applications/Chromium.app/Contents/MacOS/Chromium');
    }

    const names = process.platform === 'win32' ? ['msedge.exe', 'chrome.exe']
        : ['google-chrome', 'google-chrome-stable', 'chromium', 'chromium-browser', 'microsoft-edge', 'microsoft-edge-stable'];
    for (const folder of (process.env.PATH ?? '').split(delimiter).filter(Boolean)) {
        for (const name of names) candidates.push(join(folder, name));
    }

    // Playwright's Chromium (npx playwright install chromium), newest first.
    const playwright = process.env.PLAYWRIGHT_BROWSERS_PATH || (process.platform === 'win32'
        ? join(process.env.LOCALAPPDATA ?? '', 'ms-playwright')
        : process.platform === 'darwin' ? join(homedir(), 'Library', 'Caches', 'ms-playwright')
            : join(homedir(), '.cache', 'ms-playwright'));
    if (existsSync(playwright)) {
        const builds = readdirSync(playwright).filter(name => /^chromium-\d+$/.test(name))
            .sort((a, b) => Number(b.split('-')[1]) - Number(a.split('-')[1]));
        for (const build of builds) {
            candidates.push(join(playwright, build, 'chrome-win', 'chrome.exe'),
                join(playwright, build, 'chrome-linux', 'chrome'),
                join(playwright, build, 'chrome-mac', 'Chromium.app', 'Contents', 'MacOS', 'Chromium'));
        }
    }

    return candidates.find(isExecutable);
}

function isExecutable(path) {
    try {
        accessSync(path, process.platform === 'win32' ? constants.F_OK : constants.X_OK);
        return true;
    } catch {
        return false;
    }
}
