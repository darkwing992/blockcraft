// Runs the BlockCraft big server on Cloudflare (free plan): one Durable Object holds the world
// and every player's connection. Deploy with "Deploy Big Server.bat".
import { World } from './world.js';

const CORS = { 'Access-Control-Allow-Origin': '*', 'Content-Type': 'application/json' };

export class BlockCraftServer {
  constructor(state) {
    this.state = state;
    const s = state.storage;
    this.world = new World({
      get: k => s.get(k),
      put: (k, v) => s.put(k, v),
      delete: k => s.delete(k),
      list: prefix => s.list({ prefix }),
    });
    state.blockConcurrencyWhile(() => this.world.load());
  }

  async fetch(req) {
    const url = new URL(req.url);
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
}

export default {
  fetch(req, env) {
    if (req.method === 'OPTIONS') return new Response(null, { headers: { ...CORS, 'Access-Control-Allow-Methods': 'GET', 'Access-Control-Allow-Headers': '*' } });
    const stub = env.WORLD.get(env.WORLD.idFromName('main'));
    return stub.fetch(req);
  },
};
