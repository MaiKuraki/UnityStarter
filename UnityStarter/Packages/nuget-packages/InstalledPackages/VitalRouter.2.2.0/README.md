# VitalRouter

[![GitHub license](https://img.shields.io/github/license/hadashiA/VitalRouter)](./LICENSE)
![Unity 2022.2+](https://img.shields.io/badge/unity-2022.2+-000.svg)
![.NET 6.0+](https://img.shields.io/badge/.NET-6.0%2B-512bd4.svg)

VitalRouter is a high-performance, zero-allocation in-memory messaging library for C#. It is specifically designed for environments where performance and decoupling are critical, such as Unity games or complex .NET applications.

By using simple attributes, you can implement asynchronous handlers, middleware (interceptors), and advanced sequence control. VitalRouter leverages Roslyn Source Generators to produce highly efficient code, promoting a clean, unidirectional control flow with minimal overhead.

## Documentation

Visit [vitalrouter.hadashikick.jp](https://vitalrouter.hadashikick.jp) to see the full documentation.

## Key Features

- **Zero Allocation**: Optimized for high-frequency messaging without GC pressure.
- **Thread-Safe**: Designed for safe use across multiple threads.
- **Unidirectional Flow**: Promotes a predictable data flow through your application.
- **Declarative Routing**: Use attributes like `[Routes]` and `[Route]` to define handlers.
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
Commands are lightweight data structures representing an event or action.

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
    // Use ValueTask for pure .NET performance
    [Route]
    public void On(MoveCommand cmd)
    {
        Console.WriteLine($"Moving to {cmd.Destination}");
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

### (Optional) Naive Pub/Sub
You can also subscribe using lambdas without using the source generator.

```csharp
// Simple subscription
router.Subscribe<MoveCommand>(cmd => { /* ... */ });

// Async subscription with ordering
router.SubscribeAwait<MoveCommand>(async (cmd, ct) => 
{
    await DoSomethingAsync();
}, CommandOrdering.Sequential);

// Inline interceptors (Filters)
router
    .WithFilter<MoveCommand>(async (cmd, context, next) =>
    {
        Console.WriteLine("Before");
        await next(cmd, context);
        Console.WriteLine("After");
    })
    .Subscribe(cmd => { /* ... */ });
```

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
UniTask is a fast async/await extension for Unity. VitalRouter actively supports UniTask.
Requirements: UniTask >= 2.5.5

> [!TIP]
> If UniTask is installed, the `VITALROUTER_UNITASK_INTEGRATION` flag is automatically turned on, executing optimized GC-free code paths.

[Read more](https://vitalrouter.hadashikick.jp/extensions/unitask)

---

## Installation

### Requirements
- Unity 2022.2+ (Uses Incremental Source Generator)
- .NET 6.0+

### Packages

| Package                                      | Latest version                                                                                                                                                   |
| :------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `VitalRouter`                                | [![NuGet](https://img.shields.io/nuget/v/VitalRouter)](https://www.nuget.org/packages/VitalRouter)                                                               |
| `VitalRouter.Extensions.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.Extensions.DependencyInjection)](https://www.nuget.org/packages/VitalRouter.Extensions.DependencyInjection) |
| `VitalRouter.R3`                             | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.R3)](https://www.nuget.org/packages/VitalRouter.R3)                                                         |
| `VitalRouter.MRuby`                          | [![NuGet](https://img.shields.io/nuget/v/VitalRouter.MRuby)](https://www.nuget.org/packages/VitalRouter.MRuby)                                                   |

### Unity Installation

> [!NOTE]
> Starting with version 2.0, distribution in Unity has been changed to NuGet.

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Search and install `VitalRouter` packages in the NuGet window.

**Optional (UPM)**

If you prefer UPM, you can install the assembly for Unity via Git URL:
```text
https://github.com/hadashiA/VitalRouter.git?path=/src/VitalRouter.Unity/Assets/VitalRouter#2.2.0
```

---

## Advanced Features

### Async Interceptor Pipeline
Pipelining of async interceptors for published messages is possible. This is a general strong pattern for data exchange.

<img src="./website/docs/assets/diagram_interceptors.svg" alt="Interceptor Diagram" width="50%" />

[Read more](https://vitalrouter.hadashikick.jp/pipeline/interceptor)

### R3 Integration
R3 is the next generation Reactive Extensions implementation in the C# world. VitalRouter supports the ability to work with R3.

[Read more](https://vitalrouter.hadashikick.jp/extensions/r3)

### MRuby Scripting
Control command publishing via external MRuby scripts. Fiber in mruby and async/await in C# are fully integrated.

![MRuby and C# Diagram](./website/docs/assets/diagram_mruby.png)

[Read more](https://vitalrouter.hadashikick.jp/extensions/mruby/intro)

---

## Appendix: Best Practices

Based on large-scale production usage (e.g., HybridFrame):

1. **Prefer record structs**: For commands that are pure data, `record struct` provides equality comparison and zero-allocation benefits.
2. **Explicit Lifecycles**: Always use `.AddTo(destroyCancellationToken)` or manual `Dispose()` to avoid memory leaks and ghost event handling.
3. **UniTask for Unity**: Use `UniTask` or `UniTaskVoid` as return types in Unity handlers to leverage optimized pooling. Use `ValueTask` for pure .NET projects.
4. **Contextual Metadata**: Use `PublishContext` for cross-cutting concerns (logging IDs, cancellation tokens, user permissions) rather than polluting your command structs.
5. **Sequential by Default for UI**: Use `CommandOrdering.Sequential` for UI animations or dialogue sequences to prevent race conditions.
6. **DI Integration**: In Unity, VContainer (>= 1.16.6) is highly recommended for managing router lifecycles and handler registration.

## License
MIT

## Author
@hadashiA

