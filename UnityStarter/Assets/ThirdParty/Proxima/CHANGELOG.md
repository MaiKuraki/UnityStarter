# Proxima Changelog

## Version 1.4.0

### New Features

- Add support for viewing and modifying `Materials`.
- Add support for viewing and modifying `Scriptable Objects`.
- Add support for rich text in Unity logs: <color>, <size>, <b>, and <i>.

### Fixes

- Fix more Unity 6 errors.

## Version 1.3.2

### Fixes

- Fix errors in Unity 2023.2
- Fix errors in Unity 6

## Version 1.3.1

### Fixes
- Fix Unity 2023.2 warnings.
- Disable start screen in batch mode.
- Fix float parsing breaking in some cultures.
- Fix serialization of some non-english characters.
- Fix serialization of non-int enum types.

## Version 1.3.0

### New Features:

- New inspector buttons to create, destroy, and duplicate GameObjects.
- New inspector field to add a component to a GameObject.

### Fixes:

- Fix new deprecation warnings on Unity 2023.1
- Fix compile error on Unity 2021.1 and 2021.2 for ParticleSystemForceField
- Allow any URL to access proxima to support HTTP tunnels like ngrok
- Fix an issue where Proxima would displaya no GameObjects if the first scene has no GameObjects.
- Fix an exception if the browser provides an invalide timestamp.
- Fix sharedMaterials property sending updates continuously, even when it doesn't change.
- Fix deep link parameter parsing.

## Version 1.2.1

- Added IP address to connection log.
- Improve version checking to display start screen.
- Internal changes to support packaging features separately.
- Removed stray debug log on viewing the start screen.

## Version 1.2.0

### New Features:
- **WebGL Support**: You can now open Proxima Inspector from a WebGL build! From the Proxima Connect UI, click "Open in Browser" to open Proxima in a new tab. This implementation uses a BroadcastChannel in the browser to communicate with your app, so Proxima must be running in the same browser as the Unity app.
- **Serializable Structs and Classes**: You can now view any struct or class marked as [Serializable] in the Proxima Inspector! Nested objects and arrays of objects are fully supported.
- **ProximaButton Attribute**: Add the [ProximaButton("Button Name")] attribute to MonoBehaviour methods to add a buttons in the Proxima inspector. Instructions at https://www.unityproxima.com/docs/buttons.
- **Deep Links**: New query parameters can be appended to the Proxima URL:
  - 'pass' - Automatically connect with the provided password.
  - 'page' - Navigate to page 'inspector', 'logs', or 'console' after connecting.
  - 'go' - Select a gameObject by name after connecting.
  - 's' - Set the search filter on the inspector page.
  - 'collapsed' - Collapse the navigation menu.
  - 'run' - Immediately run console command.
  - For examples, see: https://www.unityproxima.com/docs/deeplinks
- **Show Hidden Checkbox**: By default, Proxima respects hideFlags for GameObjects and Components. There is a new checkbox on the inspector page to show these hidden objects.
- **Start Screen**: Added a new start screen where you can view the latest changes, access documentation, and provide feedback.

### Changes and Fixes
 - Arrays of enums, flags, and layers can now be viewed and edited.
 - Fixed flag toggling for multi-flag values (e.g., Rigid Body Constraints).
 - Fixed some missing properties for built-in components.

## Version 1.1.0
- Added support for non-english characters in gameObject names, logs, etc.
  - Right-to-left languages are forced to display left-to-right in the browser pages to match Unity's behavior.
- Add "Run Script" button to run a sequence of commands in the console. See https://www.unityproxima.com/docs/console
- Added a button to collapse the navigation panel to just icons for smaller screens.
- Added touch-drag support for modifying numbers and arrays in the proxima inspector.
- Added an option "Set Run In Background" to Proxima Inspector to have Unity continue running when not in focus while
  Proxima is running. This is useful if you are connecting to Proxima from a browser on the same device, since the
  browser will cause Unity to lose focus.
- Prevent messages from sending when the connection is closed to avoid logged exceptions.

## Version 1.0.0
 - Initial Release