# An overview of ASM API structure

ASM is structured into three levels:
1. Surface level
2. Intermediary level
3. Core level

<br/>

## Surface level:
The surface level is most common level to interact with ASM.

It includes:
* `SceneCollection.Open()`*, and similar.*
* `Scene.Open()`*, and similar.*
* `SceneHelper.Open()`*, and similar.*

<br/>

## Intermediary level:
The intermediary level tracks the open state of [scenes](Scenes.md) and [collections](Scene%20collections.md), and also verifies whatever a scene actually should be opened or not (ASM does not support duplicate instances of a scene).

Can be accessed using:\
`SceneManager.runtime`

The surface level API is are mostly just proxy calls to the intermediary one:

```csharp
public Scene : ASMModel
{
	...
	public SceneOperation Open() => SceneManager.runtime.Open(this);
	...
}
```

<br/>

## Core level:
This is the core of ASM. It deals with the actual loading and unloading of scenes.

`SceneOperation`

<br/>

[Scene operation](Scene%20operations.md) basically takes a list of [scenes](Scenes.md) to close, and a list of scenes to open, then closes / opens them. Scenes specified to close will always be closed before any scenes are opened (except loading screen).

Scene operation also manages loading screens and will automatically open a specified loading screen before it begins to unload scenes. If a loading screen was opened, then it will be automatically closed after scenes have been loaded.

Scenes in unity cannot be loaded in parallel, due to this scene operations will usually run in a queue, some exceptions exist (like loading screens and finish preload).

An operation can be started in code by using either of these two:\
`SceneOperation.Queue()`\
`SceneOperation.Start() //Ignores queue`

```csharp
public Scene[] scenesToOpen;
public Scene loadingScene;

public void OnButtonClick() =>
	SceneOperation.Queue().
		Open(sceneToOpen).
		Close(SceneManager.openScenes).
		With(loadingScene);
```

### Fluent API

As you can also see above, SceneOperation supports a fluent API, allowing for things like this:

```csharp

SceneManager.runtime.Open(sceneToOpen).Close(closeAlreadyOpenScene).With(loadingScreenScene);

//This is functionally equivalent to above
sceneToOpen.Open().Close(closeAlreadyOpenScene).With(loadingScreenScene);

```

> You should have a look at `.With(..)` using intellisense, or in the [api documentation](../api/Core.SceneOperation.md), it has many overloads for various different things.
