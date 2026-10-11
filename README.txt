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

CONTROLS
  WASD move, mouse look (or arrow keys), Space jump/swim
  Double-tap W (or hold Shift) sprint, C sneak
  Hold left click: mine / attack
  Right click: place block, open chest, sleep in bed, equip armor, use bucket
  Hold right click: eat food / draw bow
  E inventory & crafting, Q drop item, 1-9 / scroll select, F fly (creative), F3 debug, Esc pause

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
