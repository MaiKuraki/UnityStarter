# Scene callbacks

## Interfaces

Sometimes you may want to run some initialization code when a scene opens, before loading screen is closed. Scene callbacks helps you do that. Scene callbacks must be implemented by a MonoBehaviour, and that script must then be attached to a scene, ASM will then invoke the script when that scene is opened. Callbacks for scene close also exist.

Collection callbacks are called for all scenes that are contained within the collection that is opened, or closed.

Open callbacks:

* `ISceneOpen` (void)
* `ISceneOpenAsync` (coroutine)
* `ICollectionOpen` (void)
* `ICollectionOpenAsync` (coroutine)

Close callbacks:

* `ISceneClose` (void)
* `ISceneCloseAsync` (coroutine)
* `ICollectionClose` (void)
* `ICollectionCloseAsync` (coroutine)

> Note that these callbacks may sometimes not be called when loading scenes outside of ASM. This is because they are invoked by ASM SceneOperation directly, and if SceneOperation is not opening the scenes, then no callbacks. Some exceptions exist.

## Order

* Loading screen open (_if one specified_)
* `ICollectionClose` (_if collection specified_)
* `ISceneClose`
* Unload scenes
* Load scenes
* Preload scene (_if one defined_)
* `ISceneOpen`
* `ICollection` open (_if collection specified_)
* Hide loading screen (_if one specified_)

## Example

```csharp
public class LogOnSceneCallback : MonoBehaviour, ISceneOpen, ISceneClose
{
    public void OnSceneOpen()
    {
        Debug.Log(this.ASMScene().name + " opened");
    }
    public void OnSceneClose()
    {
        Debug.Log(this.ASMScene().name + " closed");
    }
}

public class Promotion : MonoBehaviour, ICollectionOpenAsync, ICollectionCloseAsync
{
    //TODO: Remove when you want a promotion
    public IEnumerator OnCollectionOpen(SceneCollection collection)
    {
        yield return new WaitForSecondsRealtime(60);
    }
    public IEnumerator OnCollectionClose(SceneCollection collection)
    {
        yield return new WaitForSecondsRealtime(60);
    }
}
```

## Events

There are also several C# events, which are fire and forget, scene operation will not wait for them, you can register listeners for them anywhere.

```csharp
//Invoked by unity when editor starts / script recompile
[InitializeOnLoad]
static void OnLoad
{
	SceneManager.runtime.collectionOpened += (c) => Log(c, "opened");
	SceneManager.runtime.collectionClosed += (c) => Log(c, "closed");

	SceneManager.runtime.sceneOpened += (s) => Log(s, "opened");
	SceneManager.runtime.sceneClosed += (s) => Log(s, "closed");
	SceneManager.runtime.scenePreloaded += (s) => Log(s, "preloaded");
}

void Log(Scene scene, string action)
{
	Debug.Log($"{scene.name} {action}.");
}

void Log(SceneCollection collection, string action)
{
	Debug.Log($"{collection.title} {action}.");
}
```

# Scene operation callbacks

[Fluent scene operation API](Scene%20operations.md#fluent-api) can be used to specify callbacks in code.

```csharp

public void OpenScenesWithCallbackBeforeLoadingScreenClose(IEnumerable<Scene> scenes, Action callbackAction)
{
    //Remove null scenes
    scenes = scenes.Where(s => s);

    //Add callback after scenes have loaded, that logs to console
    SceneManager.runtime.Open(scenes).
    Callback(Callback.BeforeLoadingScreenClose().Do(callbackAction));
}

public void LogAfterAllScenesOpened(IEnumerable<Scene> scenes)
{
	//Remove null scenes
    scenes = scenes.Where(s => s);
    
	//Add callback after scenes have loaded, that logs to console
    SceneManager.runtime.Open(scenes).
    Callback(Callback.After(Phase.LoadScenes).Do(() => Debug.Log("Scenes loaded")));
}

public void OpenScenesWithDelayInbetween(IEnumerable<Scene> scenes, float delay)
{
	//Remove null scenes
	scenes = scenes.Where(s => s);

	//Add callback after scene has loaded, that delays the operation
	SceneManager.runtime.Open(scenes).
	Callback(scenes.Select(s => Callback.After(Phase.LoadScenes, s).Do(delay)));
}
```
