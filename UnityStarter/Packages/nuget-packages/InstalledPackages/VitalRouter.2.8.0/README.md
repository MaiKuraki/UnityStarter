# VitalRouter

[![GitHub license](https://img.shields.io/github/license/hadashiA/VitalRouter)](./LICENSE)
![Unity 2022.2+](https://img.shields.io/badge/unity-2022.2+-000.svg)
![.NET 6.0+](https://img.shields.io/badge/.NET-6.0%2B-512bd4.svg)

VitalRouter is a high-performance, zero-allocation in-memory messaging library for C#. It is specifically designed for environments where performance and decoupling are critical, such as Unity games or complex .NET applications.

By using simple attributes, you can implement asynchronous handlers, middleware (interceptors), and advanced sequence control. VitalRouter leverages Roslyn Source Generators to produce highly efficient code, promoting a clean, unidirectional control flow with minimal overhead.

## Documentation

Visit [vitalrouter.hadashikick.jp](https://vitalrouter.hadashikick.jp) for the full documentation.

## Key Features

- **Zero Allocation**: Optimized for high-frequency messaging without GC pressure.
- **Thread-Safe**: Designed for safe use across multiple threads.
- **Unidirectional Flow**: Promotes a predictable data flow through your application.
- **Declarative Routing**: Use attributes like `[Routes]` and `[Route]` to define handlers.
- **Sequential Control**: Declaratively queue, drop, or cancel overlapping async handlers — perfect for game cutscenes, dialogue, and tutorials.
- **Async Interceptor Pipeline**: Build sophisticated message processing chains.
- **Versatile Compatibility**: Works seamlessly in both Unity and standard .NET projects.
- **DI Integration**: Native support for VContainer (Unity) and Microsoft.Extensions.DependencyInjection.

---

## Core Concepts

VitalRouter follows a simple messaging flow. A **Publisher** sends a **Command** to a **Router**, which then dispatches it through optional **Interceptors** to one or more **Handlers**.

```mermaid
---
config:
  theme: dark
---
graph LR
    Publisher[Publisher] -- PublishAsync --> Router[Router]
    subgraph Pipeline
        Router -- next --> Interceptor1[Interceptor A]
        Interceptor1 -- next --> Interceptor2[Interceptor B]
    end
    Interceptor2 -- Invoke --> Handler[Handler/Presenter]
```

### 1. Define a Command
Commands are lightweight data structures representing events or actions.

```csharp
// record structs are recommended for zero-allocation messaging
public readonly record struct MoveCommand(Vector3 Destination) : ICommand;
```

> [!TIP]
> **AOT/HybridCLR Note**: While `record struct` is valid, ensure your AOT metadata is correctly generated for iOS/AOT environments if using tools like HybridCLR.

### 2. Create a Handler (Presenter)
Use the `[Routes]` attribute on a `partial` class to receive commands.

```csharp
[Routes]
public partial class PlayerPresenter
{
    // Use ValueTask for performance in pure .NET projects
    [Route]
    public async ValueTask On(MoveCommand cmd)
    {
        await MoveToAsync(cmd.Destination);
    }

    // Use UniTask for Unity-specific async handling
    [Route]
    public async UniTask On(SomeAsyncCommand cmd)
    {
        await DoSomethingAsync();
    }
}
```

### 3. Map and Publish
Connect your handler to a router and start sending commands.

```csharp
var router = new Router();
var presenter = new PlayerPresenter();

// MapTo returns a Subscription (IDisposable)
using var subscription = presenter.MapTo(router);

// Publish a message
await router.PublishAsync(new MoveCommand(new Vector3(1, 0, 0)));

```

### (Optional) Simple Pub/Sub
You can also subscribe with lambdas instead of using the source generator.

```csharp
// Simple subscription
router.Subscribe<MoveCommand>(cmd => { /* ... */ });

// Async subscription with ordering
router.SubscribeAwait<MoveCommand>(async (cmd, ct) =>
{
    await DoSomethingAsync();
}, CommandOrdering.Sequential);

// Inline interceptors (Filters)
//
// `WithFilter(...)` returns a derived child router that owns the given filter.
// Subscribers registered on the child receive commands published on the parent
// (with the filter applied), just like an Rx `Where` chain forwards items from
// its source.
router
    .WithFilter<MoveCommand>(async (cmd, context, next) =>
    {
        Console.WriteLine("Before");
        await next(cmd, context);
        Console.WriteLine("After");
    })
    .Subscribe(cmd => { /* ... */ });

// Publishing on the parent triggers the filter for subscribers on the child:
await router.PublishAsync(new MoveCommand(...));
// → "Before" → handler → "After"
```

## Sequential Control (Command Ordering)

One of VitalRouter's standout features is **declarative control over how concurrent async handlers run**. This matters most in games, where dialogue, cutscenes, and tutorials must play *in order* — never overlapping.

Take a cutscene. You just publish commands, and VitalRouter plays them back-to-back:

```csharp
public readonly record struct WalkCommand(Vector3 To) : ICommand;
public readonly record struct SpeakCommand(string Text) : ICommand;
public readonly record struct WaitCommand(float Seconds) : ICommand;

// `Sequential`: each command waits for the previous handler to finish.
[Routes(CommandOrdering.Sequential)]
public partial class CutscenePresenter : MonoBehaviour
{
    [Route]
    async UniTask On(WalkCommand cmd) => await character.WalkToAsync(cmd.To);

    [Route]
    async UniTask On(SpeakCommand cmd) => await dialogueView.ShowAsync(cmd.Text);

    [Route]
    async UniTask On(WaitCommand cmd) => await UniTask.Delay(TimeSpan.FromSeconds(cmd.Seconds));
}
```

```csharp
// Fire-and-forget. VitalRouter queues these and runs them strictly in order.
router.PublishAsync(new WalkCommand(stage.Center));
router.PublishAsync(new SpeakCommand("Hello there!"));
router.PublishAsync(new WaitCommand(0.5f));
router.PublishAsync(new SpeakCommand("Welcome to our little town."));
```

Without ordering, all four handlers would start at the same time and the scene would be a mess. With `Sequential`, the character walks in, *then* speaks, *then* pauses, *then* speaks again — no manual coroutine chaining or hand-rolled state machine required.

Pick the strategy that fits each case:

| Ordering              | Behavior                                          | Typical use                                         |
| :-------------------- | :------------------------------------------------ | :-------------------------------------------------- |
| `Parallel` (default)  | Run all handlers concurrently                     | Independent reactions                               |
| `Sequential`          | Queue, then run one at a time in order            | Dialogue, cutscenes, tutorials                      |
| `Drop`                | Ignore new commands while one is still running    | Debounce buttons, prevent double-firing             |
| `Switch`              | Cancel the running handler, start the new one     | "Latest wins" — re-targeting, search-as-you-type    |

Ordering can be set globally, per Router, per `[Routes]` class, per `[Route]` method, or per `SubscribeAwait` call.

[Read more](https://vitalrouter.hadashikick.jp/pipeline/sequential-control)

---

## Unity Integration

VitalRouter is highly optimized for Unity, especially when combined with UniTask.

### MonoBehaviour Example
When using `MapTo` in a `MonoBehaviour`, always bind the subscription to the object's lifecycle.

```csharp
[Routes]
public partial class CharacterController : MonoBehaviour
{
    private void Start()
    {
        // Bind the subscription to this GameObject's lifecycle
        this.MapTo(Router.Default).AddTo(destroyCancellationToken);
    }

    [Route]
    public void On(MoveCommand cmd)
    {
        transform.position = cmd.Destination;
    }
}
```

> [!IMPORTANT]
> **Assembly Definition (.asmdef)**: You must reference `VitalRouter` in your `.asmdef` file for the Source Generator to process your `[Routes]` attributes.

---

## UniTask Integration
UniTask is a fast async/await library for Unity. VitalRouter actively supports UniTask.
Requires: UniTask >= 2.5.5

> [!TIP]
> If UniTask is installed, the `VITALROUTER_UNITASK_INTEGRATION` flag is defined automatically, enabling optimized GC-free code paths.

[Read more](https://vitalrouter.hadashikick.jp/extensions/unitask)

---

## Installation

### Requirements
- **Unity projects**: Unity 2022.2+ required for incremental source generator support.
- **Standard .NET projects**: .NET 6.0+.

### Packages

| Package                                      | Purpose                                      | Latest version                                                                                                                                                   |
| :------------------------------------------- | :------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `VitalRouter`                                | Core messaging and source-generated routing  | [![NuGet](https://img.shields.io/nuget/v/VitalRouter)](https://www.nuget.org/packages/VitalRouter)                                                               |
| `VitalRouter.Extensions.DependencyInjection` | DI registration for standard .NET apps       | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.Extensions.DependencyInjection)](https://www.nuget.org/packages/VitalRouter.Extensions.DependencyInjection) |
| `VitalRouter.R3`                             | Bridge commands to R3 reactive streams       | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.R3)](https://www.nuget.org/packages/VitalRouter.R3)                                                         |
| `VitalRouter.MRuby`                          | Publish and handle commands from MRuby scripts | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.MRuby)](https://www.nuget.org/packages/VitalRouter.MRuby)                                                   |

### Unity Installation

> [!NOTE]
> Since version 2.0, Unity distribution has moved to NuGet.

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Search for and install the `VitalRouter` packages in the NuGet window.

**Optional Extensions for Unity**

Install the following package to use Unity-specific features, including integration with VContainer/UniTask.

```text
https://github.com/hadashiA/VitalRouter.git?path=/src/VitalRouter.Unity/Assets/VitalRouter#2.8.0
```

---

## Advanced Features

### Async Interceptor Pipeline
You can pipeline async interceptors for published messages. This is a powerful general-purpose pattern for message processing.

<img src="./website/docs/assets/diagram_interceptors.svg" alt="Interceptor Diagram" width="50%" />

[Read more](https://vitalrouter.hadashikick.jp/pipeline/interceptor)

### R3 Integration
R3 is a next-generation Reactive Extensions implementation for C#. VitalRouter integrates with R3.

[Read more](https://vitalrouter.hadashikick.jp/extensions/r3)

### MRuby Scripting
Control command publishing via external MRuby scripts. MRuby fibers and C# async/await are fully integrated.

![MRuby and C# Diagram](./website/docs/assets/diagram_mruby.png)

[Read more](https://vitalrouter.hadashikick.jp/extensions/mruby/intro)

---

## Performance

Publishing allocates zero extra heap memory; if `ICommand` is a struct, no boxing occurs.
Performance is on the same level as plain event dispatching, so it can be used for very granular messaging in games.

Below are simple Publish benchmarks (8 subscribers per message; BenchmarkDotNet v0.14.0, .NET 9.0.5, Apple M4).

Synchronous subscribers:

| Method                              | Mean         | Allocated |
|------------------------------------ |-------------:|----------:|
| 'Publish (ZeroMessenger)'           |     5.756 ns |         - |
| 'Publish (MessagePipe)'             |     8.900 ns |         - |
| 'Publish (R3 Subject)'              |    12.827 ns |         - |
| 'Publish (VitalRouter)'             |    12.869 ns |         - |
| 'Publish (System.Reactive Subject)' |    14.598 ns |         - |
| 'Publish (MediatR)'                 |    36.204 ns |      64 B |
| 'Publish (PubSub)'                  |    88.976 ns |     192 B |
| 'Publish (Prism)'                   | 1,087.741 ns |    1552 B |

Asynchronous subscribers (awaiting until all subscribers complete):

| Method                    | Mean      | Allocated |
|-------------------------- |----------:|----------:|
| 'Publish (ZeroMessenger)' |  26.63 ns |         - |
| 'Publish (VitalRouter)'   |  27.01 ns |         - |
| 'Publish (MessagePipe)'   |  36.06 ns |      40 B |
| 'Publish (MediatR)'       |  39.70 ns |      64 B |
| 'Publish (PubSub)'        | 102.57 ns |     192 B |

## Appendix: Best Practices

Based on large-scale production usage (e.g., HybridFrame):

1. **Prefer record structs**: For commands that are pure data, `record struct` provides equality comparison and zero-allocation benefits.
2. **Explicit Lifecycles**: Always use `.AddTo(destroyCancellationToken)` or manual `Dispose()` to avoid memory leaks and ghost event handling.
3. **UniTask for Unity**: Use `UniTask` or `UniTaskVoid` as return types in Unity handlers to leverage optimized pooling. Use `ValueTask` for pure .NET projects.
4. **Contextual Metadata**: Use `PublishContext` for cross-cutting concerns (logging IDs, cancellation tokens, user permissions) instead of adding those fields to your command structs.
5. **Sequential by Default for UI**: Use `CommandOrdering.Sequential` for UI animations or dialogue sequences to prevent race conditions.
6. **DI Integration**: In Unity, VContainer (>= 1.16.6) is highly recommended for managing router lifecycles and handler registration.

## License
MIT

## Author
@hadashiA
