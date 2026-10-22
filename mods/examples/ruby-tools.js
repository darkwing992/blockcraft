// @name Ruby Tools
// @author BlockCraft
// @version 1.0
// @description Ruby ore deep underground, rubies, a full set of ruby tools and armor, and blocks of ruby.

// Ruby ore: found below y=30, needs an iron pickaxe or better. Drops a ruby.
const rubyOre = bc.addBlock('ruby_ore', {
  name: 'Ruby Ore',
  texture: { color: '#d4144f', style: 'ore' },
  hardness: 3, tool: 'pick', tier: 2,
  drops: 'ruby'                       // the name of an item this mod adds below
});
const ruby = bc.addItem('ruby', { name: 'Ruby', texture: { color: '#e0115f', style: 'gem' } });
bc.addOre(rubyOre, { veins: 3, size: 5, minY: 4, maxY: 30 });
bc.addSmelting(rubyOre, ruby);

// Tools: tier 4 is as good as diamond. The picture is an iron tool recoloured red.
const tools = {
  sword:  ['Ruby Sword',   [' R ', ' R ', ' S ']],
  pick:   ['Ruby Pickaxe', ['RRR', ' S ', ' S ']],
  axe:    ['Ruby Axe',     ['RR ', 'RS ', ' S ']],
  shovel: ['Ruby Shovel',  [' R ', ' S ', ' S ']],
};
for (const type in tools) {
  const [name, shape] = tools[type];
  const id = bc.addItem('ruby_' + type, { name, tool: { type, tier: 4 }, durability: 1800, texture: { color: '#e0115f' } });
  bc.addRecipe({ shape, key: { R: ruby, S: bc.IT.STICK }, out: id });
}

// Armor: defense points are like diamond armor
const armor = {
  helmet:     ['Ruby Helmet',     3, ['RRR', 'R R']],
  chestplate: ['Ruby Chestplate', 8, ['R R', 'RRR', 'RRR']],
  leggings:   ['Ruby Leggings',   6, ['RRR', 'R R', 'R R']],
  boots:      ['Ruby Boots',      3, ['R R', 'R R']],
};
for (const slot in armor) {
  const [name, defense, shape] = armor[slot];
  const id = bc.addItem('ruby_' + slot, { name, armor: { slot, defense }, durability: 500, texture: { color: '#e0115f' } });
  bc.addRecipe({ shape, key: { R: ruby }, out: id });
}

// A shiny block of ruby (and back again)
const rubyBlock = bc.addBlock('ruby_block', { name: 'Block of Ruby', texture: { color: '#d4144f', style: 'metal' }, hardness: 5, tool: 'pick', tier: 2 });
bc.addRecipe({ shape: ['RRR', 'RRR', 'RRR'], key: { R: ruby }, out: rubyBlock });
bc.addRecipe({ ingredients: [rubyBlock], out: ruby, count: 9 });
