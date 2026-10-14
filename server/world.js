// BlockCraft big server: one shared world for up to 100 players.
// This file has the game logic only; worker.js runs it on Cloudflare (always online) and
// local.mjs runs it on your own PC. Players' games do the 3D world, physics and their own
// nearby mobs; the server stores the world (block changes, chests, inventories), keeps the
// time of day, grows crops, and passes messages between players.
export const MAX_PLAYERS = 100;
export const SERVER_PROTO = 2; // 2 = accounts with passwords (1 = old games, which can still log in on the computer they played on)
const NEAR = 96;            // players/mobs farther away than this aren't sent to you
const WHEAT = 46, WHEAT_YOUNG = 48, CHEST = 35;
const NAME_RE = /^[A-Za-z0-9_ \-]{1,16}$/;
export const ADMINS = ['darkwing']; // accounts (lower-case names) that can use the admin panel
const MAX_CLOUD_WORLDS = 30, MAX_CLOUD_BYTES = 30 * 1024 * 1024, CLOUD_PART = 120000; // cloud saves: worlds per account, size of one (gzipped) world, bytes per stored part

export class World {
  // store: { get(key), put(key, value), delete(key), list(prefix) -> Map }  (all async)
  constructor(store) {
    this.store = store;
    this.clients = new Map();   // id -> client
    this.dirty = new Set();
    this.lastFlush = Date.now();
    this.lastCrops = 0;
    this.seq = 0;
  }

  async load() {
    let info = await this.store.get('world');
    if (!info) {
      info = { seed: Math.floor(Math.random() * 1e9), name: 'BlockCraft Server', mode: 'survival', epoch: Date.now(), t0: 0.3, created: Date.now() };
      await this.store.put('world', info);
    }
    this.info = info;
    this.chunks = new Map();   // "cx,cz" -> Map(blockIndex -> id)
    for (const [k, v] of await this.store.list('c:')) this.chunks.set(k.slice(2), new Map(pairs(v)));
    this.meta = new Map();
    for (const [k, v] of await this.store.list('m:')) this.meta.set(k.slice(2), v);
    this.chests = new Map();
    for (const [k, v] of await this.store.list('h:')) this.chests.set(k.slice(2), v);
    this.crops = new Map();
    for (const [k, v] of await this.store.list('r:')) this.crops.set(k.slice(2), v);
    // accounts, keyed by the lower-case name so "Sam" and "sam" are the same account:
    // { name, hash, salt, tokens: [computers that are logged in], save, first }
    this.players = new Map();
    for (const [k, v] of await this.store.list('p:')) {
      const name = v.name || k.slice(2), key = name.toLowerCase();
      if (v.token) { v.tokens = [v.token]; delete v.token; }   // accounts from before passwords existed
      v.name = name; v.tokens = v.tokens || [];
      if (this.players.has(key)) continue;
      this.players.set(key, v);
      if (k.slice(2) !== key) { this.dirty.add(k); this.mark('p:', key); }
    }
    this.fails = new Map();    // lower-case name -> { n, until }: slows down password guessing
  }

  time() { return (this.info.t0 + (Date.now() - this.info.epoch) / 600000) % 1; }
  online() { let n = 0; for (const c of this.clients.values()) if (c.name) n++; return n; }
  status() { return { name: this.info.name, online: this.online(), max: MAX_PLAYERS, proto: SERVER_PROTO }; }

  // a new socket; send(text) sends to it, close() closes it
  connect(send, close) {
    const c = { id: 'p' + (++this.seq) + Math.random().toString(36).slice(2, 6), send, close, name: null, st: null, mobs: [],
      msgT: Date.now(), msgN: 0, chatT: 0 };
    this.clients.set(c.id, c);
    return c;
  }

  async disconnect(c) {
    if (!this.clients.has(c.id)) return;
    this.clients.delete(c.id);
    if (c.name) this.broadcast({ t: 'left', id: c.id }, c), this.broadcast({ t: 'chat', text: c.name + ' left the game', color: '#ffff55' }, c);
    await this.flush(true);
  }

  out(c, msg) { try { c.send(JSON.stringify(msg)); } catch (e) { } }
  broadcast(msg, except) { const s = JSON.stringify(msg); for (const o of this.clients.values()) if (o !== except && o.name) try { o.send(s); } catch (e) { } }
  near(a, b) { return a.st && b.st && Math.abs(a.st[0] - b.st[0]) < NEAR && Math.abs(a.st[2] - b.st[2]) < NEAR; }

  mark(prefix, key) { this.dirty.add(prefix + key); }
  async flush(force) {
    if (!this.dirty.size || (!force && Date.now() - this.lastFlush < 5000)) return;
    this.lastFlush = Date.now();
    const keys = [...this.dirty]; this.dirty.clear();
    for (const k of keys) {
      const p = k.slice(0, 2), key = k.slice(2);
      let v;
      if (p === 'c:') { const m = this.chunks.get(key); v = m && m.size ? [...m].flat() : undefined; }
      else if (p === 'm:') v = this.meta.get(key);
      else if (p === 'h:') v = this.chests.get(key);
      else if (p === 'r:') v = this.crops.get(key);
      else if (p === 'p:') v = this.players.get(key);   // an old mixed-case key comes back undefined and is deleted
      if (v === undefined) await this.store.delete(k); else await this.store.put(k, v);
    }
  }

  setBlock(x, y, z, id, meta) {
    const cx = x >> 4, cz = z >> 4, key = cx + ',' + cz, k = x + ',' + y + ',' + z;
    let m = this.chunks.get(key); if (!m) { m = new Map(); this.chunks.set(key, m); }
    const old = m.get(((y * 16 + (z & 15)) * 16) + (x & 15));
    m.set(((y * 16 + (z & 15)) * 16) + (x & 15), id); this.mark('c:', key);
    if (meta) { this.meta.set(k, meta); this.mark('m:', k); }
    else if (this.meta.has(k) && id === 0) { this.meta.delete(k); this.mark('m:', k); }
    if (old === CHEST && id !== CHEST && this.chests.has(k)) { this.chests.delete(k); this.mark('h:', k); }
    if (id === WHEAT_YOUNG && !this.crops.has(k)) { this.crops.set(k, Date.now() + 60000 + Math.random() * 120000); this.mark('r:', k); }
    if (id !== WHEAT_YOUNG && this.crops.has(k)) { this.crops.delete(k); this.mark('r:', k); }
  }

  growCrops() {
    const now = Date.now(); if (now - this.lastCrops < 1000) return; this.lastCrops = now;
    const ripe = [];
    for (const [k, t] of this.crops) if (now >= t) ripe.push(k);
    if (!ripe.length) return;
    const l = [];
    for (const k of ripe) { this.crops.delete(k); this.mark('r:', k); const [x, y, z] = k.split(',').map(Number); this.setBlock(x, y, z, WHEAT, 0); l.push([x, y, z, WHEAT, 0]); }
    this.broadcast({ t: 'b', l });
  }

  async message(c, text) {
    // flood protection: at most ~40 messages a second per player
    const now = Date.now();
    if (now - c.msgT > 1000) { c.msgT = now; c.msgN = 0; }
    if (++c.msgN > 40) return;
    let d; try { d = JSON.parse(text); } catch (e) { return; }
    if (!d || typeof d.t !== 'string') return;
    if (!c.name) { if (d.t === 'hello' && !c.busy) { c.busy = true; try { await this.hello(c, d); } finally { c.busy = false; } } return; }
    switch (d.t) {
      case 'st': {
        if (Array.isArray(d.s) && d.s.length >= 10) c.st = d.s.map(Number);
        c.mobs = Array.isArray(d.m) ? d.m.slice(0, 60) : [];
        // reply with everyone and every mob near this player
        const pl = [], mobs = [];
        for (const o of this.clients.values()) {
          if (o === c || !o.name || !this.near(c, o)) continue;
          pl.push([o.id, o.name, ...o.st]);
          for (const m of o.mobs) if (Math.abs(m[2] - c.st[0]) < NEAR && Math.abs(m[4] - c.st[2]) < NEAR) mobs.push([o.id, ...m]);
        }
        this.out(c, { t: 'tick', time: this.time(), pl, mobs, online: this.online(), w: this.wx && this.wx.until > now ? this.wx.k : undefined });
        break;
      }
      case 'b': {
        if (!Array.isArray(d.l)) return;
        const l = [];
        for (const e of d.l.slice(0, 2000)) {
          if (!Array.isArray(e)) continue;
          const [x, y, z, id, meta] = e;
          if (![x, y, z, id].every(Number.isInteger) || y < 0 || y > 127 || id < 0 || id > 255 || (id >= 100 && id < 200)) continue;
          const mt = typeof meta === 'string' && meta.length < 8 ? meta : 0;
          this.setBlock(x, y, z, id, mt); l.push([x, y, z, id, mt]);
        }
        if (l.length) this.broadcast({ t: 'b', l }, c);
        break;
      }
      case 'chest':
        if (typeof d.k !== 'string' || !Array.isArray(d.c)) return;
        this.chests.set(d.k, d.c.slice(0, 27)); this.mark('h:', d.k);
        this.broadcast({ t: 'chest', k: d.k, c: d.c.slice(0, 27) }, c);
        break;
      case 'me': {
        const p = this.players.get(c.key); if (p && d.p && typeof d.p === 'object') { p.save = d.p; this.mark('p:', c.key); }
        break;
      }
      case 'chat': {
        const t = String(d.raw || '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, 120);
        if (!t || now - c.chatT < 700) return; c.chatT = now;
        if (this.isAdmin(c)) this.broadcast({ t: 'chat', text: '<[Admin] ' + c.name + '> ' + t, color: '#ffb0ff' }, c);
        else this.broadcast({ t: 'chat', text: '<' + c.name + '> ' + t }, c);
        break;
      }
      case 'hit': case 'hurt': case 'loot': {
        // messages for one player (e.g. "you hit my mob", "my zombie hit you")
        const o = this.clients.get(d.to); if (!o || !o.name) return;
        d.from = c.id; delete d.to; this.out(o, d);
        break;
      }
      case 'skin': { // your skin (a preset name or a small PNG) - everyone near you gets it
        if (typeof d.d !== 'string' || d.d.length > 65000 || !/^(preset:[a-z]{1,16}|data:image\/png;base64,[A-Za-z0-9+\/=]+)$/.test(d.d)) return;
        c.skin = d.d;
        const s = JSON.stringify({ t: 'skin', id: c.id, d: d.d });
        for (const o of this.clients.values()) if (o !== c && o.name) try { o.send(s); } catch (e) { }
        break;
      }
      case 'getskin': {
        const o = this.clients.get(d.id); if (o && o.skin) this.out(c, { t: 'skin', id: o.id, d: o.skin });
        break;
      }
      case 'fx': {
        const s = JSON.stringify(Object.assign(d, { from: c.id }));
        for (const o of this.clients.values()) if (o !== c && o.name && this.near(c, o)) try { o.send(s); } catch (e) { }
        break;
      }
      case 'admin': await this.admin(c, d); break;
    }
    this.growCrops();
    await this.flush(false);
  }

  // ---- the admin panel: only accounts in ADMINS (with a password) can do these ----
  isAdmin(c) { const p = c.key && this.players.get(c.key); return !!(p && p.hash && ADMINS.includes(c.key)); }
  async admin(c, d) {
    const say = text => this.out(c, { t: 'chat', text, color: '#ff8cff' });
    if (!this.isAdmin(c)) return say('Only admins can do that.');
    const target = typeof d.id === 'string' ? this.clients.get(d.id) : null;
    switch (d.a) {
      case 'list':
        this.out(c, { t: 'adminlist', l: [...this.clients.values()].filter(o => o.name).map(o => [o.id, o.name, ...(o.st ? o.st.slice(0, 3).map(v => Math.round(v)) : [0, 0, 0])]),
          banned: [...this.players.values()].filter(p => p.banned).map(p => p.name) });
        break;
      case 'kick': case 'ban': {
        if (!target || !target.name || target === c || ADMINS.includes(target.key)) return say('You can not ' + d.a + ' that player.');
        if (d.a === 'ban') { const p = this.players.get(target.key); if (p) { p.banned = true; this.mark('p:', target.key); await this.flush(true); } }
        this.out(target, { t: 'kicked', why: d.a === 'ban' ? 'You were banned from this server by an admin.' : 'You were kicked from the server by an admin.' });
        setTimeout(() => { try { target.close(); } catch (e) { } }, 300);
        this.broadcast({ t: 'chat', text: target.name + ' was ' + (d.a === 'ban' ? 'banned' : 'kicked') + ' by an admin.', color: '#ff5555' });
        break;
      }
      case 'unban': {
        const key = String(d.name || '').toLowerCase(), p = this.players.get(key);
        if (p && p.banned) { delete p.banned; this.mark('p:', key); await this.flush(true); say(p.name + ' is not banned any more.'); }
        break;
      }
      case 'say': {
        const t = String(d.text || '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, 160);
        if (t) this.broadcast({ t: 'chat', text: '[ADMIN] ' + t, color: '#ff55ff' });
        break;
      }
      case 'time': {
        const v = Number(d.v); if (!(v >= 0 && v < 1)) return;
        this.info.t0 = v; this.info.epoch = Date.now(); await this.store.put('world', this.info);
        this.broadcast({ t: 'chat', text: 'An admin changed the time.', color: '#ff8cff' });
        break;
      }
      case 'weather': // lasts 10 minutes, then the normal weather comes back
        this.wx = ['rain', 'thunder', 'clear'].includes(d.k) ? { k: d.k, until: Date.now() + 600000 } : null;
        break;
      case 'bring': // pull a player over to you
        if (target && target.name && c.st) this.out(target, { t: 'tpme', p: c.st.slice(0, 3) });
        break;
    }
  }

  // ---- cloud saves (HTTP): /api/login, /api/cloud ... ----
  // req = { method, path, query: URLSearchParams, header(name), json(), bytes() }  ->  { status, json } or { status, bytes }
  async api(req) {
    const bad = (status, why) => ({ status, json: { error: why } });
    try {
      if (req.path === '/api/login' && req.method === 'POST') {
        let d; try { d = await req.json(); } catch (e) { return bad(400, 'Bad request.'); }
        if (!d || typeof d !== 'object') return bad(400, 'Bad request.');
        const a = await this.auth({ name: d.name, pass: d.pass, token: d.token, signup: !!d.signup, v: SERVER_PROTO });
        if (a.why) return bad(a.code === 'badpass' || a.code === 'needpass' ? 401 : 400, a.why);
        if (!a.p.hash) return bad(400, 'This name has no password yet. Sign up to give it one.');
        await this.flush(true);
        return { status: 200, json: { ok: true, name: a.p.name } };
      }
      // everything else needs a logged-in computer: x-bc-name + x-bc-token
      const key = String(req.header('x-bc-name') || '').trim().toLowerCase(), token = String(req.header('x-bc-token') || '');
      const p = this.players.get(key);
      if (!p || !p.hash || token.length < 8 || !p.tokens.includes(token)) return bad(401, 'Please log in again.');
      const index = async () => (await this.store.get('wl:' + key)) || [];
      if (req.path === '/api/cloud' && req.method === 'GET') {
        return { status: 200, json: { name: p.name, save: (await this.store.get('a:' + key)) || {}, worlds: await index() } };
      }
      if (req.path === '/api/cloud/ach' && req.method === 'POST') { // merge achievements, biomes and PvP keys from this computer
        let d; try { d = await req.json(); } catch (e) { return bad(400, 'Bad request.'); }
        const cur = (await this.store.get('a:' + key)) || {};
        const merge = (a, b, pick) => { const o = { ...(a || {}) }; if (b && typeof b === 'object') for (const k of Object.keys(b).slice(0, 300)) if (typeof k === 'string' && k.length < 40) o[k] = k in o ? pick(o[k], b[k]) : b[k]; return o; };
        const out = {
          ach: merge(cur.ach, d.ach, (x, y) => Math.min(+x || Date.now(), +y || Date.now())),
          biomes: merge(cur.biomes, d.biomes, x => x),
          keys: merge(cur.keys, d.keys, x => x), // first one wins: it is the key that owns that PvP name
        };
        if (JSON.stringify(out).length > 60000) return bad(413, 'Too much data.');
        await this.store.put('a:' + key, out);
        return { status: 200, json: out };
      }
      const id = String(req.query.get('id') || '');
      if (!/^[A-Za-z0-9_\-]{1,64}$/.test(id)) return bad(400, 'Bad world id.');
      const list = await index(), i = list.findIndex(w => w.id === id);
      if (req.path === '/api/cloud/world' && req.method === 'GET') {
        if (i < 0) return bad(404, 'That world is not in the cloud.');
        const parts = []; let n = 0;
        for (let k = 0; k < list[i].parts; k++) { const b = await this.store.getBin('wd:' + key + ':' + id + ':' + k); if (!b) return bad(500, 'Part of that world is missing.'); parts.push(b); n += b.length; }
        const all = new Uint8Array(n); let o = 0; for (const b of parts) { all.set(b, o); o += b.length; }
        return { status: 200, bytes: all };
      }
      if (req.path === '/api/cloud/world' && req.method === 'POST') { // the world file (gzipped JSON), info in x-bc-info
        let info; try { info = JSON.parse(decodeURIComponent(req.header('x-bc-info') || '')); } catch (e) { return bad(400, 'Bad world info.'); }
        if (!info || typeof info !== 'object' || JSON.stringify(info).length > 2000) return bad(400, 'Bad world info.');
        if (i < 0 && list.length >= MAX_CLOUD_WORLDS) return bad(413, 'Your cloud is full (' + MAX_CLOUD_WORLDS + ' worlds). Delete one first.');
        const data = await req.bytes();
        if (!data.length || data.length > MAX_CLOUD_BYTES) return bad(413, 'That world is too big for the cloud.');
        const parts = Math.ceil(data.length / CLOUD_PART);
        for (let k = 0; k < parts; k++) await this.store.putBin('wd:' + key + ':' + id + ':' + k, data.slice(k * CLOUD_PART, (k + 1) * CLOUD_PART));
        const old = i >= 0 ? list[i].parts : 0;
        for (let k = parts; k < old; k++) await this.store.delete('wd:' + key + ':' + id + ':' + k);
        const entry = { ...info, id, parts, size: data.length, cloudTime: Date.now() };
        if (i >= 0) list[i] = entry; else list.push(entry);
        await this.store.put('wl:' + key, list);
        return { status: 200, json: { ok: true, world: entry } };
      }
      if (req.path === '/api/cloud/delete' && req.method === 'POST') {
        if (i >= 0) { for (let k = 0; k < list[i].parts; k++) await this.store.delete('wd:' + key + ':' + id + ':' + k); list.splice(i, 1); await this.store.put('wl:' + key, list); }
        return { status: 200, json: { ok: true } };
      }
      return bad(404, 'Not found.');
    } catch (e) { console.log('api error', e); return bad(500, 'The server had a problem: ' + (e.message || e)); }
  }

  // checks a name + password (or a computer that logged in before). Returns { p, key } or { why, code }.
  async auth(d) {
    const deny = (why, code) => ({ why, code });
    const name = String(d.name || '').trim(), token = String(d.token || ''), pass = typeof d.pass === 'string' ? d.pass : '';
    if (!NAME_RE.test(name)) return deny('Pick a name with only letters, numbers, spaces, _ or - (up to 16).');
    if (token.length < 8 || token.length > 64) return deny('Bad login token.');
    const key = name.toLowerCase(), now = Date.now();
    const f = this.fails.get(key);
    if (f && f.until > now) return deny('Too many wrong passwords for ' + name + '. Wait a minute and try again.');
    const failed = () => { const g = this.fails.get(key) || { n: 0, until: 0 }; g.n++; if (g.n >= 5) { g.n = 0; g.until = now + 60000; } this.fails.set(key, g); };
    let p = this.players.get(key);
    if (d.signup && d.v !== 1) {
      if (pass.length < 6 || pass.length > 64) return deny('Your password must be 6 to 64 characters long.');
      // a name is free, or it is an old name without a password that this same computer used before
      if (p && (p.hash || !p.tokens.includes(token))) return deny('The name ' + p.name + ' is already taken. Pick a different name.', 'taken');
      const salt = randomHex(16);
      if (!p) { p = { name, save: null, first: now, tokens: [] }; this.players.set(key, p); }
      p.salt = salt; p.iter = HASH_ITER; p.hash = await hashPass(pass, salt);
      remember(p, token); this.mark('p:', key);
    } else if (pass) {
      if (!p) return deny('There is no account called ' + name + '. Click Sign Up to make one.', 'noacct');
      if (!p.hash) {
        if (!p.tokens.includes(token)) { failed(); return deny('The name ' + p.name + ' belongs to another player.'); }
        return deny('This name has no password yet. Click Sign Up (on this computer) to give it one.', 'nopass');
      }
      if (await hashPass(pass, p.salt, p.iter || HASH_ITER) !== p.hash) { failed(); return deny('Wrong password for ' + p.name + '.', 'badpass'); }
      this.fails.delete(key); remember(p, token); this.mark('p:', key);
    } else {
      if (!p) return deny(d.v === 1 ? 'Update BlockCraft to sign up for an account.' : 'There is no account called ' + name + '. Click Sign Up to make one.', 'noacct');
      if (!p.tokens.includes(token)) return deny(p.hash ? 'Type your password to log in on this computer.' : 'The name ' + p.name + ' belongs to another player.', 'needpass');
    }
    return { p, key };
  }

  // Sign up:  { t:'hello', signup:true, name, pass, token }  -> makes the account (the name is then taken forever)
  // Log in:   { t:'hello', name, pass, token }                 -> checks the password and remembers this computer
  //           { t:'hello', name, token }                       -> a computer that logged in before
  async hello(c, d) {
    const deny = (why, code) => { this.out(c, { t: 'deny', why, code }); setTimeout(() => { try { c.close(); } catch (e) { } }, 300); };
    if (d.v !== SERVER_PROTO && d.v !== 1) return deny('This server needs a newer BlockCraft. Update your game.');
    if (this.online() >= MAX_PLAYERS) return deny('The server is full (' + MAX_PLAYERS + '/' + MAX_PLAYERS + ' players). Try again later.');
    const a = await this.auth(d);
    if (a.why) return deny(a.why, a.code);
    const p = a.p, key = a.key;
    if (p.banned) return deny('You are banned from this server.', 'banned');
    for (const o of [...this.clients.values()]) if (o.key === key) {
      // the same player again (e.g. rejoined quickly or from another computer): drop the old connection
      this.clients.delete(o.id); this.broadcast({ t: 'left', id: o.id }, o); try { o.close(); } catch (e) { }
    }
    c.name = p.name; c.key = key;
    const name_ = p.name;
    // the world can be big, so block changes go in parts after the welcome
    const all = [...this.chunks].map(([k, m]) => { const [cx, cz] = k.split(',').map(Number); return [cx, cz, [...m].flat()]; });
    const parts = []; let cur = [], size = 0;
    for (const ch of all) { cur.push(ch); size += ch[2].length * 4 + 20; if (size > 300000) { parts.push(cur); cur = []; size = 0; } }
    if (cur.length) parts.push(cur);
    this.out(c, { t: 'welcome', id: c.id, server: this.info.name, name: name_, account: !!p.hash, you: p.save, parts: parts.length, online: this.online(),
      world: { seed: this.info.seed, name: this.info.name, mode: this.info.mode, mods: [], meta: [...this.meta], chests: [...this.chests], time: this.time(), spawn: null, clock: 0 } });
    for (const part of parts) this.out(c, { t: 'mods', mods: part });
    this.broadcast({ t: 'chat', text: name_ + ' joined the game', color: '#ffff55' }, c);
  }
}

// passwords are never stored, only a salted PBKDF2 hash of them
const HASH_ITER = 10000; // kept modest: Cloudflare's free plan allows ~10 ms of CPU per message
async function hashPass(pass, salt, iter = HASH_ITER) {
  const enc = new TextEncoder();
  const k = await crypto.subtle.importKey('raw', enc.encode(pass), 'PBKDF2', false, ['deriveBits']);
  const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', salt: enc.encode(salt), iterations: iter, hash: 'SHA-256' }, k, 256);
  return [...new Uint8Array(bits)].map(b => b.toString(16).padStart(2, '0')).join('');
}
function randomHex(n) { return [...crypto.getRandomValues(new Uint8Array(n))].map(b => b.toString(16).padStart(2, '0')).join(''); }
function remember(p, token) { p.tokens = p.tokens.filter(t => t !== token); p.tokens.push(token); if (p.tokens.length > 10) p.tokens.shift(); }

function pairs(flat) { const out = []; for (let i = 0; i + 1 < flat.length; i += 2) out.push([flat[i], flat[i + 1]]); return out; }
