// The PvP duel server: players wait in a queue (or use a private code to find a friend), get paired
// up 1v1, and the server passes their moves and hits to each other and keeps the score.
// Each duel is best of 3 rounds. Used by worker.js (Cloudflare) and local.mjs (this PC).
const ROUNDS_TO_WIN = 2;
const RELAY = new Set(['s', 'hit', 'fx', 'chat']);

export class Arena {
  constructor() { this.clients = new Set(); this.queue = []; this.matches = new Map(); this.seq = 0; }
  status() { return { online: this.clients.size, waiting: this.queue.length, duels: this.matches.size }; }

  connect(send, close) {
    const c = { id: ++this.seq, name: null, match: null, side: 0, code: '', close,
      send: o => { try { send(JSON.stringify(o)); } catch (e) { } } };
    this.clients.add(c);
    return c;
  }

  message(c, raw) {
    if (typeof raw !== 'string' || raw.length > 4000) return;
    let d; try { d = JSON.parse(raw); } catch (e) { return; }
    if (!d || typeof d.t !== 'string') return;
    if (RELAY.has(d.t)) { // goes straight to your opponent
      const m = c.match; if (!m || m.state === 'end') return;
      if (d.t === 'chat') d.text = String(d.text || '').slice(0, 120);
      m.p[1 - c.side].send(d);
      return;
    }
    switch (d.t) {
      case 'hello':
        c.name = String(d.name || 'Player').replace(/[^\w \-]/g, '').slice(0, 16) || 'Player';
        c.send({ t: 'lobby', ...this.status() });
        break;
      case 'queue': { // find an opponent (only players with the same code, if one is given)
        if (!c.name || c.match) return;
        this.unqueue(c);
        c.code = String(d.code || '').toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 8);
        const o = this.queue.find(q => q !== c && q.code === c.code);
        if (o) { this.unqueue(o); this.start(o, c); }
        else { this.queue.push(c); c.send({ t: 'waiting', ...this.status() }); }
        break;
      }
      case 'cancel': this.unqueue(c); c.send({ t: 'lobby', ...this.status() }); break;
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
      case 'leave': this.drop(c); c.send({ t: 'lobby', ...this.status() }); break;
      case 'ping': c.send({ t: 'pong' }); break;
    }
  }

  start(a, b) {
    const m = { id: ++this.seq, p: [a, b], score: [0, 0], round: 1, state: 'fight', seed: Math.floor(Math.random() * 1e9), timer: null };
    a.match = b.match = m; a.side = 0; b.side = 1;
    this.matches.set(m.id, m);
    a.send({ t: 'match', you: 0, opp: b.name, seed: m.seed, round: 1, score: m.score });
    b.send({ t: 'match', you: 1, opp: a.name, seed: m.seed, round: 1, score: m.score });
  }
  end(m, winner, forfeit) {
    m.state = 'end'; clearTimeout(m.timer); this.matches.delete(m.id);
    for (const p of m.p) if (p.match === m) { p.match = null; p.send({ t: 'end', winner, score: m.score, forfeit }); }
  }
  unqueue(c) { const i = this.queue.indexOf(c); if (i >= 0) this.queue.splice(i, 1); }
  drop(c) { // leaving in the middle of a duel = your opponent wins
    this.unqueue(c);
    const m = c.match; if (m && m.state !== 'end') { c.match = null; this.end(m, 1 - c.side, true); }
  }
  disconnect(c) { this.drop(c); this.clients.delete(c); }
}
