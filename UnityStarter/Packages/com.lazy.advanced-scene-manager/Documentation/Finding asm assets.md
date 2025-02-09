# Finding ASM assets

You can find scenes, collections and profiles very easily, like so:

```csharp
var profile = Profile.Find("name_or_id");

var scene = Scene.Find("name_path_or_id");
var gameplayScenes = Scene.Find(s => s.name.Contains("gameplay"));
var openScenes1 = SceneUtility.FindOpen("name_path_or_id");
var openScenes2 = SceneUtility.FindOpen(s => s.name.contains("gameplay"));

var collectionScene1 = SceneCollection.Find("name_or_id").Find("name_id_or_path");
var collectionScene1 = SceneCollection.Find("name_or_id").FirstOrDefault(s => s.name.Contains("gameplay")); //Linq

//Checks only active profile
var collection1 = SceneCollection.Find("name_title_or_id"); 

//Checks all profiles
var collection2 = SceneCollection.Find("name_title_or_id", activeProfile: false); 
```

> Notice: The method documentation (sandcastle comments / triple slash comments) are currently slightly wrong, as the methods were updated, but not comments. This will be fixed in a future update.
