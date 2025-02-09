# Scene manager

The scene manager class is meant to act as the the core API of ASM, and provides access to the most important systems within.

> There are also a lot miscellaneous static utility classes in AdvancedSceneManager.Utility that are not accessible from SceneManager.

> Note when coming from 1.9:\
> ASM 1.9 had multiple scene managers, these has been merged into one, SceneManager.runtime.

## SceneManager.assets
Provides access to ASM asset lists, which would be:
* Collections
* Scenes
* Collection templates
* Default scenes
```csharp
//Gets all level scenes
SceneManager.assets.scenes.Where(s => s.name.StartsWith("Level")).ToArray();
```

## SceneManager.openScenes
Provides access to the list of open scenes.\
Proxy for: `SceneManager.runtime.openScenes`.

## SceneManager.openCollection
Provides access to the currently open collection, null if none.\
Proxy for: `SceneManager.runtime.openCollection`.

## SceneManager.preloadedScene
Provides access to the currently preloaded scene, null if none.\
Proxy for: `SceneManager.runtime.preloadedScene`.

## SceneManager.runtime
Provides access to runtime scene management.
```csharp
public SceneCollection collection;

void OpenCollection() =>
	SceneManager.runtime.Open(collection); //Equivalent to: collection.Open();
```

## SceneManager.app
Provides access to application wide ASM stuff. Handles startup and quit.
```csharp
void OpenCollection()
{
	//Register callback that should run before quit
	SceneManager.app.RegisterQuitCallback(SaveBeforeQuit);
	//Fades the screen out, calls SaveBeforeQuit(), then quits game
	SceneManager.app.Quit(); 
}

IEnumerator SaveBeforeQuit()
{
	yield return SaveGameManager.SaveGame(); //Example save code
}
```

## SceneManager.settings
Provides access to ASM settings. 
```csharp
void ToggleWhateverCollectionShouldOpenWhenAContainedSceneIsOpened()
{
	//When a scene is opened, you can optionally have ASM open the entire collection that it is contained within. This is a user scoped / machine setting, it won't be synced to source control. Let's toggle it for fun, why not.
	var currentValue = SceneManager.settings.user.openCollectionOnSceneAssetOpen;
	SceneManager.settings.user.openCollectionOnSceneAssetOpen = !currentValue;
	SceneManager.settings.user.Save();
}
```

## SceneManager.profile
Provides access to the currently active profile, null if none.\
Equivalent to:\
`Profile.current`.

## SceneManager.isInitialized
Gets whatever ASM is initialized after a domain reload. Some api:s may fail if this is false, most notably .assets.

## SceneManager.OnInitialized(Action)
Registers a callback for when ASM is initialized after a domain reload. Callback is called immediately if ASM is already initialized.
