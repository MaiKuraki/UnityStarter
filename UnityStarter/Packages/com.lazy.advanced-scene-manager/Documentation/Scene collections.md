# Scene collections

A scene collection, *more commonly referred to as just collection*, is a collection of scenes that will all be opened at the same time.

![](../image/scene-manager-window-collections.png)

When a collection closes, all currently open non-persistent scenes will closed, unless the closing collection was opened as additive. Additive collections will only close its own scenes.

Collections support opening [loading screens](Loading%20screens.md) before opening or closing, and will, by default, use the default loading screen, which is defined in [settings](Scene%20manager%20window.md#scene-loading-page).

## Startup

There are three options for whatever a collection should open during startup.
Any collections set to **Open**, will be opened by ASM, in order.

If no collection is set to **Open**, then ASM will find the first collection in list set to **Auto**, skipping any collections set to **Do Not Open**.
