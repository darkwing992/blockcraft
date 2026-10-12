// BlockCraft big server: one shared world for up to 100 players.
// This file has the game logic only; worker.js runs it on Cloudflare (always online) and
// local.mjs runs it on your own PC. Players' games do the 3D world, physics and their own
// nearby mobs; the server stores the world (block changes, chests, inventories), keeps the
// time of day, grows crops, and passes messages between players.
export const MAX_PLAYERS = 100;
export const SERVER_PROTO = 1;
const NEAR = 96;            // players/mobs farther away than this aren't sent to you
const WHEAT = 46, WHEAT_YOUNG = 48, CHEST = 35;
const NAME_RE = /^[A-Za-z0-9_ \-]{1,16}$/;

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
    this.players = new Map();  // name -> { token, save }
    for (const [k, v] of await this.store.list('p:')) this.players.set(k.slice(2), v);
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
      else if (p === 'p:') v = this.players.get(key);
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
    if (!c.name) { if (d.t === 'hello') await this.hello(c, d); return; }
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
        this.out(c, { t: 'tick', time: this.time(), pl, mobs, online: this.online() });
        break;
      }
      case 'b': {
        if (!Array.isArray(d.l)) return;
        const l = [];
        for (const e of d.l.slice(0, 2000)) {
          if (!Array.isArray(e)) continue;
          const [x, y, z, id, meta] = e;
          if (![x, y, z, id].every(Number.isInteger) || y < 0 || y > 127 || id < 0 || id > 99) continue;
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
        const p = this.players.get(c.name); if (p && d.p && typeof d.p === 'object') { p.save = d.p; this.mark('p:', c.name); }
        break;
      }
      case 'chat': {
        const t = String(d.raw || '').replace(/[\u0000-\u001f]/g, '').trim().slice(0, 120);
        if (!t || now - c.chatT < 700) return; c.chatT = now;
        this.broadcast({ t: 'chat', text: '<' + c.name + '> ' + t }, c);
        break;
      }
      case 'hit': case 'hurt': case 'loot': {
        // messages for one player (e.g. "you hit my mob", "my zombie hit you")
        const o = this.clients.get(d.to); if (!o || !o.name) return;
        d.from = c.id; delete d.to; this.out(o, d);
        break;
      }
      case 'fx': {
        const s = JSON.stringify(Object.assign(d, { from: c.id }));
        for (const o of this.clients.values()) if (o !== c && o.name && this.near(c, o)) try { o.send(s); } catch (e) { }
        break;
      }
    }
    this.growCrops();
    await this.flush(false);
  }

  async hello(c, d) {
    const deny = why => { this.out(c, { t: 'deny', why }); setTimeout(() => { try { c.close(); } catch (e) { } }, 300); };
    if (d.v !== SERVER_PROTO) return deny('This server needs a newer BlockCraft. Update your game.');
    const name = String(d.name || '').trim(), token = String(d.token || '');
    if (!NAME_RE.test(name)) return deny('Pick a name with only letters, numbers, spaces, _ or - (up to 16).');
    if (token.length < 8) return deny('Bad login token.');
    if (this.online() >= MAX_PLAYERS) return deny('The server is full (' + MAX_PLAYERS + '/' + MAX_PLAYERS + ' players). Try again later.');
    let p = this.players.get(name);
    if (p && p.token !== token) return deny('The name ' + name + ' belongs to another player. Pick a different name.');
    for (const o of [...this.clients.values()]) if (o.name && o.name.toLowerCase() === name.toLowerCase()) {
      if (!p) return deny('Someone called ' + name + ' is already playing.');
      // the same player again (e.g. rejoined quickly): drop the old connection
      this.clients.delete(o.id); this.broadcast({ t: 'left', id: o.id }, o); try { o.close(); } catch (e) { }
    }
    if (!p) { p = { token, save: null, first: Date.now() }; this.players.set(name, p); this.mark('p:', name); }
    c.name = name;
    // the world can be big, so block changes go in parts after the welcome
    const all = [...this.chunks].map(([k, m]) => { const [cx, cz] = k.split(',').map(Number); return [cx, cz, [...m].flat()]; });
    const parts = []; let cur = [], size = 0;
    for (const ch of all) { cur.push(ch); size += ch[2].length * 4 + 20; if (size > 300000) { parts.push(cur); cur = []; size = 0; } }
    if (cur.length) parts.push(cur);
    this.out(c, { t: 'welcome', id: c.id, server: this.info.name, name, you: p.save, parts: parts.length, online: this.online(),
      world: { seed: this.info.seed, name: this.info.name, mode: this.info.mode, mods: [], meta: [...this.meta], chests: [...this.chests], time: this.time(), spawn: null, clock: 0 } });
    for (const part of parts) this.out(c, { t: 'mods', mods: part });
    this.broadcast({ t: 'chat', text: name + ' joined the game', color: '#ffff55' }, c);
  }
}

function pairs(flat) { const out = []; for (let i = 0; i + 1 < flat.length; i += 2) out.push([flat[i], flat[i + 1]]); return out; }
