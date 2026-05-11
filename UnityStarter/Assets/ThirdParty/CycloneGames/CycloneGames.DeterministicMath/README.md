# CycloneGames.DeterministicMath

English | [简体中文](./README.SCH.md)

Cross-platform deterministic fixed-point math library. Q32.32 arithmetic, CORDIC trigonometry, quaternion rotations, and 2D collision detection — all producing bit-identical results on every platform.

## Features

- **Q32.32 fixed-point arithmetic** — 64-bit `FPInt64` with ±2.1B range, ~2.3e-10 precision
- **CORDIC trigonometry** — `Sin`, `Cos`, `Tan`, `Atan`, `Atan2`, `Asin`, `Acos` via 32-iteration CORDIC; zero floating-point, fully deterministic
- **Vector math** — `FPVector2`, `FPVector3` with dot, cross, magnitude, normalization, lerp
- **Quaternion rotations** — `FPQuaternion` with AngleAxis, Euler (ZXY), LookRotation, Slerp, Nlerp
- **2D collision** — Circle, AABB, Ray primitives with overlap tests and raycasting
- **3D geometry** — Sphere, AABB, OBB, Ray primitives with SAT overlap tests and raycasting
- **4×4 matrix** — Column-major matrices: Translate, Scale, Rotate, TRS, Perspective, multiply, transform
- **Deterministic PRNG** — xoshiro256** struct with state save/restore for rollback (zero GC)
- **Pure C# Core** — `noEngineReferences: true`, zero Unity dependency, netstandard2.0
- **Zero-GC** — all types are value types (`readonly struct`), no heap allocations on hot paths
- **Fast division** — power-of-2 divisor fast path (de Bruijn bit-scan)

## Architecture

```text
CycloneGames.DeterministicMath.Core   ← Pure C#, engine-agnostic
```

No Unity adapter layer needed — this module has no platform-specific behavior beyond logging (via static delegates with safe defaults).

### Cross-Engine Compatibility

The core math (multiplication, Slerp, vector rotation) is engine-agnostic — it works identically in Unity, Godot, Unreal, or a standalone .NET server. Only the static constructors encode engine-specific conventions:

| Constructor | Default Convention | Cross-Engine Usage |
| --- | --- | --- |
| `Euler(pitch, yaw, roll)` | Unity ZXY, Y-up, left-handed | Use `Euler(first, second, third, EulerOrder.YXZ)` for Godot |
| `LookRotation(forward, up)` | Unity (forward = +Z, up = +Y) | Remap axes for Godot (forward = -Z) or Unreal (up = +Z) |
| `AngleAxis(angle, axis)` | Engine-agnostic | Always correct |
| Quaternion math (×, Slerp) | Engine-agnostic | Always correct |

**Godot example:**

```csharp
// Godot uses YXZ order, right-handed, Y-up with forward = -Z
var rot = FPQuaternion.Euler(pitch, yaw, roll, EulerOrder.YXZ);
// For LookRotation: negate forward direction
var lookRot = FPQuaternion.LookRotation(-godotForward, godotUp);
```

## Quick Start

### Fixed-Point Numbers

```csharp
using CycloneGames.DeterministicMath;

var a = FPInt64.FromFloat(3.14f);
var b = FPInt64.FromInt(2);
var result = a * b;           // 6.28 in Q32.32
float approx = result.ToFloat(); // ~6.28f
```

### Trigonometry

```csharp
var angle = FPInt64.Pi / 3;      // 60 degrees
var sin = FPMath.Sin(angle);     // ~0.866
var cos = FPMath.Cos(angle);     // ~0.5

// Compute both in a single CORDIC pass (half the cost)
FPMath.SinCos(angle, out var s, out var c);

// Atan2 with correct quadrant handling
var theta = FPMath.Atan2(sin, cos); // ~π/3
```

### Quaternion Rotation

```csharp
var rotation = FPQuaternion.Euler(FPInt64.Pi / 2, 0, 0); // 90° pitch
var rotated = rotation * new FPVector3(1, 0, 0);           // (1, 0, 0) → (1, 0, 1)

// Smooth interpolation
var mid = FPQuaternion.Slerp(q1, q2, FPInt64.FromFloat(0.5f));
```

### 2D Collision

```csharp
var circle = new FPCircle(new FPVector2(0, 0), 5);
var aabb = new FPAABB(new FPVector2(3, 3), new FPVector2(8, 8));
bool hit = FPCollision2D.CircleAABBOverlap(circle, aabb); // true

var ray = new FPRay2D(new FPVector2(-1, 1), new FPVector2(1, 0));
var t = FPCollision2D.RayCircleIntersect(ray, circle); // t ≈ 0.172
```

### 3D Geometry & Raycasting

```csharp
var sphere = new FPSphere(new FPVector3(0, 0, 0), 5);
var aabb = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));

// Overlap tests
bool hit = FPRaycast3D.SphereBoundsOverlap(sphere, aabb);

// OBB with SAT
var obb = new FPOBB(new FPVector3(0, 0, 0), FPVector3.One, FPQuaternion.Identity);
bool obbHit = FPRaycast3D.OBBOverlap(obbA, obbB);

// 3D raycasting
var ray = new FPRay(new FPVector3(-1, 2, 2), new FPVector3(1, 0, 0));
var t = FPRaycast3D.RayBounds(ray, aabb); // t ≈ 1.0

// Matrix transforms
var mat = FPMatrix4x4.TRS(translation, rotation, scale);
var worldPoint = mat * localPoint;
```

### Deterministic Random

```csharp
var rand = DeterministicRandom.Create(12345UL);
var value = rand.NextFP();                        // [0, 1)
var ranged = rand.NextFP(FPInt64.Zero, 100);      // [0, 100)

// Save/restore for rollback
var state = rand.SaveState();
rand.RestoreState(state); // identical sequence resumes
```

## API Reference

### FPInt64

| Member | Description |
| --- | --- |
| `RawValue` | The underlying `long` raw bits |
| `FromInt(int)` / `FromFloat(float)` / `FromDouble(double)` | Conversion from standard types |
| `ToInt()` / `ToFloat()` / `ToDouble()` | Conversion to standard types |
| `Sqrt(FPInt64)` | Newton's method, 8 iterations |
| `Abs` / `Min` / `Max` / `Clamp` / `Lerp` | Standard math |

Constants: `Zero`, `OneValue`, `MinusOne`, `Pi`, `TwoPi`, `HalfPi`, `Deg2Rad`, `Rad2Deg`

### FPMath

| Method | Description |
| --- | --- |
| `Sin(angle)` / `Cos(angle)` | CORDIC rotation mode, 32 iterations |
| `SinCos(angle, out sin, out cos)` | Combined call, half the cost |
| `Tan(angle)` | Sin/Cos with asymptote detection |
| `Atan(y)` / `Atan2(y, x)` | CORDIC vectoring mode |
| `Asin(x)` / `Acos(x)` | Via Atan2 + Sqrt, domain [-1, 1] |
| `NormalizeAngle(a)` | Wrap to [-Pi, Pi] |

### FPQuaternion

| Member | Description |
| --- | --- |
| `AngleAxis(angle, axis)` | Rotation from axis and angle |
| `Euler(pitch, yaw, roll)` | ZXY order, matches Unity |
| `LookRotation(forward, up)` | Build rotation from direction |
| `Slerp(a, b, t)` / `Nlerp(a, b, t)` | Spherical / normalized linear interpolation |
| `q * p` (operator) | Rotate vector by quaternion |
| `q1 * q2` (operator) | Hamilton product (composition) |

### FPCollision2D

| Method | Description |
| --- | --- |
| `CircleOverlap` / `AABBOverlap` / `CircleAABBOverlap` | Overlap tests |
| `AABBContainsPoint` / `AABBContainsAABB` / `CircleContainsPoint` | Containment tests |
| `RayAABBIntersect` / `RayCircleIntersect` | Raycast (returns distance or -1) |
| `ClosestPointOnAABB` / `ClosestPointOnCircle` | Closest surface point |

### DeterministicRandom

| Member | Description |
| --- | --- |
| `NextULong()` | Raw xoshiro256** output |
| `NextInt(max)` / `NextInt(min, max)` | Integer in range |
| `NextFP()` / `NextFP(min, max)` | Fixed-point in range |
| `SaveState()` / `RestoreState()` | Rollback support |

### FPMatrix4x4

| Member | Description |
| --- | --- |
| `Identity` / `Zero` | Static defaults |
| `Translate(v)` / `Scale(v)` / `Rotate(q)` | Transform matrices |
| `TRS(t, r, s)` | Combined Translation * Rotation * Scale |
| `Perspective(fov, aspect, near, far)` | Projection matrix (right-handed) |
| `m * m` (operator) | Matrix multiplication |
| `m * v` (operator) | Transform point (with perspective divide) |

### FPRaycast3D

| Member | Description |
| --- | --- |
| `SphereContainsPoint` / `SphereOverlap` / `SphereBoundsOverlap` | Sphere tests |
| `BoundsContainsPoint` / `BoundsOverlap` | AABB tests |
| `OBBOverlap` | SAT-based OBB overlap (15 axes) |
| `RaySphere` / `RayBounds` / `RayOBB` | 3D raycast (returns distance or -1) |
| `ClosestPointOnBounds` / `ClosestPointOnSphere` | Closest surface point |

### Shape Types (readonly struct)

| Type | Fields |
| --- | --- |
| `FPSphere` | `Center (FPVector3)`, `Radius (FPInt64)` |
| `FPBounds` | `Min (FPVector3)`, `Max (FPVector3)` |
| `FPOBB` | `Center`, `HalfExtents (FPVector3)`, `Orientation (FPQuaternion)` |
| `FPRay` | `Origin (FPVector3)`, `Direction (FPVector3)` |

## Performance Notes

- All types are `readonly struct` — zero heap allocation per operation
- `SinCos` reuses one CORDIC pass; calling both `Sin` and `Cos` separately costs 2x
- Quaternion multiplication is 16 FPInt64 multiplications + 12 additions
- Slerp costs 1 Acos + 3 SinCos (guarded against poles); Nlerp costs 1 Normalize (cheaper)
- DeterministicRandom is a struct — zero heap allocation, embeddable in entities
- Division by power-of-2 (e.g. /2) takes the fast bit-shift path
- OBB overlap uses SAT with 15 separating axes and skip for near-parallel edges

## Module Reference

```
CycloneGames.DeterministicMath/
├── package.json
├── README.md / README.SCH.md
├── Core/
│   ├── CycloneGames.DeterministicMath.Core.asmdef
│   ├── DeterministicMathLogger.cs
│   ├── FPInt64.cs
│   ├── FPVector2.cs
│   ├── FPVector3.cs
│   ├── DeterministicRandom.cs
│   ├── FPMath.cs
│   ├── FPQuaternion.cs
│   ├── FPCollision2D.cs
│   ├── FPMatrix4x4.cs
│   └── FPGeometry3D.cs
└── Tests/Editor/
    ├── CycloneGames.DeterministicMath.Tests.Editor.asmdef
    ├── FPInt64Tests.cs
    ├── FPMathTrigTests.cs
    ├── FPQuaternionTests.cs
    └── FPGeometry3DTests.cs
```
