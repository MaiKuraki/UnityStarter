# Abilities Integration (Ability Icon Provider)

Map your ability IDs to asset locations and let `AbilityIconProvider` load/cache `Sprite` icons.

```csharp
using CycloneGames.AssetManagement;
using CycloneGames.AssetManagement.Integrations.Abilities;

// Build a mapper (could be from a config table)
string MapAbilityIdToLocation(string id) => $"Assets/Icons/Abilities/{id}.png";

var provider = new AbilityIconProvider(pkg, MapAbilityIdToLocation);
var icon = provider.GetIcon("Fireball");
// ... use icon in UI
provider.Dispose(); // release cached handles when shutting down
```