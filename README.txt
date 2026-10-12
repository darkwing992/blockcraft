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

SKINS (version 3.8)
  Title screen > Skins. Pick one of the built-in skins, or click "Import Skin (.png)" and choose a
  Minecraft Java skin file (64x64, the old 64x32, or HD - the same PNG you'd use in Minecraft;
  slim "Alex" arms are detected). Friends, big-server players and PvP opponents see your skin.

PVP DUELS (version 3.7)
  Title screen > Minigames > PvP Duels. "Find a Match" puts you against any other player who is
  waiting; or you and a friend type the same secret code and click "Duel a Friend".
  Pick a kit first: Classic, Knight, Archer, Crossbow Ranger, Berserker or Tank. The map is random:
  Classic Arena, Sky Islands (falling off = you lose the round), Nether Pit (lava!) or Forest Ruins. First to win 2 rounds wins. You can't build or break blocks in the arena.
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
