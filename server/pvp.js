// The PvP duel server: players wait in a queue (or use a private code to find a friend), get paired
// up 1v1, and the server passes their moves and hits to each other and keeps the score.
// Each duel is first to 3 round wins (best of 5). Used by worker.js (Cloudflare) and local.mjs (this PC).
const ROUNDS_TO_WIN = 3;
const RELAY = new Set(['s', 'hit', 'fx', 'chat', 'skin']);
const MAPS = 4; // arenas: classic, sky islands, nether pit, forest ruins
// Ranked: random matches ("Find a Match") change both players' rating (Elo, everyone starts at 1000).
// Duels with a secret code are just for fun and don't count. Leaving a ranked duel counts as a loss.
// A name belongs to the first browser that plays with it (it gets a secret key), so nobody can play as you.
const START_RATING = 1000, K = 32, TOP = 50;
const newKey = () => { let k = ''; for (let i = 0; i < 24; i++) k += 'abcdefghijklmnopqrstuvwxyz0123456789'[Math.floor(Math.random() * 36)]; return k; };

export class Arena {
  // store (optional) = { list(prefix) -> Map, put(key, value) } to keep ratings when the server restarts
  constructor(store) {
    this.clients = new Set(); this.queue = []; this.matches = new Map(); this.seq = 0;
    this.store = store || null; this.players = new Map(); // lowercase name -> { name, key, rating, w, l }
    this.ready = this.load();
  }
  async load() {
    if (!this.store) return;
    try { const m = await this.store.list('pvp:'); for (const [k, v] of m) if (v && typeof v === 'object' && v.key) this.players.set(k.slice(4), v); }
    catch (e) { console.log('could not load PvP ratings', e); }
  }
  save(r) { if (this.store) Promise.resolve(this.store.put('pvp:' + r.name.toLowerCase(), r)).catch(e => console.log('could not save PvP rating', e)); }
  status() { return { online: this.clients.size, waiting: this.queue.length, duels: this.matches.size }; }
  ranked() { return [...this.players.values()].filter(r => r.w + r.l > 0).sort((a, b) => b.rating - a.rating || b.w - a.w); }
  rankOf(r) { const i = this.ranked().indexOf(r); return i < 0 ? 0 : i + 1; }
  me(r) { return r ? { name: r.name, rating: r.rating, w: r.w, l: r.l, rank: this.rankOf(r) } : null; }
  leaderboard(name) {
    const list = this.ranked();
    const you = name ? this.players.get(String(name).toLowerCase()) : null;
    return { top: list.slice(0, TOP).map((r, i) => ({ rank: i + 1, name: r.name, rating: r.rating, w: r.w, l: r.l })), players: list.length, you: this.me(you) };
  }

  connect(send, close) {
    const c = { id: ++this.seq, name: null, match: null, side: 0, code: '', close,
      send: o => { try { send(JSON.stringify(o)); } catch (e) { } } };
    this.clients.add(c);
    return c;
  }

  message(c, raw) {
    if (typeof raw !== 'string' || raw.length > 70000) return; // skins can be a few KB
    let d; try { d = JSON.parse(raw); } catch (e) { return; }
    if (!d || typeof d.t !== 'string') return;
    if (RELAY.has(d.t)) { // goes straight to your opponent
      const m = c.match; if (!m || m.state === 'end') return;
      if (d.t === 'chat') d.text = String(d.text || '').slice(0, 120);
      if (d.t === 'skin' && (typeof d.d !== 'string' || d.d.length > 65000)) return;
      m.p[1 - c.side].send(d);
      return;
    }
    switch (d.t) {
      case 'hello':
        c.name = String(d.name || 'Player').replace(/[^\w \-]/g, '').slice(0, 16) || 'Player';
        { const id = c.name.toLowerCase(); let r = this.players.get(id);
          if (!r) { r = { name: c.name, key: newKey(), rating: START_RATING, w: 0, l: 0 }; this.players.set(id, r); this.save(r); c.send({ t: 'key', name: c.name, key: r.key }); d.key = r.key; }
          if (d.key === r.key) c.rec = r;
          else { c.rec = null; c.send({ t: 'note', text: 'Someone else already uses the name "' + r.name + '" on the leaderboard, so your duels with it are not ranked. Pick a different name to get your own rank!' }); } }
        c.send({ t: 'lobby', ...this.status(), me: this.me(c.rec) });
        break;
      case 'queue': { // find an opponent (only players with the same code, if one is given)
        if (!c.name || c.match) return;
        this.unqueue(c);
        c.code = String(d.code || '').toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 8);
        c.kit = String(d.kit || 'classic').replace(/[^a-z]/g, '').slice(0, 16);
        const o = this.queue.find(q => q !== c && q.code === c.code);
        if (o) { this.unqueue(o); this.start(o, c); }
        else { this.queue.push(c); c.send({ t: 'waiting', ...this.status() }); }
        break;
      }
      case 'cancel': this.unqueue(c); c.send({ t: 'lobby', ...this.status(), me: this.me(c.rec) }); break;
      case 'dead': { // you died: your opponent wins the round
        const m = c.match; if (!m || m.state !== 'fight') return;
        const w = 1 - c.side; m.score[w]++; m.state = 'between';
        if (m.score[w] >= ROUNDS_TO_WIN) { this.end(m, w, false); return; }
        m.round++;
        for (const p of m.p) p.send({ t: 'roundover', winner: w, score: m.score });
        m.timer = setTimeout(() => {
          if (m.state !== 'between') return; m.state = 'fight';
          for (const p of m.p) p.send({ t: 'round', round: m.round, score: m.score });
        }, 3500);
        break;
      }
      case 'leave': this.drop(c); c.send({ t: 'lobby', ...this.status(), me: this.me(c.rec) }); break;
      case 'top': c.send({ t: 'top', ...this.leaderboard(c.name) }); break;
      case 'ping': c.send({ t: 'pong' }); break;
    }
  }

  start(a, b) {
    const ranked = !a.code && !!a.rec && !!b.rec && a.rec !== b.rec;
    const m = { id: ++this.seq, ranked, p: [a, b], score: [0, 0], round: 1, state: 'fight', seed: Math.floor(Math.random() * 1e9), timer: null, map: Math.floor(Math.random() * MAPS) };
    a.match = b.match = m; a.side = 0; b.side = 1;
    this.matches.set(m.id, m);
    a.send({ t: 'match', ranked, oppRating: b.rec ? b.rec.rating : null, toWin: ROUNDS_TO_WIN, you: 0, opp: b.name, oppKit: b.kit, map: m.map, seed: m.seed, round: 1, score: m.score });
    b.send({ t: 'match', ranked, oppRating: a.rec ? a.rec.rating : null, toWin: ROUNDS_TO_WIN, you: 1, opp: a.name, oppKit: a.kit, map: m.map, seed: m.seed, round: 1, score: m.score });
  }
  end(m, winner, forfeit) {
    m.state = 'end'; clearTimeout(m.timer); this.matches.delete(m.id);
    let delta = 0;
    if (m.ranked) { // Elo: beating a stronger player gives more points
      const W = m.p[winner].rec, L = m.p[1 - winner].rec;
      const expect = 1 / (1 + Math.pow(10, (L.rating - W.rating) / 400));
      delta = Math.max(1, Math.round(K * (1 - expect)));
      W.rating += delta; W.w++; L.rating = Math.max(0, L.rating - delta); L.l++;
      this.save(W); this.save(L);
    }
    m.p.forEach((p, i) => {
      const rank = m.ranked ? { change: i === winner ? delta : -delta, me: this.me(p.rec) } : {};
      if (p.match === m) { p.match = null; p.send({ t: 'end', winner, score: m.score, forfeit, ranked: m.ranked, ...rank }); }
    });
  }
  unqueue(c) { const i = this.queue.indexOf(c); if (i >= 0) this.queue.splice(i, 1); }
  drop(c) { // leaving in the middle of a duel = your opponent wins
    this.unqueue(c);
    const m = c.match; if (m && m.state !== 'end') { c.match = null; this.end(m, 1 - c.side, true); }
  }
  disconnect(c) { this.drop(c); this.clients.delete(c); }
}
