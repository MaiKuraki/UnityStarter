# Loading screens

> For how to perform actions while a loading screen is open, after a scene has opened, have a look here:\
> [Callbacks](Callbacks.md)

There are three ways of opening a loading screen:

1. Opening a collection *(specified in [collection menu](Scene%20manager%20window.md#collection-popup))*
2. SceneOperation.With(Scene loadingScene)
3. LoadingScreenUtility

```csharp
public SceneCollection collection;
void OpenCollection()
{
	//Assuming collection has loading screen
	//defined, or is using default, ASM will
	//automatically open and close it.
	collection.Open();
}

void OpenCollectionWithDifferentLoadingScreen(Scene loadingScene)
{
	//Overrides any other set loading screen.
	collection.Open().With(loadingScene: loadingScene);
}

IEnumerator DoActionWithLoadingScreen(Action action, Scene loadingScene)
{
	
	var operation = LoadingScreenUtility.OpenLoadingScreen(loadingScene);
	yield return operation;
	
	action.Invoke();
	
	//Close loading screen using operation.value, which contains the
	//LoadingScreen script of the opened loading screen
	yield return LoadingScreenUtility.CloseLoadingScreen(operation.value); 
	
}

IEnumerator _DoActionWithLoadingScreen(Action action, Scene loadingScene)
{
	//Do action using built-in convinience method
	yield return LoadingScreenUtility.DoAction(loadingScene, action);
}

void _DoCoroutineWithLoadingScreen(Func<IEnumerator> coroutine, Scene loadingScene)
{
	//Same as above, but running a coroutine instead.
	//Neither DoAction requires yield return, they start on their own.
	LoadingScreenUtility.DoAction(loadingScene, coroutine);
}
```

## Custom loading screens

Custom loading screens can be created by creating a script that inherits from `AdvancedSceneManager.Callbacks.LoadingScreen` and implement `OnOpen()`, `OnClose()`, `OnProgressChanged(float progress)`.

```csharp
using System.Collections;
using AdvancedSceneManager.Callbacks;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UI;

public class ProgressBarLoadingScreen : LoadingScreen
{

    public Slider slider;
    public CanvasGroup fadeGroup;
    public float fadeDuration = 0.5f;
    
    public override IEnumerator OnOpen() =>
        FadeIn();

    public override IEnumerator OnClose()
    {

        //Hide slider before fade, since it is brighter than background and will 
        //appear to stay on screen for longer than background which looks bad
        if (slider)
            slider.gameObject.SetActive(false);

        yield return FadeOut();

    }

    public override void OnProgressChanged(float progress)
    {
        //You could also use base.operation.progress to
        //retrieve progress in Update() for example.
        if (slider)
            slider.value = progress;
    }

    IEnumerator FadeIn()
    {

        if (fadeDuration > 0)
            yield return fadeGroup.Fade(1, fadeDuration);
        else
            fadeGroup.alpha = 1;

    }

    IEnumerator FadeOut()
    {
        yield return fadeGroup.Fade(0, fadeDuration);
    }

}
```

When script is finished, place it in a dedicated scene and [assign scene as loading screen on a collection](Scene%20manager%20window.md#collection-popup) or as default loading screen in [ASM settings](Scene%20manager%20window.md#startup-page).

> Note if a custom loading screen is not showing up, you may have to modify the scene and save it *(note that scene must be dirty for it to save to disk)*, for the loading screen flag to appear.

## Default loading screens

There are several default loading screens included in ASM:
* **Fade**
* **Icon bounce**
* **Press any button**
* **Progress bar**
* **Quote**
* **Video**

These are located in\
`/AdvancedSceneManager/Defaults/Loading Screen/`

You can also access them programmatically:\
`SceneManager.assets.defaults.fadeScreen`
