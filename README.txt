BLOCKCRAFT
==========

HOW TO PLAY (WINDOWS)
  Double-click BlockCraft.exe and press PLAY.
  The game opens in its own window. Keep the launcher open while you play (minimizing it is fine);
  it is what saves your worlds. Quit to the title screen before closing the launcher.
  Keep BlockCraft.exe, index.html, versions.json and the versions folder together.

  Your worlds are saved as files in the "saves" folder in this game folder:
      saves\<World_Name>-xxx.json        <- the world
      saves\<World_Name>-xxx.info.json   <- its name / mode / last played
  Deleted worlds are moved to saves\deleted instead of being destroyed.
  "Play BlockCraft.bat" still works too, as a backup way to start the game.

  If your antivirus warns about BlockCraft.exe: it is a brand-new program that no antivirus has
  seen before, so it has no "reputation" yet. The source code is in the launcher folder
  (Launcher.cs) and build.bat rebuilds it.

  To give a world to a friend: copy its two files into their "saves" folder,
  or use "Export" / "Import World File" on the Select World screen.

  You can also just open index.html in Chrome or Edge. Then worlds are saved in the
  browser, or click "Choose Save Folder..." and pick this game folder to save files here.

OLD VERSIONS
  Pick any version in the box above PLAY in the launcher (1.1 is the very first one).
  Old versions keep their worlds in saves\old-versions\<version>, so they can never mess up
  the worlds you made in the newest version.

UPDATES (GITHUB)
  The launcher checks GitHub for a newer BlockCraft every time it starts (or press "Updates").
  A new version is downloaded, its checksum is checked, and it is installed automatically.
  The old version stays in the versions folder so you can still play it.
  To set it up (once):
    1. Make a free account on github.com and create a new EMPTY public repository,
       for example "blockcraft" (don't add a README).
    2. Double-click "Publish to GitHub.bat" and paste the repository address.
       Git will ask you to sign in to GitHub the first time.
  After that, run "Publish to GitHub.bat" again whenever the game gets a new version.
  Friends can download the whole folder from your GitHub page (Code > Download ZIP), and their
  launcher will then get every new version automatically.
  The launcher never replaces BlockCraft.exe by itself. If there is a new launcher, a yellow
  "New launcher available" link appears at the bottom; it opens your GitHub page.

MULTIPLAYER (PLAY WITH FRIENDS)
  To host: open your world, press Esc and click "Open to Friends". You get a join code
  (like 3ZS2F). Tell your friends the code. Keep your game open while they play.
  To join: on the title screen click "Multiplayer", type your name and the code, and click Join.
  Works over the internet, no setup needed. Friends can use BlockCraft.exe, or index.html in
  Chrome/Edge (also on a Chromebook).
  - Press T (or Enter) to chat.
  - The host's game runs the world and saves it, including each friend's inventory,
    so friends get their stuff back when they join again.
  - Mobs live around the host. Only the host can sleep through the night.
  - Everyone needs the same version of BlockCraft.
  - It uses the free PeerJS service to connect. Some school or work networks block it.

BIG SERVER (ALWAYS ONLINE, UP TO 100 PLAYERS)
  One shared world that anyone with the game can join any time, even when your PC is off.
  It runs for free on Cloudflare. To set it up (once):
    1. Install Node.js from https://nodejs.org (the LTS version) if you don't have it.
    2. Double-click "Deploy Big Server.bat". A browser window opens: sign in to Cloudflare or
       make a free account, then click Allow. The first time, Cloudflare may ask you to pick a
       name for your workers.dev address.
    3. When it says Done, the game knows the server address. Run "Publish to GitHub.bat" so
       everyone's launcher gets it.
  To play: title screen > Multiplayer > Join Server.
  - Accounts: click Sign Up, pick a name and a password. The name is yours forever and nobody
    else can use it. Log in from any computer with your password; after that the computer
    remembers you. Passwords can't be reset, so remember yours. The server only keeps a
    scrambled (hashed) copy of it, never the password itself.
  - Your stuff is saved on the server under your account.
  - Mobs near you are run by your game, so everyone near each other sees the same mobs.
  - Cloudflare's free plan has a daily limit. It is plenty for a group of friends; if lots of
    people play all day it may stop until the next day (their $5/month plan removes this).
  - Chat on the big server is open to everyone who joins. Only share the game with people you know.
  Run it on your own PC instead: double-click "Run Server On This PC.bat" (needs Node.js).
  Players on your Wi-Fi join with the address it shows (like ws://192.168.1.5:8080/ws).
  The world is saved in server\server-data.json.

THE NETHER
  Lava pools sit deep in caves. Pour water next to lava (bucket) to make obsidian, and mine it
  with a diamond pickaxe. Build a frame of obsidian 4 wide and 5 tall (the middle 2x3 empty),
  then use flint and steel inside it. Stand in the purple portal to go to the Nether.
  A portal home is built for you on the other side. 1 block in the Nether = 8 in the Overworld.
  Watch out for ghasts (they shoot fireballs) and don't hit zombified piglins unless you want
  the whole group after you. Beds explode in the Nether! (The Nether is single player only.)
MOBS
  Slimes split when killed. Endermen get angry if you look right at them. Right-click a wolf
  with a bone to tame it - it follows you and fights monsters. Throw ender pearls to teleport.
  Iron golems guard villages. Squid swim in the sea (ink sacs make black dye), bats live in caves,
  rabbits hop around plains. Husks roam deserts and strays roam snowy places.

THE END
  Blazes live in the Nether and drop blaze rods (2 blaze powder each). Ender pearl + blaze powder
  = Eye of Ender. 3 quartz over 3 obsidian = 2 End Portal Frames. Place 12 frames in a ring around
  a 3x3 hole (3 on each side, no corners) and put an Eye of Ender in every frame - the End Portal
  opens. Jump in to fight the Ender Dragon! Break the End Crystals on the obsidian towers first,
  they heal the dragon. Beat it and an exit portal with the Dragon Egg appears.
  (The End is single player only.)

ADMIN PANEL (version 7.1) - only for the account "darkwing"
  Log in as darkwing, then press P in a world (or click Admin Panel in the pause menu).
  You: fly (even in survival), god mode, super speed, heal, +10 levels, switch game mode.
  World: change the time and weather, spawn any mob, remove all mobs, teleport anywhere,
  and click any item to get a full stack of it. Zombie Apocalypse: next wave, kill all zombies,
  summon The Abomination. On the big server: list of everyone online, go to them or bring them to
  you, kick, ban and unban, and send announcements. The server checks that it really is the
  darkwing account, so nobody else can kick or ban (run "Deploy Big Server.bat" once for this).

THE HD UPDATE (version 7.0)
  Every texture in the game is redone. All blocks are now 32x32 pixels instead of 16x16, so they are
  twice as sharp, with new art for everything: tree bark, stone, ores, bricks, leaves, flowers, sand,
  water (with light rippling on it), glowstone, lava, doors and more. Far-away blocks now fade smoothly
  instead of sparkling. Zombie Apocalypse zombies are completely re-textured: rotting skin with veins,
  bruises and open wounds, dark eye sockets with glowing eyes, broken teeth, ribs showing, and torn,
  bloody, muddy clothes. Animals and other mobs got sharper faces and fur, and there is a new sun and
  moon and better cracks when you break blocks.

THE HORDE UPDATE (version 6.3) - Zombie Apocalypse
  New zombies: Bloaters (huge and slow - they burst into a poison cloud when they die, stay out of
  it), Infernos (on fire, glowing in the dark - one hit sets you on fire, jump in water to put it out),
  Climbers (climb straight up walls, so walls won't stop them) and Leapers (pounce at you from far away).
  BOSS: The Abomination. A giant zombie with a boss health bar. It jumps and slams the ground (get away
  from it!), calls zombies up out of the ground, smashes through walls, and gets faster when it is
  hurt. It comes after 10 minutes in Survival (then every 15 minutes) and every 10th wave. It drops
  diamonds, golden apples, emeralds and more. The night sky is no longer red.

THE NIGHTMARE UPDATE (version 6.2) - Zombie Apocalypse only
  Glowing eyes watch you from far away in the dark. Zombies dig up out of the ground near you.
  Some "dead bodies" lying around are not dead - they jump up when you walk past. Zombies pound
  on doors, planks and barricades and smash through them. Get attacked from behind and you might
  get a jumpscare. Your flashlight flickers and dies when zombies are close, the screen goes dark
  around the edges, and sometimes you hear footsteps running up behind you... Some zombies are huge.

THE ADVENTURE UPDATE (version 6.0)
  Weather: it rains now and then, sometimes with thunderstorms and lightning (stay away!), and it
  snows in cold places. You can sleep through a thunderstorm.
  Boats: 5 planks in a U shape (P . P / P P P). Right-click water to put it down, right-click it to get
  in. W / S row, A / D turn, C gets out. Hit it to pick it back up.
  Horses live on the plains. Right-click one with an empty hand to jump on - it bucks, keep trying
  until it likes you (hearts). Then put a saddle on it (3 leather on top, 2 iron ingots below) and
  ride: it goes where you look, Shift gallops, Space jumps, C gets off. Tamed horses are saved.
  Dungeons: mossy rooms deep underground with a monster spawner and treasure chests.
  Desert temples: sandstone pyramids. Climb down the ladder in the middle to the hidden treasure room.
  Fish swim in the sea - hit one to catch it, cook it in a furnace.

ACCOUNTS AND CLOUD SAVES (version 4.8)
  Click "Log In" at the top left of the title screen. Make an account (name + password) or log in
  with the one you made for the big server - it is the same account. While you are logged in:
  - your achievements (and the biomes you found, and your PvP name) are saved in the cloud
  - every world you play is uploaded to the cloud (about once a minute, and when you quit)
  - on another computer, log in and your worlds show up in Singleplayer with a cloud mark - click Play
  "Upload All My Worlds" (on the account screen) sends the worlds already on this computer.
  Deleting a world deletes the cloud copy too. Up to 30 worlds per account.
  The cloud lives on the big server, so run "Deploy Big Server.bat" after updating.
  You can also log in from the launcher (version 4.9 / launcher 2.6): click the blue Log In button at the top
  (or the cloud on the side bar). The launcher and the game share the login (saves\account.json keeps your
  name and a login key - never your password), so logging in or out in one does it in the other.

RANKED PVP AND THE LEADERBOARD (version 4.7)
  "Find a Match" duels are ranked: everyone starts at 1000 rating, winning gives points and losing
  takes them away (beating a better player gives more). Ranks: Bronze, Silver (950), Gold (1100),
  Diamond (1250) and Champion (1400). Click "Leaderboard" on the PvP screen to see the top 50.
  Leaving a ranked duel counts as a loss. "Duel a Friend" (secret code) is just for fun and not ranked.
  The first browser that plays with a name owns it on the leaderboard, so nobody can play as you
  (use the same browser / launcher to keep your rank).

DOORS (version 4.4)
  6 planks of one kind of wood in two columns (crafting table) = 3 doors: oak, spruce or cherry.
  Place it on the ground (it needs 2 blocks of room) and right-click to open or close it.
  Closed doors keep zombies and other monsters out. Village houses have doors too.

XP, ENCHANTING AND ACHIEVEMENTS (version 4.3)
  Defeating mobs, mining coal / lapis / diamonds / quartz, smelting and trading drop green XP orbs.
  Collect them to level up (the green bar and number above your hotbar). Dying drops your XP.
  Enchanting Table: 1 book on top, diamond - obsidian - diamond in the middle, 3 obsidian at the
  bottom. Put a tool, weapon, bow or armor in it with lapis lazuli and pick an enchantment - it costs
  levels and lapis. Put bookshelves around the table (one block gap) for stronger enchantments:
  Sharpness, Efficiency, Protection, Power and Unbreaking. Enchanted items glow purple.
  Achievements pop up in the corner when you do things for the first time. See them all with the
  Achievements button on the title screen or in the pause menu.

NEW BIOMES AND MOBS (version 4.3)
  Cherry Groves (pink trees, bees and foxes), Badlands (striped terracotta cliffs and red sand) and
  Mushroom Fields (giant mushrooms, mooshrooms, and no monsters ever). Foxes also live in snowy
  forests. Smelt clay blocks into terracotta. Cherry logs make pink planks.

SKINS (version 3.8)
  Title screen > Skins. Pick one of the built-in skins, or click "Import Skin (.png)" and choose a
  Minecraft Java skin file (64x64, the old 64x32, or HD - the same PNG you'd use in Minecraft;
  slim "Alex" arms are detected). Friends, big-server players and PvP opponents see your skin.

PVP DUELS (version 3.7)
  Title screen > Minigames > PvP Duels. "Find a Match" puts you against any other player who is
  waiting; or you and a friend type the same secret code and click "Duel a Friend".
  Pick a kit first: Classic, Knight, Archer, Crossbow Ranger, Berserker or Tank. The map is random:
  Classic Arena, Sky Islands (falling off = you lose the round), Nether Pit (lava!) or Forest Ruins. First to win 3 rounds wins. You can't build or break blocks in the arena.
  PvP runs on the big server: run "Deploy Big Server.bat" once so it has the duel server too.
  (On your own Wi-Fi, "Run Server On This PC.bat" has it as well.)

ZOMBIE APOCALYPSE (version 2.7)
  On the title screen click "Minigames", then "Zombie Apocalypse". Pick a map - Dark Forest, Desert, Plains or Ruined City -
  and a mode:
  - Survival: zombies roam all the time and nights are worse. See how long you last.
  - Waves: fight wave after wave. A supply crate drops after every wave, and every 5th wave
    brings a Juggernaut. If everyone dies it's game over.
  "Play With Friends" gives you a code; up to 4 friends join with it (5 players). Friends can also
  type the code in Multiplayer > Join Friend.
  Zombies: Walkers (slow), Runners (fast and screaming), Crawlers (low, fit under gaps),
  Stalkers (almost invisible until they're right behind you), Bombers (run at you and blow up),
  Juggernauts (huge, armored, smash through walls).
  New: Hounds (zombie dogs, hunt in packs), Spitters (spit acid that burns), Screamers (shriek and call
  every zombie nearby - kill them fast!).
  Lurkers hide up in trees and on roofs, leap down and choke you with their tongue - your air runs out,
  so click fast to break free!
  If a Crawler grabs your leg you can't move: click fast (or press jump / A) to kick free! Walkers and Juggernauts break blocks to reach you.
  - L (or the Light button on phones, right stick click on a controller) = flashlight.
  - Firecrackers: right-click to throw. Walkers, Runners and Crawlers run to them instead of you.
  - Crossbow: hold right-click to load, right-click again to fire. Uses bolts first, then arrows.
  - Ammo: Wooden Arrows (2 sticks = 3), Flame Arrows (arrow + coal, sets zombies on fire),
    Bolts (iron ingot over a stick = 4), Arrows. Bows use the first arrows in your inventory.
  - Ammo crates (green boxes) are hidden around every map and on every floor of the city.
  - The Dark Forest is haunted: very tall dark trees, dead trees with cobwebs, gravestones, thick fog.
  - Abandoned campsites (tents, a fire pit, chests) are scattered around the forest, plains and desert.
    Crates and chests can hold iron, gold, diamonds, tools, armor, food and more.
  - Fix weapons: put a damaged weapon or tool in the crafting grid with its material
    (string for bows, iron for crossbows, planks / cobblestone / iron / diamond for tools).
  - Also new: Bandages (paper + string), Canned Food, Barricades, Sandbags, Concrete.
  Things you drop now show for everyone in multiplayer, and they can pick them up.

ON A PHONE (IPHONE OR ANDROID)
  Put the game online for free with GitHub Pages (once): on github.com open your blockcraft
  repository > Settings > Pages > under "Branch" pick "main" and "/ (root)" > Save. After a minute
  the game is at https://darkwing992.github.io/blockcraft/ - open that on any phone.
  - Hold the phone sideways. Left circle = walk, drag the screen = look, buttons = jump / mine /
    place / sneak / fly, top-left: pause, bag (inventory), drop, view, chat (in multiplayer).
  - In the inventory, "Move one" and "Quick move" do what right-click and shift-click do on a PC.
  - iPhone: tap the Share button > "Add to Home Screen" so it opens full screen like an app.
    Android: it goes full screen by itself; use the menu > "Add to Home screen" / "Install app".
  - Phones start with Fast graphics and a short render distance; change them in the pause menu.
  - Worlds are saved on the phone. The big server and friends' worlds work on phones too.

CONTROLLER (XBOX / PLAYSTATION)
  Plug in or connect the controller, then press A. Buttons are like Minecraft on Xbox:
  Left stick walk (click = sprint), right stick look (click = sneak), A jump (double-tap = fly in
  creative), B sneak, X drop, Y inventory, RT mine / attack, LT place / use / eat,
  LB / RB change hotbar slot, Menu = pause, View or D-pad up = change camera.
  In menus and the inventory the left stick moves a cursor: A = click, X = move one,
  RB = quick move, B = back, right stick scrolls lists. It rumbles when you get hurt.

CONTROLS
  WASD move, mouse look (or arrow keys), Space jump/swim
  Double-tap W (or hold Shift) sprint, C sneak
  Hold left click: mine / attack
  Right click: place block, open chest, sleep in bed, equip armor, use bucket
  Hold right click: eat food / draw bow
  E inventory & crafting (2x2 grid; right-click a Crafting Table for 3x3), Q drop item, 1-9 / scroll select, F fly (creative), F3 debug, Esc pause
  T or Enter: chat (in multiplayer)
  F5: change view (first person / behind you / in front of you)
  Ladders: walk into them or hold Space to climb, sneak (C) to hold still

ON A CHROMEBOOK
  Chromebooks can't run .bat files. Use one of these instead:
  1) Open the online version of the game in Chrome (the link from Claude). Worlds are saved
     in Chrome on that Chromebook.
  2) Copy this game folder to the Chromebook (e.g. Google Drive or a USB stick), then open
     index.html in Chrome. Click "Choose Save Folder..." on the Select World screen and pick
     the game folder - worlds are then saved as files in its "saves" folder.
  Chromebooks automatically start with "Fast" graphics and a shorter render distance.
  If your Chromebook has a touchscreen, tap the screen and on-screen controls appear.
  The F3 debug screen is also on the ` key (top-left of the keyboard).
