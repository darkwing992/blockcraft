// @name Fun Commands
// @author BlockCraft
// @version 1.0
// @description Chat commands: /heal /feed /day /night /tp /give /boom /spawn /where. Press T to chat.

bc.command('heal', () => { bc.heal(20); bc.say('You feel great!'); }, 'fill your hearts');
bc.command('feed', () => { bc.player.food = 20; bc.say('Yum!'); }, 'fill your hunger');
bc.command('day', () => { bc.time = 0.3; bc.say('Good morning!'); }, 'make it day');
bc.command('night', () => { bc.time = 0.75; bc.say('Watch out for monsters...'); }, 'make it night');

bc.command('tp', args => {
  const [x, y, z] = args.map(Number);
  if ([x, y, z].some(isNaN)) return bc.say('Use it like this: /tp 100 70 -40', '#f88');
  bc.player.teleport(x, y, z);
}, 'teleport: /tp x y z');

// /give diamond 5   (uses the names of blocks and items, like diamond, torch, iron_ingot, oak_door)
bc.command('give', args => {
  const name = (args[0] || '').toUpperCase(), count = +(args[1] || 1);
  const id = bc.IT[name] ?? bc.B[name];
  if (id == null) return bc.say('I don\'t know "' + args[0] + '". Try /give diamond 5', '#f88');
  bc.give(id, count);
  bc.say('Gave you ' + count + ' ' + args[0]);
}, 'get items: /give diamond 5');

bc.command('boom', () => {
  const t = bc.lookingAt();
  if (!t) return bc.say('Look at a block first!', '#f88');
  bc.explode(t.x + 0.5, t.y + 0.5, t.z + 0.5, 3);
}, 'blow up the block you look at');

bc.command('spawn', args => {
  const type = args[0];
  if (!bc.mobTypes.includes(type)) return bc.say('Mobs: ' + bc.mobTypes.join(', '), '#f88');
  const p = bc.player;
  bc.spawnMob(type, p.x + 2, p.y + 1, p.z);
}, 'spawn a mob: /spawn pig');

bc.command('where', () => {
  const p = bc.player;
  bc.say('You are at ' + Math.floor(p.x) + ' ' + Math.floor(p.y) + ' ' + Math.floor(p.z));
}, 'show your position');
