BLOCKCRAFT MODS & SHADERS
=========================

Mods add new things to the game: blocks, items, tools, armor, recipes, ores, chat commands, and
code that reacts to what happens. Shaders change how the game looks (colours, glow, outlines...).

ADDING A MOD OR SHADER
  * Put mod files (.js) in this "mods" folder, and shader files (.glsl) in the "shaders" folder
    next to BlockCraft.exe. Restart the game and they are loaded.
  * Or in the game: title screen (or pause menu) > Mods & Shaders > "Add Mod File" / "Add Shader File".
    That also works when you play in Chrome or on a Chromebook.
  * Or make one right in the game: "Make a Mod" / "Make a Shader" opens an editor with a working example.
  * Switch mods and shaders on and off on the same screen. Mod changes need a restart (there is a
    "Restart Now" button); shaders change straight away.
  * Example mods are in mods\examples and example shaders in shaders\examples. Copy one into the
    mods (or shaders) folder to use it, or open it to see how it works.

  ONLY USE MODS FROM PEOPLE YOU TRUST. A mod is a small program and can do anything the game can do,
  including reading your login. Shaders can only change pictures, so they are always safe.
  Mods are for your own game. If you play with friends, everyone should have the same mods, or blocks
  from a mod show up as pink-and-black "Missing Block"s for the others.

-------------------------------------------------------------------------------------------------
WRITING A MOD
-------------------------------------------------------------------------------------------------
A mod is JavaScript. It starts with a few comment lines that describe it:

  // @name Ruby Tools
  // @author Your Name
  // @version 1.0
  // @description What it adds.

The rest of the file runs once when the game starts. It gets an object called `bc`:

ADDING BLOCKS
  const id = bc.addBlock('glow_rock', {
    name: 'Glow Rock',              // the name players see
    texture: ...,                   // see TEXTURES below. Or top: / side: / bottom: for different sides
    hardness: 1.5,                  // time to break (dirt 0.5, stone 1.5, obsidian 12). 0 = instant, -1 = unbreakable
    tool: 'pick',                   // the tool that breaks it fastest: 'pick', 'axe', 'shovel' or 'sword'
    tier: 2,                        // pickaxe needed for it to drop anything: 1 wood, 2 stone, 3 iron, 4 diamond
    drops: 'ruby',                  // what it drops: an item name or id, [id, count], [[id,count],[id,count]],
                                    //   a function returning one of those, or [] for nothing. Default: itself
    light: 12,                      // gives off light, 0-15 (a torch is 14)
    transparent: false,             // true = see-through (glass, leaves)
    plant: false,                   // true = drawn like a flower (two crossed pictures), no collision
    solid: true,                    // false = you can walk through it
    slippery: false,                // true = slippery like ice
    sound: 'stone',                 // 'stone', 'wood', 'grass', 'sand', 'gravel', 'glass' or 'cloth'
    creative: true,                 // false = hide it from the creative inventory
  });
  A game can hold 27 mod blocks in total (all mods together). Items have no limit.

ADDING ITEMS
  const id = bc.addItem('ruby', {
    name: 'Ruby',
    texture: ...,                   // see TEXTURES below
    stack: 64,                      // how many fit in one slot
    food: [6, 7],                   // eatable: [hunger points, saturation]   (a steak is [8, 12.8])
    tool: { type: 'pick', tier: 4 },// a tool: type 'pick', 'axe', 'shovel', 'sword' or 'hoe'; tier 1-6 (4 = diamond)
    damage: 8,                      // attack damage
    durability: 1500,               // uses before it breaks
    armor: { slot: 'chestplate', defense: 8 },  // slot: 'helmet', 'chestplate', 'leggings' or 'boots'
    fuel: 40,                       // burns in a furnace for this many seconds
    onUse: e => { ... },            // runs when you right-click with it. e.target = the block you look at (or null)
  });

TEXTURES (for blocks and items)
  { color: '#58a6ff', style: 'stone' }      block styles: plain, stone, sand, ore, planks, bricks,
                                            leaves, glass, metal, wool, glow
  { color: '#d4144f', style: 'ore', base: 'netherrack' }  ore on another block's texture (default stone)
  { color: '#e0115f', style: 'gem' }        item styles: gem, ingot, dust, ball, stick
  { color: '#e0115f', like: 'sword' }       recolour a game picture: sword, pickaxe, axe, shovel, hoe,
                                            helmet, chestplate, leggings, boots, ingot, gem, or any item
                                            name like 'apple'. Tools and armor do this by themselves.
  'stone'                                   use a block texture from the game (blocks) as it is
  'data:image/png;base64,...'               your own picture (any size, it is scaled to 32x32)
  (ctx, size) => { ... }                    draw it yourself on a size x size canvas (size is 32)

RECIPES, SMELTING, FUEL
  bc.addRecipe({ shape: [' R ', ' R ', ' S '], key: { R: ruby, S: bc.IT.STICK }, out: sword });
  bc.addRecipe({ shape: ['RR', 'RR'], key: { R: ruby }, out: block, count: 1 });   // 2x2 fits in the inventory
  bc.addRecipe({ ingredients: [ruby, 'torch'], out: lamp, count: 2 });              // shapeless
  bc.addSmelting(rubyOre, ruby);       // furnace: input -> output
  bc.addFuel(myLog, 15);               // seconds it burns
  Everywhere you need a block or item you can use its id, or a name: 'stone', 'torch', 'iron_ingot',
  'diamond', or the name of something your mod added ('ruby').

WORLD GENERATION
  bc.addOre(rubyOre, { veins: 3, size: 5, minY: 4, maxY: 30, replace: 'stone', dimension: 'overworld' });
  bc.on('generate', e => { ... })      runs for every new 16x16 piece of land (a chunk):
     e.get(x, y, z) / e.set(x, y, z, id)   x and z are 0-15 inside the chunk, y is 0-127
     e.random()                            random numbers that are the same for the same world seed
     e.dimension ('overworld', 'nether', 'end', 'sky', 'frost', 'wild', 'sun' or 'abyss'), e.x0 / e.z0 = world position of the chunk corner
  Only land that has never been visited gets new ores or blocks.

EVENTS: bc.on(name, function)
  'tick'        every frame, gets the time since the last frame in seconds
  'worldLoad'   a world was opened                          { name }
  'blockBreak'  the player broke a block                     { x, y, z, id }   return false to stop it
  'blockPlace'  the player placed a block                    { x, y, z, id }
  'hurt'        the player is about to take damage           { amount }        return false to cancel it
  'mobKill'     a mob died                                   { type, x, y, z }
  'chat'        the player said something (single player)    { text }          return false to hide it
  'generate'    new land (see above)

CHAT COMMANDS
  bc.command('heal', args => { bc.heal(20); }, 'fill your hearts');
  Players type /heal in the chat (press T). args is a list of the words after the command.
  /help lists every command from every mod.

DOING THINGS
  bc.say(text, color)          a line in the chat            bc.toast(text)     a message above the hotbar
  bc.getBlock(x, y, z)         block id at a position         bc.setBlock(x, y, z, id)
  bc.give(id, count)           put items in the inventory     bc.heal(amount)
  bc.player.x / .y / .z        position     .teleport(x, y, z)     .health / .food (0-20, can be set)
  bc.player.mode ('survival' or 'creative')   .name   .item (id of the item in your hand)
  bc.time                      time of day, 0-1 (0.3 morning, 0.5 sunset, 0.75 midnight), can be set
  bc.spawnMob(type, x, y, z)   bc.mobTypes = every mob name   bc.mobs = the mobs that are loaded
  bc.explode(x, y, z, power)   bc.lookingAt()  -> { x, y, z, id } of the block you look at, or null
  bc.random()                  bc.B / bc.IT = ids of every block / item in the game
  bc.addShader(name, code)     a mod can bring its own shader (see below)
  bc.game                      the whole game object, for advanced mods

  Something wrong? The game shows mod errors in the chat and on the Mods & Shaders screen.
  Press F12 in the game window to see the browser console for more details.

-------------------------------------------------------------------------------------------------
WRITING A SHADER
-------------------------------------------------------------------------------------------------
A shader is GLSL (the language graphics cards use). It has one function, effect(), which runs for
every pixel of the picture:

  // @name Old Photo
  // @description Brown old-photo colours.
  // @param amount 0.85 0 1          <- a slider players can change: name, start value, min, max

  vec4 effect(vec4 color, vec2 uv) {
    float l = luma(color.rgb);
    vec3 sepia = vec3(l * 1.1, l * 0.95, l * 0.72);
    return vec4(mix(color.rgb, sepia, amount), 1.0);
  }

  color        the colour of this pixel (red, green, blue, alpha from 0 to 1)
  uv           where the pixel is: (0,0) bottom left to (1,1) top right
  pixel(uv)    the colour of any other pixel           depth(uv)   how far away it is, in blocks
  luma(rgb)    how bright a colour is                  resolution  screen size in pixels
  time         seconds since the game started          daylight    0 at night, 1 in the day
  underwater   1 when your head is underwater, else 0

  Shaders run from top to bottom on the Mods & Shaders screen (use the arrow to change the order).
  If a shader has a mistake, the game shows the line and doesn't use it.
