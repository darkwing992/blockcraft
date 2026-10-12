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

CONTROLS
  WASD move, mouse look (or arrow keys), Space jump/swim
  Double-tap W (or hold Shift) sprint, C sneak
  Hold left click: mine / attack
  Right click: place block, open chest, sleep in bed, equip armor, use bucket
  Hold right click: eat food / draw bow
  E inventory & crafting, Q drop item, 1-9 / scroll select, F fly (creative), F3 debug, Esc pause
  T or Enter: chat (in multiplayer)

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
