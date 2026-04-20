# CycloneGames.GameplayTags

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

Upstream: `https://github.com/BandoWare/GameplayTags`

A production-grade **Gameplay Tags** system for Unity, inspired by Unreal Engine. Built on a pure C# engine-agnostic core with zero Unity dependencies, plus optional Unity integration layers for ECS, Burst/Jobs, and editor tooling.

## Differences vs Upstream (BandoWare/GameplayTags)

| Area | Upstream | CycloneGames |
|------|----------|-------------|
| **Thread Safety** | None | COW Snapshot + Volatile.Read/Write, lock-free reads |
| **Registration** | Assembly attributes only | 4 methods: attributes, static class, runtime dynamic, JSON files |
| **Performance** | Standard containers | 256-bit `GameplayTagMask`, `GameplayTagMaskLarge`, bitset-accelerated containers |
| **Networking** | None | `GameplayTagNetSerializer` (Full + Delta + Mask) |
| **ECS** | None | `GameplayTagMaskComponent`, `NativeGameplayTagMask`, Burst-compatible |
| **Hot-Update** | Not supported | `RegisterDynamicTag`, `RegisterDynamicTagsFromAssembly`, HybridCLR aware |
| **Tag Migration** | None | `GameplayTagRedirector` with chain flattening |
| **Build Pipeline** | None | v1 binary format with CRC32 checksum |
| **Editor** | Basic | Search, context menu, validation tool, Source Generator |
| **Object Pool** | None | `GameplayTagContainerPool`, `Pools.ListPool<T>`, `Pools.DictionaryPool<K,V>` |

## Features

### 🏗️ Architecture

| Feature | Description |
|---------|-------------|
| **Engine-Agnostic Core** | `CycloneGames.GameplayTags.Core` has `noEngineReferences: true` — pure C#, portable to any .NET runtime |
| **Copy-on-Write Snapshots** | `TagDataSnapshot` published via `Volatile.Write`, lock-free reads from any thread |
| **Three-Assembly Design** | Core (pure C#) → Unity (Burst/Jobs/ECS) → Editor (IMGUI tooling), clean dependency graph |
| **Conditional Compilation** | Unity.Collections, Burst, Entities support via `#if CYCLONE_HAS_*` — zero overhead when packages are absent |
| **Source Generator** | Compile-time constant tag references — no magic strings, no runtime typos |

### ⚡ Performance

| Feature | Description |
|---------|-------------|
| **256-bit Bitmask** | `GameplayTagMask` — blittable 32-byte struct, O(1) HasTag/HasAll/HasAny via 4 bitwise AND operations |
| **Dynamic Large Mask** | `GameplayTagMaskLarge` — auto-growing `ulong[]` for 256+ tags, still O(1) per-tag |
| **Bitset-Accelerated Containers** | `GameplayTagContainer` activates `int[]` bitset at ≥64 tags for O(1) lookups |
| **Zero-GC Enumeration** | Custom `GameplayTagEnumerator` struct, pool-backed `List<T>` for batch operations |
| **Object Pool** | `GameplayTagContainerPool` with `Pools.ListPool<T>` / `Pools.DictionaryPool<K,V>` eliminates allocation spikes |
| **Binary Build Format** | v1 versioned binary with CRC32 checksum — near-instant tag loading in builds |

### 🌐 Networking

| Feature | Description |
|---------|-------------|
| **Compact Binary Protocol** | `GameplayTagNetSerializer` — Full snapshot: `4 + 2N` bytes; Delta: `6 + 2(A+R)` bytes |
| **Delta Serialization** | Only changed tags are sent — automatic diff via `GetDiffExplicitTags()` |
| **Auto-Detect Packets** | `Deserialize()` auto-detects Full vs Delta by marker byte |
| **Mask Serialization** | 256-bit `GameplayTagMask` serialized as exactly 32 bytes via `unsafe memcpy` |
| **Framework-Agnostic** | Works with Netcode for GameObjects, Mirror, FishNet, or custom transport |

### 🧵 Thread Safety

| Feature | Description |
|---------|-------------|
| **Immutable Snapshot** | `TagDataSnapshot` — once published, never mutated; readers capture a reference and use freely |
| **ReadOnlyGameplayTagContainer** | Immutable thread-safe snapshot of container state — safe for worker threads and Jobs |
| **Lock-Free Reads** | All tag queries use `Volatile.Read` on the snapshot — no contention |
| **Tag Redirector** | `GameplayTagRedirector` uses `lock` for writes, lock-free `Dictionary.TryGetValue` for reads |

### 🎮 ECS / DOD (Conditional)

| Feature | Description |
|---------|-------------|
| **NativeGameplayTagMask** | `NativeBitArray`-based mask, Burst/Jobs compatible (`#if CYCLONE_HAS_COLLECTIONS`) |
| **GameplayTagMaskComponent** | `IComponentData` wrapper for the 256-bit `GameplayTagMask` (`#if CYCLONE_HAS_ENTITIES`) |
| **Change Detection** | `GameplayTagMaskPrevious` + `GameplayTagsDirty` (IEnableableComponent) for delta replication |
| **Main-Thread Bridge** | `CopyFrom(GameplayTagMask)`, `CopyFrom(IGameplayTagContainer)`, `CopyFrom(ReadOnlyGameplayTagContainer)` |

### 🛠️ Editor Tooling

| Feature | Description |
|---------|-------------|
| **Gameplay Tag Manager Window** | Searchable tree view with add/delete/create-child-tag context menu |
| **Inspector Tag Selector** | Popup with search bar, filtered tree, clear button — for `GameplayTag` and `GameplayTagContainer` fields |
| **Tag Validation Tool** | Scans all Prefabs, ScriptableObjects, and open scenes for invalid tag references with one-click fix |
| **Live JSON File Watching** | Automatically reloads tags when `.json` files change in `ProjectSettings/GameplayTags/` |
| **Source Generator** | Emits a static class of `GameplayTag` constants at compile time — zero-cost references in code |

---

## Assembly Structure

```
CycloneGames.GameplayTags.Core        ← Pure C# (no Unity references, allowUnsafeCode)
    ↑
CycloneGames.GameplayTags.Unity       ← Unity integration (Burst/Jobs/ECS, conditional)
    ↑
CycloneGames.GameplayTags.Editor      ← IMGUI editor windows & inspectors
    ↑
CycloneGames.GameplayTags.SourceGenerator  ← Roslyn Source Generator (compile-time)
```

## Installation

### Requirements

- Unity 2022.3+
- .NET Standard 2.1

### Optional Packages (auto-detected)

| Package | Define | Unlocks |
|---------|--------|---------|
| `com.unity.collections` ≥ 1.0.0 | `CYCLONE_HAS_COLLECTIONS` | `NativeGameplayTagMask` |
| `com.unity.burst` ≥ 1.0.0 | `CYCLONE_HAS_BURST` | `[BurstCompile]` on mask operations |
| `com.unity.entities` ≥ 1.0.0 | `CYCLONE_HAS_ENTITIES` | `GameplayTagMaskComponent` (IComponentData) |

---

## Tag Registration

Four methods are supported. All tags become available via `GameplayTagManager` after initialization.

### 1. Assembly Attributes

```csharp
[assembly: GameplayTag("Damage.Fatal")]
[assembly: GameplayTag("Damage.Miss")]
[assembly: GameplayTag("CrowdControl.Stunned")]
[assembly: GameplayTag("CrowdControl.Slow")]
```

### 2. Static Class Registration (Recommended for Hot-Update)

```csharp
using CycloneGames.GameplayTags.Runtime;

[assembly: RegisterGameplayTagsFrom(typeof(ProjectGameplayTags))]

public static class ProjectGameplayTags
{
    public const string Damage_Fatal       = "Damage.Fatal";
    public const string Damage_Miss        = "Damage.Miss";
    public const string CrowdControl_Stun  = "CrowdControl.Stunned";
}
```

The manager scans `public const string` fields and registers them at initialization. Ideal for HybridCLR hot-update workflows — tags can be consolidated in generated classes without asset edits.

### 3. Runtime Dynamic Registration

For server-driven or runtime-generated tags:

```csharp
// Single tag
GameplayTagManager.RegisterDynamicTag("Runtime.ServerBuff.SpeedBoost");

// Batch registration
GameplayTagManager.RegisterDynamicTags(new[] { "Event.Seasonal.Summer", "Event.Seasonal.Winter" });

// From a hot-loaded assembly (HybridCLR)
GameplayTagManager.RegisterDynamicTagsFromAssembly(hotLoadedAssembly);
```

### 4. JSON Files (Designer-Friendly)

Create `.json` files in `ProjectSettings/GameplayTags/`:

```json
{
  "Damage.Physical.Slash": {
    "Comment": "Damage from a slashing weapon."
  },
  "Damage.Magical.Fire": {
    "Comment": "Damage from a fire spell."
  },
  "Status.Burning": {
    "Comment": "Applied when taking fire damage over time."
  }
}
```

The editor watches for file changes and reloads automatically.

---

## Core API

### GameplayTag

The fundamental unit — a lightweight struct identified by name and resolved to a `RuntimeIndex` for O(1) operations.

```csharp
using CycloneGames.GameplayTags.Runtime;

// Request a tag (returns GameplayTag.None if not found)
GameplayTag stunTag = GameplayTagManager.RequestTag("CrowdControl.Stunned");

// Safe request
if (GameplayTagManager.TryRequestTag("CrowdControl.Stunned", out GameplayTag tag))
{
    Debug.Log($"Tag: {tag.Name}, Index: {tag.RuntimeIndex}");
}

// Hierarchy queries
bool isChild  = stunTag.IsChildOf(ccTag);        // "CrowdControl.Stunned".IsChildOf("CrowdControl") → true
bool isParent = ccTag.IsParentOf(stunTag);        // "CrowdControl".IsParentOf("CrowdControl.Stunned") → true
int  depth    = tagA.MatchesTagDepth(tagB);       // Number of matching hierarchy levels

// Properties
string name        = stunTag.Name;                // "CrowdControl.Stunned"
string label       = stunTag.Label;               // "Stunned"
int    level       = stunTag.HierarchyLevel;      // 2
bool   isLeaf      = stunTag.IsLeaf;              // true (no children)
bool   isValid     = stunTag.IsValid;             // true if registered
GameplayTag parent = stunTag.ParentTag;           // "CrowdControl"

// Span-based hierarchy access (zero allocation)
ReadOnlySpan<GameplayTag> parents  = stunTag.ParentTags;     // ["CrowdControl"]
ReadOnlySpan<GameplayTag> children = ccTag.ChildTags;        // ["CrowdControl.Stunned", "CrowdControl.Slow", ...]
ReadOnlySpan<GameplayTag> hierarchy = stunTag.HierarchyTags; // ["CrowdControl", "CrowdControl.Stunned"]
```

### GameplayTagContainer

A serializable, hierarchy-aware collection of tags. Automatically maintains implicit parent tags.

```csharp
// Create and populate
var container = new GameplayTagContainer();
container.AddTag(GameplayTagManager.RequestTag("Damage.Physical.Slash"));
container.AddTag(GameplayTagManager.RequestTag("Status.Burning"));

// Queries (hierarchy-aware: adding "Damage.Physical.Slash" implicitly adds "Damage" and "Damage.Physical")
bool hasDamage  = container.HasTag(damageTag);         // true — parent match
bool hasExact   = container.HasTagExact(slashTag);     // true — explicit only
bool hasAll     = container.HasAll(requiredTags);       // all required present?
bool hasAny     = container.HasAny(checkTags);          // any present?

// Removal
container.RemoveTag(slashTag);
container.Clear();

// Enumeration (zero-GC struct enumerator)
foreach (GameplayTag tag in container.GetExplicitTags()) { /* ... */ }
foreach (GameplayTag tag in container.GetTags())         { /* all including implicit parents */ }

// Clone / Copy
var clone = container.Clone();
container.CopyFrom(otherContainer);

// Delta computation (for networking)
container.GetDiffExplicitTags(previousContainer, addedList, removedList);
```

### GameplayTagCountContainer

Maintains per-tag reference counts with event callbacks — ideal for buff/debuff stacking in GAS (Gameplay Ability System).

```csharp
var countContainer = new GameplayTagCountContainer();

// Register callbacks BEFORE adding tags
countContainer.RegisterTagEventCallback(
    stunTag,
    GameplayTagEventType.NewOrRemoved,
    (tag, count) => Debug.Log($"{tag.Name} {(count > 0 ? "APPLIED" : "REMOVED")}")
);

countContainer.RegisterTagEventCallback(
    stunTag,
    GameplayTagEventType.AnyCountChange,
    (tag, count) => Debug.Log($"{tag.Name} stack count: {count}")
);

// Global events
countContainer.OnAnyTagNewOrRemove += (tag, count) => { /* any tag added/removed */ };
countContainer.OnAnyTagCountChange += (tag, count) => { /* any count change */ };

// Add/remove (count-tracked)
countContainer.AddTag(stunTag);      // count: 0→1, fires NewOrRemoved + AnyCountChange
countContainer.AddTag(stunTag);      // count: 1→2, fires AnyCountChange only
countContainer.RemoveTag(stunTag);   // count: 2→1, fires AnyCountChange only
countContainer.RemoveTag(stunTag);   // count: 1→0, fires NewOrRemoved + AnyCountChange

// Query counts
int explicitCount = countContainer.GetExplicitTagCount(stunTag);
int totalCount    = countContainer.GetTagCount(stunTag);  // includes hierarchy

// Cleanup
countContainer.RemoveAllTagEventCallbacks();
```

### GameplayTagQuery

Complex tag matching with nested boolean logic:

```csharp
// Simple: has ALL of these tags?
var queryAll = GameplayTagQuery.BuildQueryAll(requiredTags);
bool matches = queryAll.Matches(container);

// Simple: has ANY of these tags?
var queryAny = GameplayTagQuery.BuildQueryAny(optionalTags);

// Complex: (HasAll[Damage, Status.Burning]) AND (HasNone[Status.Immune])
var query = new GameplayTagQuery
{
    RootExpression = new GameplayTagQueryExpression
    {
        Operator = EGameplayTagQueryExprOperator.All,
        Expressions = new List<GameplayTagQueryExpression>
        {
            new() { Operator = EGameplayTagQueryExprOperator.All, Tags = damageTags },
            new() { Operator = EGameplayTagQueryExprOperator.None, Tags = immuneTags },
        }
    }
};

bool result = query.Matches(container);
```

### GameplayTagRequirements

A simple "Required + Forbidden" check pattern used in ability activation:

```csharp
var requirements = new GameplayTagRequirements
{
    m_RequiredTags  = requiredContainer,   // must have ALL of these
    m_ForbiddenTags = forbiddenContainer,  // must have NONE of these
};

// Single container check
bool canActivate = requirements.Matches(ownerTags);

// Split static + dynamic containers (e.g., inherent tags + buff tags)
bool canActivate2 = requirements.Matches(staticTags, dynamicTags);
```

### GameplayTagRedirector

Transparent tag renaming and migration:

```csharp
// Register redirects (e.g., during version migration)
GameplayTagRedirector.AddRedirect("OldTag.Damage.Fire", "Damage.Magical.Fire");

// Chains are automatically flattened: A→B→C becomes A→C
GameplayTagRedirector.AddRedirect("LegacyTag.Burn", "OldTag.Damage.Fire");
// Now "LegacyTag.Burn" resolves directly to "Damage.Magical.Fire"

// All RequestTag / TryRequestTag calls automatically apply redirects
var tag = GameplayTagManager.RequestTag("OldTag.Damage.Fire"); // → resolves to "Damage.Magical.Fire"

// Batch registration
GameplayTagRedirector.AddRedirects(new Dictionary<string, string>
{
    { "Old.A", "New.A" },
    { "Old.B", "New.B" },
});

// Query / manage
bool hasRedirect = GameplayTagRedirector.HasRedirect("OldTag.Damage.Fire");
IReadOnlyDictionary<string, string> all = GameplayTagRedirector.GetAllRedirects();
GameplayTagRedirector.RemoveRedirect("OldTag.Damage.Fire");
GameplayTagRedirector.ClearAll();
```

---

## Performance API

### GameplayTagMask (256-bit Blittable Bitmask)

A 32-byte value type for ultra-fast tag operations. Ideal for ECS, hot loops, and network serialization.

```csharp
// Create from tags
var mask = GameplayTagMask.FromTag(stunTag);
var mask2 = GameplayTagMask.FromTagWithHierarchy(stunTag); // includes all parent tags
var mask3 = GameplayTagMask.FromContainer(container);

// O(1) operations — single bitwise instruction
mask.AddTag(burnTag);
mask.RemoveTag(burnTag);
bool has     = mask.HasTag(stunTag);
bool hasHier = mask.HasTagHierarchical(stunTag); // checks children too

// O(1) bulk operations — 4 AND/OR operations
bool hasAll = mask.HasAll(requiredMask);
bool hasAny = mask.HasAny(checkMask);
bool hasNo  = mask.HasNone(immuneMask);

// Set algebra — all O(1)
var union = GameplayTagMask.Union(maskA, maskB);
var inter = GameplayTagMask.Intersection(maskA, maskB);
var diff  = GameplayTagMask.Difference(maskA, maskB);

// Iterate set bits (yields GameplayTag via RuntimeIndex)
foreach (GameplayTag tag in mask)
{
    Debug.Log(tag.Name);
}

// Low-level bit access (public)
mask.SetBit(42);
mask.ClearBit(42);
bool isSet = mask.IsSet(42);
ulong word = mask.GetWord(0); // word index 0–3
```

### GameplayTagMaskLarge (Dynamic Size)

For projects with more than 256 tag types:

```csharp
var largeMask = new GameplayTagMaskLarge(capacity: 2048);
largeMask.AddTag(someTag);
largeMask.AddTagWithHierarchy(someTag);

bool has    = largeMask.HasTag(someTag);
bool hasAll = largeMask.HasAll(otherLargeMask);
bool hasAny = largeMask.HasAny(otherLargeMask);
```

### ReadOnlyGameplayTagContainer (Thread-Safe Snapshot)

Create an immutable snapshot for worker threads, Jobs, or cross-frame caching:

```csharp
// Create snapshot (main thread)
ReadOnlyGameplayTagContainer snapshot = container.CreateSnapshot();

// Use on any thread — fully immutable
bool has    = snapshot.HasTag(stunTag);       // O(1) with bitset (≥64 tags), O(log n) otherwise
bool exact  = snapshot.HasTagExact(stunTag);  // O(log n) binary search
bool hasAll = snapshot.HasAll(otherContainer);
bool hasAny = snapshot.HasAny(otherSnapshot);

// Access raw indices (for custom processing)
ReadOnlySpan<int> implicit_ = snapshot.GetImplicitIndices();
ReadOnlySpan<int> explicit_ = snapshot.GetExplicitIndices();

// Network-ready serialization
byte[] packet = snapshot.Serialize();
```

---

## Networking

### GameplayTagNetSerializer

Framework-agnostic binary serialization for any networking layer:

```csharp
// --- Full Snapshot ---
byte[] fullPacket = GameplayTagNetSerializer.SerializeFull(container);
// Wire: [version:1][marker:0xFE][count:2][index:2 × N] = 4 + 2N bytes

// Deserialize on remote
GameplayTagNetSerializer.DeserializeFull(remoteContainer, fullPacket);

// --- Delta (only changed tags) ---
byte[] deltaPacket = GameplayTagNetSerializer.SerializeDelta(currentContainer, previousContainer);
// Wire: [version:1][marker:0xFD][addCount:2][idx...][removeCount:2][idx...] = 6 + 2(A+R) bytes

// Apply delta on remote
GameplayTagNetSerializer.ApplyDelta(remoteContainer, deltaPacket);

// --- Auto-Detect ---
GameplayTagNetSerializer.Deserialize(remoteContainer, packet);
// Automatically detects Full vs Delta by marker byte

// --- Pre-allocated buffer ---
int size = GameplayTagNetSerializer.GetFullSerializedSize(container);
byte[] buffer = new byte[size];
int written = GameplayTagNetSerializer.SerializeFull(container, buffer, offset: 0);

// --- 256-bit Mask (exactly 32 bytes, unsafe memcpy) ---
byte[] maskBuffer = new byte[32];
GameplayTagNetSerializer.SerializeMask(mask, maskBuffer, offset: 0);
GameplayTagMask remoteMask = GameplayTagNetSerializer.DeserializeMask(maskBuffer, offset: 0);
```

**Integration Example (Mirror)**:

```csharp
public class TagSyncBehaviour : NetworkBehaviour
{
    private GameplayTagContainer _serverTags = new();
    private GameplayTagContainer _previousTags = new();

    [Server]
    void UpdateTags()
    {
        byte[] delta = GameplayTagNetSerializer.SerializeDelta(_serverTags, _previousTags);
        _previousTags.CopyFrom(_serverTags);
        RpcApplyTagDelta(delta);
    }

    [ClientRpc]
    void RpcApplyTagDelta(byte[] data)
    {
        GameplayTagNetSerializer.Deserialize(_clientTags, data);
    }
}
```

---

## ECS / Burst / Jobs

> Requires `com.unity.collections`, `com.unity.burst`, and/or `com.unity.entities`. Features are enabled automatically via `versionDefines`.

### GameplayTagMaskComponent (ECS)

```csharp
// Add to entity
EntityManager.AddComponentData(entity, new GameplayTagMaskComponent
{
    Mask = GameplayTagMask.FromContainer(container)
});

// Query in a system
foreach (var (tags, entity) in SystemAPI.Query<RefRW<GameplayTagMaskComponent>>().WithEntityAccess())
{
    if (tags.ValueRO.HasTag(stunTag))
    {
        // Skip movement for stunned entities
    }

    tags.ValueRW.SetTag(burnTag);    // Add a tag
    tags.ValueRW.ClearTag(burnTag);  // Remove a tag

    bool all = tags.ValueRO.HasAll(requiredMask);
    bool any = tags.ValueRO.HasAny(checkMask);
}
```

### Change Detection for Network Replication

```csharp
// Entities also have GameplayTagMaskPrevious and GameplayTagsDirty components

foreach (var (current, previous, dirty, entity) in
    SystemAPI.Query<RefRO<GameplayTagMaskComponent>, RefRW<GameplayTagMaskPrevious>,
                    RefRW<GameplayTagsDirty>>()
    .WithEntityAccess())
{
    if (current.ValueRO.Mask != previous.ValueRO.Mask)
    {
        // Mask changed — mark dirty for replication
        SystemAPI.SetComponentEnabled<GameplayTagsDirty>(entity, true);
        previous.ValueRW.Mask = current.ValueRO.Mask;
    }
}
```

### NativeGameplayTagMask (Burst/Jobs)

```csharp
// Create on main thread
var nativeMask = new NativeGameplayTagMask(Allocator.TempJob);
nativeMask.Set(stunTag);
nativeMask.Set(burnTag);

// Or copy from managed data
nativeMask.CopyFrom(managedMask);        // from GameplayTagMask
nativeMask.CopyFrom(container);           // from IGameplayTagContainer
nativeMask.CopyFrom(readOnlySnapshot);    // from ReadOnlyGameplayTagContainer

// Use in Jobs
var job = new TagCheckJob { RequiredTags = nativeMask };
job.Schedule(entityCount, 64).Complete();

// Bitwise operations (Burst-compatible)
NativeGameplayTagMask.And(in maskA, in maskB, Allocator.Temp, out var result);
NativeGameplayTagMask.Or(in maskA, in maskB, Allocator.Temp, out var result2);

bool containsAll = nativeMask.ContainsAll(otherMask);
bool containsAny = nativeMask.ContainsAny(otherMask);

// Cleanup
nativeMask.Dispose();
result.Dispose();
```

---

## Editor Tools

### Gameplay Tag Manager Window

**Menu**: `Tools > CycloneGames > Gameplay Tag Manager`

- Full tree view of all registered tags
- Search bar for quick filtering
- Right-click context menu: **Add Tag**, **Delete Tag**, **Create Child Tag** (prefills parent path)
- Refresh button to reload tags and trigger Source Generator

### Inspector Integration

`GameplayTag` and `GameplayTagContainer` fields have custom property drawers:

- **GameplayTag**: Dropdown with searchable popup showing the full tag tree
- **GameplayTagContainer**: "Edit Tags" button opens a popup with checkboxes, plus "Clear All" and per-tag remove buttons
- Invalid tags are highlighted in red

### Tag Validation Tool

**Menu**: `Tools > CycloneGames > GameplayTags > Tag Validation Window`

- Scans all Prefabs, ScriptableObjects, and open Scenes for invalid `GameplayTagContainer` references
- Shows asset path, invalid tag name, and context object
- **Fix** button to remove individual invalid tags
- **Fix All** button for batch cleanup

### Source Generator

The `GameplayTagsSourceGenerator` automatically generates a static class with compile-time `GameplayTag` constants:

```csharp
// Generated code (example)
public static class GTags
{
    public static readonly GameplayTag Damage_Physical_Slash = GameplayTagManager.RequestTag("Damage.Physical.Slash");
    public static readonly GameplayTag CrowdControl_Stunned  = GameplayTagManager.RequestTag("CrowdControl.Stunned");
    // ...
}

// Usage — zero magic strings, IDE auto-complete, compile-time error on typos
if (container.HasTag(GTags.CrowdControl_Stunned)) { /* ... */ }
```

---

## Build Pipeline

Tags are compiled to an efficient binary format during `IPreprocessBuildWithReport`:

```
[byte version=1] [int tagCount] [string tagName × tagCount] [uint crc32]
```

- **Version byte** ensures forward compatibility
- **CRC32 checksum** detects data corruption (disk I/O errors, truncation)
- At runtime, `BuildGameplayTagSource` reads this binary from `Resources` — no JSON parsing overhead

---

## HybridCLR / Hot-Update Support

The system is designed for hot-update workflows:

```csharp
// After loading a hot-update assembly:
GameplayTagManager.RegisterDynamicTagsFromAssembly(hotLoadedAssembly);

// Or register from a type in the hot-update assembly:
GameplayTagManager.RegisterDynamicTagsFromType(typeof(HotUpdateTags));

// Or from server config:
GameplayTagManager.RegisterDynamicTags(serverTagList);
```

Dynamic registration automatically rebuilds the `TagDataSnapshot` and publishes it atomically.

---

## Dependencies

- **None** (Core assembly is pure C#)

### Optional

| Package | Purpose |
|---------|---------|
| `com.unity.collections` | `NativeGameplayTagMask` |
| `com.unity.burst` | `[BurstCompile]` on native mask operations |
| `com.unity.entities` | `GameplayTagMaskComponent` (IComponentData) |
| Newtonsoft.Json | JSON tag file parsing (editor only) |
