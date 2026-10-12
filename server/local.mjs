// Runs the BlockCraft big server on this PC (needs Node.js): node local.mjs [port]
// Players on your Wi-Fi join with ws://<this PC's address>:<port>/ws. It also serves the game
// itself at http://<this PC's address>:<port>/ so they can just open that in a browser.
// The world is saved in server-data.json next to this file.
import http from 'node:http';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { World } from './world.js';
import { Arena } from './pvp.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const PORT = +(process.argv[2] || process.env.PORT || 8080);
const DATA = path.join(here, 'server-data.json');
const GAME = path.join(here, '..', 'index.html');

// ---- a tiny key/value store kept in one JSON file ----
let data = {};
try { data = JSON.parse(fs.readFileSync(DATA, 'utf8')); } catch (e) { }
let saveTimer = null;
const saveSoon = () => { clearTimeout(saveTimer); saveTimer = setTimeout(() => { fs.writeFileSync(DATA + '.tmp', JSON.stringify(data)); fs.renameSync(DATA + '.tmp', DATA); }, 500); };
const store = {
  async get(k) { return data[k]; },
  async put(k, v) { data[k] = v; saveSoon(); },
  async delete(k) { delete data[k]; saveSoon(); },
  async list(prefix) { return new Map(Object.entries(data).filter(([k]) => k.startsWith(prefix))); },
};

const world = new World(store);
const arena = new Arena();
await world.load();

const server = http.createServer((req, res) => {
  if (req.url === '/pvp/status') { res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' }); res.end(JSON.stringify(arena.status())); return; }
  if (req.url === '/status') { res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' }); res.end(JSON.stringify(world.status())); return; }
  if (req.url === '/' || req.url.startsWith('/?') || req.url === '/index.html') {
    try { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(fs.readFileSync(GAME)); }
    catch (e) { res.writeHead(404); res.end('index.html not found'); }
    return;
  }
  res.writeHead(404); res.end('Not found');
});

// ---- minimal WebSocket server (text messages only), so no extra downloads are needed ----
server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  if (!key || (req.headers.upgrade || '').toLowerCase() !== 'websocket') { socket.destroy(); return; }
  const accept = crypto.createHash('sha1').update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');
  socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ' + accept + '\r\n\r\n');
  socket.setNoDelay(true);
  let open = true;
  const frame = (op, payload) => {
    const n = payload.length;
    const head = n < 126 ? Buffer.from([0x80 | op, n]) : n < 65536 ? Buffer.from([0x80 | op, 126, n >> 8, n & 255]) : (() => { const b = Buffer.alloc(10); b[0] = 0x80 | op; b[1] = 127; b.writeBigUInt64BE(BigInt(n), 2); return b; })();
    if (open) socket.write(Buffer.concat([head, payload]));
  };
  const pvp = (req.url || '').startsWith('/pvp'), srv = pvp ? arena : world; // /pvp = the duel server, anything else = the world
  const c = srv.connect(t => frame(1, Buffer.from(t, 'utf8')), () => { frame(8, Buffer.alloc(0)); socket.end(); });
  let buf = Buffer.alloc(0), parts = [];
  socket.on('data', chunk => {
    buf = Buffer.concat([buf, chunk]);
    for (;;) {
      if (buf.length < 2) return;
      const fin = buf[0] & 0x80, op = buf[0] & 15, masked = buf[1] & 0x80;
      let len = buf[1] & 127, off = 2;
      if (len === 126) { if (buf.length < 4) return; len = buf.readUInt16BE(2); off = 4; }
      else if (len === 127) { if (buf.length < 10) return; len = Number(buf.readBigUInt64BE(2)); off = 10; }
      if (len > 4 * 1024 * 1024) { socket.destroy(); return; }
      const mOff = off; if (masked) off += 4;
      if (buf.length < off + len) return;
      const payload = Buffer.from(buf.subarray(off, off + len));
      if (masked) for (let i = 0; i < len; i++) payload[i] ^= buf[mOff + (i & 3)];
      buf = buf.subarray(off + len);
      if (op === 8) { frame(8, Buffer.alloc(0)); socket.end(); return; }
      if (op === 9) { frame(10, payload); continue; }
      if (op === 1 || op === 0) {
        parts.push(payload);
        if (fin) { const text = Buffer.concat(parts).toString('utf8'); parts = []; if (pvp) arena.message(c, text); else world.message(c, text).catch(e => console.log('message error', e)); }
      }
    }
  });
  const gone = () => { if (!open) return; open = false; srv.disconnect(c); };
  socket.on('close', gone); socket.on('error', gone);
});

server.listen(PORT, () => {
  const ips = Object.values(os.networkInterfaces()).flat().filter(a => a && a.family === 'IPv4' && !a.internal).map(a => a.address);
  console.log('BlockCraft big server is running (up to 100 players). Keep this window open.');
  console.log('On this PC, join with:      ws://localhost:' + PORT + '/ws   (PvP duels use the same address)');
  for (const ip of ips) console.log('On your Wi-Fi, join with:   ws://' + ip + ':' + PORT + '/ws   (or open http://' + ip + ':' + PORT + '/ )');
});
process.on('SIGINT', async () => { await world.flush(true); clearTimeout(saveTimer); fs.writeFileSync(DATA, JSON.stringify(data)); process.exit(0); });
