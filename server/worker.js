// Runs the BlockCraft big server on Cloudflare (free plan): one Durable Object holds the world
// and every player's connection. Deploy with "Deploy Big Server.bat".
import { World } from './world.js';
import { Arena } from './pvp.js';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

export class BlockCraftServer {
  constructor(state) { this.state = state; }
  // the world is only loaded by the 'main' object; the 'pvp' object just runs duels in memory
  ensureWorld() {
    if (!this.loading) {
      const s = this.state.storage;
      this.world = new World({
        get: k => s.get(k),
        put: (k, v) => s.put(k, v),
        delete: k => s.delete(k),
        list: prefix => s.list({ prefix }),
      });
      this.loading = this.world.load();
    }
    return this.loading;
  }

  async fetch(req) {
    const url = new URL(req.url);
    if (url.pathname.startsWith('/pvp')) return this.pvp(req, url);
    await this.ensureWorld();
    if (url.pathname === '/status') return new Response(JSON.stringify(this.world.status()), { headers: CORS });
    if (req.headers.get('Upgrade') !== 'websocket') return new Response('BlockCraft server is running. Join it from the game: Multiplayer > Join Server.', { status: 200 });
    const [client, server] = Object.values(new WebSocketPair());
    server.accept();
    const c = this.world.connect(t => server.send(t), () => server.close(1000, 'bye'));
    server.addEventListener('message', e => { this.world.message(c, typeof e.data === 'string' ? e.data : '').catch(err => console.log('message error', err)); });
    const gone = () => { this.world.disconnect(c).catch(() => { }); };
    server.addEventListener('close', gone);
    server.addEventListener('error', gone);
    return new Response(null, { status: 101, webSocket: client });
  }

  pvp(req, url) {
    if (!this.arena) this.arena = new Arena();
    if (url.pathname === '/pvp/status') return new Response(JSON.stringify(this.arena.status()), { headers: CORS });
    if (req.headers.get('Upgrade') !== 'websocket') return new Response('BlockCraft PvP server is running. Join from the game: Minigames > PvP Duels.', { status: 200 });
    const [client, server] = Object.values(new WebSocketPair());
    server.accept();
    const c = this.arena.connect(t => server.send(t), () => server.close(1000, 'bye'));
    server.addEventListener('message', e => { try { this.arena.message(c, typeof e.data === 'string' ? e.data : ''); } catch (err) { console.log('pvp error', err); } });
    const gone = () => this.arena.disconnect(c);
    server.addEventListener('close', gone);
    server.addEventListener('error', gone);
    return new Response(null, { status: 101, webSocket: client });
  }
}

export default {
  fetch(req, env) {
    if (req.method === 'OPTIONS') return new Response(null, { headers: { ...CORS, 'Access-Control-Allow-Methods': 'GET', 'Access-Control-Allow-Headers': '*' } });
    const pvp = new URL(req.url).pathname.startsWith('/pvp');
    const stub = env.WORLD.get(env.WORLD.idFromName(pvp ? 'pvp' : 'main'));
    return stub.fetch(req);
  },
};
