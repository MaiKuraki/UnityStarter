# Netcode

This example was built on NGO(Netcode for game objects) 1.2.0, things may have changed.

### Intro

There are 2 ways to work with ASM and NGOs.
First is to make use of the Netcode built-in scene manager, this will give us
NetworkManager, Events, Scene and object synchronization, and all nice things.

The second would be to disable the built-in scene manager, this would give you a lot
more freedom and flexibility, but at the cost of having to implement everything yourself.
That means you need to make a new NetworkManager, NetworkSceneManager,
Network sync, and NetworkObject.

Unity gave us the option to disable scene management but not individual systems.
Meaning you have to make your own scene manager with their built-in functions, which
in my opinion, is NOT a custom scene manager. Alternatively, remake all from scratch, it
is entirely up to you. Rebuilding would give you the flexibility to completely take
advantage of ASM but is very time-consuming.

This example is for the first option with the netcode plugin from ASM, If you are going
for option 2, I wish you luck.

Rules to follow

1. In NGO the client syncs the scenes, meaning the ASM workflow will not be
   applied. It’s the NGO workflow that loads the scene, you will utilize ASM on the
   server side.
2. ASM still works if you wish to work with offline scenes on the client side. Like
   loading a UI pause screen during gameplay for example. Or return to the main
   menu. It’s just the netcode scenes that will be loaded by netcode and not ASM.
3. Preload, Collection loading screen, and the active scene will not be
   synchronized, you have to create the sequence yourself, an example of this can
   be found in “ConnectionManager.cs” where we have a sequence for client
   connect and host connect. And it’s simply because the client syncs the scene not
   load it themself.
4. This plugin only syncs the scenes marked as Netcode, if you wish to change this
   rule, assign a new validator to
   “networkManager.SceneManager.VerifySceneBeforeLoading”, this is the main
   method for handling which scenes sync.
5. Method marked as netcode cannot be loaded when offline.
   This project is a good start, but just an example. Feel free to use it. Feel free to
   experiment, this is not a “must do” guide.
