# Common questions

## What is the fallback scene

The fallback scene is a scene ASM uses to simplify the scene management, unity does not allow for all scenes to be unloaded, which means we need to have a check for this every time we unload a scene, we decided it would be simpler to just keep a special scene in memory since this ensures that the scene can be unloaded, and we do not have to worry about forgetting to have a check beforehand, as ASM used to have issues with this.

The second reason is to prevent potential flickering during startup which would occur with a non-empty scene. [Startup scene](Scene%20manager%20window.md#startup-page) can be overridden in settings, this scene may also be used as fallback scene, but note that ASM could switch to regular fallback scene at any point after startup. Fallback scene will be always sorted to at the top of build settings for this reason.

> Please note that that overriding startup scene is an advanced use-case that should not be used unless actually needed, you probably want to use [startup collections](Scene%20collections.md) and [startup standalone scenes](Standalone%20scenes.md) instead.

## Build settings list

ASM will automatically manage the build list for you.

Manually modifying the build scenes list is still supported though.\
When adding a scene, ASM will add it to [standalone](Standalone%20scenes.md), and vice versa for removing a scene. Removing scenes that are contained within a collection is not supported though, and will just not work.

Scenes cannot be reordered manually.

## Event methods

Some methods in ASM are prefixed with "\_", this is due to name conflicts as [UnityEvent](https://docs.unity3d.com/Manual/UnityEvents.html) does not support methods with return values.

## Do I need to add anything to .gitignore?

ASM does not require anything to be added to git ignore. The only thing would be `<project root>/UserSettings/` but that should be ignored for unity project anyways. Default [unity .gitignore](https://github.com/github/gitignore/blob/main/Unity.gitignore) already contains it.

## Compilation issues after installing ASM

This is something we cannot really do much about. Sometimes unity compiles propertly and all works, sometimes it doesn't.

There are two ways to solve this problem:
1. Restart editor.
2. Right click on AdvancedSceneManager folder and choose 'Reimport' - sometimes this works, sometimes it doesn't.

If it still won't compile, then thats a bug, you'll need to report that to us over at:\
[Discord - Lazy Solutions](https://discord.gg/upfgXPxFnw)

## What is the difference between patches and asset store updates?

Asset store updates happens irreguarly, and has been properly tested to be stable, both by us and Unity. Validation process usually takes a few days.

Patches on the other hand have quicker turn around, we can push instantly, but may be less stable. We always test what we changed in a patch, and do our best to test ASM in general, but we do not garuantee that ASM is 100% stable. If you do get a bug though, you can just let us know, and we'll push a new patch fixing it as soon as we are able.

### Patches can be downloaded from:
#### Github
[Github releases](https://github.com/Lazy-Solutions/AdvancedSceneManager/releases/latest) hosts .unitypackage for download.

#### Inside of unity:
![](https://github.com/Lazy-Solutions/AdvancedSceneManager/blob/2.0/image/scene-manager-window-menu.png)

The checkmark in the top section of the image indicates no updates are available. But it will update depending on whatever a patch is available. Pressing it when there is no patch available will re-check for one.
