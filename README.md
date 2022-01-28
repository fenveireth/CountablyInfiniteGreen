# Countably Infinite Green

Mod and user content loader for "Golden Treasure - The Great Green"

Functions as a run-time patch, based on UnityDoorstop and HarmonyX

## Features

Reactivates unfinished / disabled game features:

* Loading of user content from directories in `*_Data/Resources/Mods/` (see example mod for layout)
* 'Profiles' menu to manage Savegames, select active mods, and manage mod load order
* Ingame dev console

Load user content:

* New plain text format to create Events more easily
* Folder watch + auto-reload to create Backgrounds (I'm not going to claim 'easily')
* Chainload more HarmonyX patches, to allow further changes

Unrelated fixes:

* The brightness slider in the settings menu is now actually center-notched

## Building and Installation

Requires:

* dotnet
* meson
* mingw32-gcc, targeting x86\_64-w64
* Golden Treasure

Copy the required assemblies from the game to `originals/`. Then `./mk.sh`

Resulting executable in `loader/build/` is self-contained, and to be placed in the game's root directory
