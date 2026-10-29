# TorchesOut

A RimWorld 1.7 mod that saves wood by putting out torches nobody needs:

- Indoor torches go out when the last person leaves a room, and relight when someone comes back.
- Outdoor torches go out during the day, and at night only burn while someone is near.
- Prisoners and patients are never left in the dark; any torch can be marked "Always on".

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077). Safe to add to or remove from an existing save.

## Layout

| Path | What it is |
|---|---|
| `Mod/` | The mod exactly as RimWorld loads and uploads it (About, 1.7, LoadFolders.xml, LICENSE) |
| `Source/` | C# source and project; builds into `Mod/1.7/Assemblies/` |
| `SteamDescription.txt` | Workshop page description (Steam BBCode), pasted into Steam after the first upload |

## Building

Requires the .NET 10 SDK.

```sh
cd Source
dotnet build -c Release
```

The project finds the game's DLLs in a macOS Steam install by default. Elsewhere, point it at the folder containing `Assembly-CSharp.dll`:

```sh
dotnet build -c Release -p:RimWorldManaged=/path/to/RimWorld/dll/folder/
```

## Installing for development

Link `Mod/` into RimWorld's `Mods` folder (the one beside the game, next to `Data/`):

```sh
ln -s "$PWD/Mod" ".../steamapps/common/RimWorld/Mods/TorchesOut"
```

## License

[CC0 1.0](LICENSE). RimWorld and its art belong to Ludeon Studios.
