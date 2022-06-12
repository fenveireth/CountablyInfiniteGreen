# Releasing your work

You will typically be working directly in a directory in the Mods directory

The mod is identified by the name of this folder

When satisfied with your work, release it by completing the 'meta.xml' file, and
zipping the folder.
The zip archive is to be placed by the players in their Mods directory

The zip must contain one directoy at the root level, which contains the
meta.xml. See the included example mod for correct structure

## The dezip step

When starting, the game inspects all zip archives in the Mods directory

If the meta.xml is found to be different from the already-existing one (if this
is not the first game start with this mod installed), the zip is extracted,
overwriting any previously extracted version of this mod

The 'version' field in meta.xml is never read, but changing it will trigger
re-extraction

The 'targetLoaderVersion' field is the only trigger for the prompt to players
to upgrade the loader if they use an outdated one :
the game makes no network calls to check for updates

## Depending on other mods

For most raw asset types (any place in the XMLs expecting a file),
you can load files provided by another mod, by escaping
from your mod directory (ex. '../&lt;other\_mod\_name&gt;/&lt;thefile&gt;').
There's no check, it just expects the other\_mod to be present and extracted,
with no regard for it being enabled or its position in the Load Order

For XML resources that have an ID, definitions are not namespaced by mod,
providing a new definition masks the previous ones.  
If you assume a particular other\_mod has been loaded before yours in the Load
Order, there's no way to formally state it for automated checking. You will have to
make it clear to your players
