// @name Lucky Blocks
// @author BlockCraft
// @version 1.0
// @description Golden lucky blocks appear on the surface. Break one for a surprise - good or bad!

// The picture is drawn by our own function: a yellow block with a "?" on it.
const lucky = bc.addBlock('lucky_block', {
  name: 'Lucky Block',
  hardness: 0.6,
  light: 6,
  drops: [],                 // it drops nothing itself, the surprise is below
  texture: (ctx, size) => {
    const p = size / 16;     // one Minecraft pixel
    ctx.fillStyle = '#e8b923'; ctx.fillRect(0, 0, size, size);
    ctx.fillStyle = '#ffd95a'; ctx.fillRect(p, p, size - 2 * p, size - 2 * p);
    ctx.fillStyle = '#b8860b';
    ctx.fillRect(0, 0, size, p); ctx.fillRect(0, 0, p, size); ctx.fillRect(size - p, 0, p, size); ctx.fillRect(0, size - p, size, p);
    ctx.fillStyle = '#7a4a00';                 // the question mark
    for (const [x, y, w, h] of [[5,3,6,2],[10,4,2,3],[8,7,3,2],[7,8,2,2],[7,12,2,2]]) ctx.fillRect(x * p, y * p, w * p, h * p);
  }
});

// put a few lucky blocks on top of the grass when new land is made
bc.on('generate', e => {
  if (e.dimension !== 'overworld' || e.random() > 0.2) return;    // about 1 in 5 chunks
  for (let tries = 0; tries < 10; tries++) {                       // look for a grass block with air on top
    const x = Math.floor(e.random() * 16), z = Math.floor(e.random() * 16);
    let y = e.height - 2;
    while (y > 1 && e.get(x, y, z) === 0) y--;                     // the highest block in this column
    if (e.get(x, y, z) === bc.B.GRASS) { e.set(x, y + 1, z, lucky); return; }
  }
});

const goodies = [[bc.IT.DIAMOND, 3], [bc.IT.GOLDEN_APPLE, 2], [bc.IT.EMERALD, 5], [bc.IT.IRON_INGOT, 8], [bc.B.TNT, 2], [bc.IT.ENDER_PEARL, 2]];
bc.on('blockBreak', e => {
  if (e.id !== lucky) return;
  const roll = bc.random();
  if (roll < 0.6) {
    const [id, n] = goodies[Math.floor(bc.random() * goodies.length)];
    bc.give(id, n);
    bc.toast('Lucky!');
  } else if (roll < 0.8) {
    bc.spawnMob('zombie', e.x + 0.5, e.y + 1, e.z + 0.5);
    bc.toast('Unlucky... a zombie!');
  } else if (roll < 0.9) {
    bc.explode(e.x + 0.5, e.y + 0.5, e.z + 0.5, 2.5);
    bc.toast('BOOM!');
  } else {
    for (let i = 0; i < 4; i++) bc.spawnMob('chicken', e.x + 0.5, e.y + 1, e.z + 0.5);
    bc.toast('Chickens!');
  }
});
