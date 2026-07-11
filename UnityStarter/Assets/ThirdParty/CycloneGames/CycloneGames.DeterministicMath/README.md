# CycloneGames.DeterministicMath

English | [简体中文](./README.SCH.md)

CycloneGames.DeterministicMath is a pure C# deterministic math foundation for simulations that must reproduce the same raw numeric state from the same ordered inputs. It provides signed Q32.32 fixed-point arithmetic, vectors, trigonometry, rotations, matrices, 2D and 3D geometry queries, and a caller-owned deterministic random stream.

The Core assembly has no Unity engine references and no external package dependencies. It can be used by Unity gameplay code, server processes, command-line tools, replay validators, and test runners through the same value contracts.

This guide starts with a working fixed-tick simulation and then develops the deeper contracts needed for lockstep, rollback, snapshots, networking, and performance-sensitive production code.

## Learning Path

- Start here: [what the module solves](#what-the-module-solves), [assembly reference](#add-the-assembly-reference), and the [five-minute example](#five-minute-fixed-tick-example).
- Learn the numeric core: [Q32.32](#q3232-fundamentals), [arithmetic policies](#arithmetic-policies), [failure strategy](#failure-strategy), and [vectors](#vectors).
- Build spatial logic: [trigonometry](#trigonometry-and-angles), [quaternions](#quaternions-and-euler-angles), [matrices](#matrices), [2D geometry](#2d-geometry), and [3D geometry](#3d-geometry).
- Build deterministic systems: [random streams](#deterministic-random-streams), [rollback](#rollback-and-resimulation), [serialization](#serialization-contract), and the [Unity adapter pattern](#unity-adapter-pattern).
- Prepare production use: [performance](#performance), [ownership and threading](#ownership-lifetime-and-threading), [common mistakes](#common-mistakes), and [validation](#validation).

## What the Module Solves

Floating-point arithmetic is appropriate for rendering, authoring, and many local effects. A synchronized simulation has a different requirement: every participant must agree on the exact state after every ordered tick. Small numeric differences can otherwise accumulate into different positions, decisions, collision results, or random call paths.

DeterministicMath provides the numeric layer for that problem:

- <code>FPInt64</code> stores signed Q32.32 values in one <code>long</code>.
- <code>FPVector2</code> and <code>FPVector3</code> provide fixed-point vector operations.
- <code>FPMath</code> provides deterministic trigonometric and angle functions.
- <code>FPQuaternion</code> and <code>EulerOrder</code> provide 3D rotations.
- <code>FPMatrix4x4</code> provides affine and projective transforms with explicit point and direction methods.
- <code>FPGeometry2D</code> and <code>FPGeometry3D</code> provide validated shapes and allocation-free queries.
- <code>DeterministicRandom</code> provides an explicit xoshiro256** stream with saveable state.

Typical uses include:

- deterministic fixed-tick character or ability simulation;
- rollback and resimulation after corrected network input;
- lockstep strategy, tactics, or board-game state;
- authoritative client/server calculations that share a raw numeric protocol;
- replay recording and verification;
- headless simulation and CI golden-vector checks;
- deterministic procedural choices driven by a saved random stream.

The module does not provide a complete networking stack, physics engine, tick scheduler, save system, encryption system, rendering layer, or cryptographic random generator. Those systems own ordering, transport, persistence, presentation, and security. DeterministicMath gives them a precise numeric foundation.

## Design at a Glance

| Concern | Design |
| --- | --- |
| Scalar representation | Signed Q32.32 in <code>FPInt64.RawValue</code> |
| Scalar storage | One signed 64-bit integer |
| Public construction | Explicit factories; the raw constructor is private |
| Arithmetic hot path | Explicit two's-complement wrapping operators |
| Checked arithmetic | Additive <code>Try*</code> methods with no exception on ordinary failure |
| Angles | Radians |
| Coordinate basis | +X right, +Y up, +Z forward |
| Matrix convention | Column vectors; <code>left * right</code> applies <code>right</code> first |
| Geometry | Validated value-type shapes and static query classes |
| Random ownership | Mutable struct owned by the simulation object that advances it |
| Unity dependency | None in Core |
| Runtime persistence | None; callers serialize explicit raw fields and state words |
| Runtime global state | None |

Most public data types are immutable value types. <code>DeterministicRandom</code> is deliberately mutable because generating a value advances its four-word stream state.

## Add the Assembly Reference

The module is located under:

~~~text
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeterministicMath/
~~~

A consuming Unity assembly references <code>CycloneGames.DeterministicMath.Core</code>. For example:

~~~json
{
    "name": "MyGame.Simulation",
    "references": [
        "CycloneGames.DeterministicMath.Core"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "autoReferenced": true,
    "noEngineReferences": true
}
~~~

Then import the namespace:

~~~csharp
using CycloneGames.DeterministicMath;
~~~

Content under <code>Assets/</code> is governed by asmdef references. The local package manifest is descriptive metadata; the assembly reference is the compile-time dependency that matters inside this repository.

## Five-Minute Fixed-Tick Example

The following class is pure C#. It accepts a fixed-point input, normalizes it safely, and advances position at 60 ticks per second.

~~~csharp
using CycloneGames.DeterministicMath;

public sealed class FixedTickMover
{
    private static readonly FPInt64 TickDelta = FPInt64.One / 60;
    private static readonly FPInt64 MoveSpeed = FPInt64.FromInt(6);

    public FPVector3 Position { get; private set; }
    public FPVector3 Velocity { get; private set; }

    public FixedTickMover(FPVector3 initialPosition)
    {
        Position = initialPosition;
        Velocity = FPVector3.Zero;
    }

    public void Tick(FPVector2 moveInput)
    {
        FPVector2 planarDirection = moveInput.NormalizedOrZero;
        FPVector3 worldDirection = new FPVector3(
            planarDirection.X,
            FPInt64.Zero,
            planarDirection.Y);

        Velocity = worldDirection * MoveSpeed;
        Position += Velocity * TickDelta;
    }
}
~~~

Drive it with an integer tick index and fixed-point input:

~~~csharp
FixedTickMover mover = new FixedTickMover(FPVector3.Zero);

for (int tick = 0; tick < 120; tick++)
{
    FPVector2 input = tick < 60
        ? FPVector2.Right
        : FPVector2.Up;

    mover.Tick(input);
}

long authoritativeX = mover.Position.X.RawValue;
long authoritativeY = mover.Position.Y.RawValue;
long authoritativeZ = mover.Position.Z.RawValue;
~~~

The tick duration, input, speed, position, and velocity remain fixed-point throughout the simulation. Unity vectors or floating-point interpolation can still be used in the presentation layer after the authoritative tick completes.

### What Makes This Reproducible

Another process produces the same raw position when all of the following are the same:

1. the Core implementation and numeric contracts;
2. the initial raw state;
3. the tick count and tick order;
4. every input value and the tick where it is applied;
5. branch and collection iteration order;
6. the order and number of random calls;
7. any external query result injected into the simulation.

Fixed-point arithmetic cannot correct a nondeterministic update order, an unordered input source, a race between jobs, or a floating-point value generated independently on each peer. Determinism is a whole-simulation property built on explicit raw contracts.

## Q32.32 Fundamentals

<code>FPInt64</code> divides one signed <code>long</code> into 32 integer bits and 32 fractional bits:

~~~text
numericValue = RawValue / 4294967296
RawValue     = numericValue * 4294967296
~~~

Important constants:

| Constant | Type | Meaning |
| --- | --- | --- |
| <code>FPInt64.FractionalBits</code> | <code>int</code> | 32 |
| <code>FPInt64.RAW_ONE</code> | <code>long</code> | Raw representation of 1 |
| <code>FPInt64.RAW_HALF</code> | <code>long</code> | Raw representation of 0.5 |
| <code>FPInt64.Zero</code> | <code>FPInt64</code> | Numeric 0 |
| <code>FPInt64.One</code> | <code>FPInt64</code> | Numeric 1 |
| <code>FPInt64.Half</code> | <code>FPInt64</code> | Numeric 0.5 |
| <code>FPInt64.MinusOne</code> | <code>FPInt64</code> | Numeric -1 |

The resolution is exactly <code>2^-32</code>, approximately <code>2.3283064365386963e-10</code>. The range is:

~~~text
minimum: -2147483648
maximum:  2147483647.99999999976716935634613037109375
~~~

The <code>FPInt64</code> constructor is private. Use a factory that states whether the source is an integer, decimal text, floating-point boundary value, or raw protocol value.

### Construct Values

~~~csharp
FPInt64 whole = FPInt64.FromInt(12);
FPInt64 fraction = FPInt64.Parse("3.125");
FPInt64 authored = FPInt64.FromDouble(0.75);
FPInt64 protocolValue = FPInt64.FromRaw(13_421_772_800L);

FPInt64 implicitWhole = 5;
~~~

Only <code>int</code> has an implicit conversion. Floating-point values require an explicit factory so that the boundary is visible in code review.

<code>FromFloat</code> and <code>FromDouble</code> reject NaN, infinity, and values outside the Q32.32 range. Their <code>TryFromFloat</code> and <code>TryFromDouble</code> forms return <code>false</code> and a default result.

~~~csharp
if (!FPInt64.TryFromDouble(authoringValue, out FPInt64 simulationValue))
{
    throw new ArgumentOutOfRangeException(
        nameof(authoringValue),
        "The authored value is outside the deterministic numeric domain.");
}
~~~

Decimal text uses an invariant period. <code>ToString()</code> emits the exact decimal expansion needed to restore the same raw bits.

~~~csharp
FPInt64 source = FPInt64.FromRaw(long.MaxValue);
string text = source.ToString();

if (!FPInt64.TryParse(text, out FPInt64 parsed))
{
    throw new FormatException("Invalid fixed-point text.");
}

bool sameBits = parsed.RawValue == source.RawValue;
~~~

Use decimal text for human-readable configuration and diagnostics. Use <code>RawValue</code> for compact snapshots and protocols.

### Convert Out

~~~csharp
int truncated = fraction.ToInt();
float presentationValue = fraction.ToFloat();
double analysisValue = fraction.ToDouble();
~~~

<code>ToInt()</code> truncates toward zero. Floating-point output belongs at presentation, tooling, logging, or adapter boundaries; do not feed independently recomputed display values back into synchronized state.

## Arithmetic Policies

The ordinary scalar operators are the low-overhead path:

~~~csharp
FPInt64 sum = left + right;
FPInt64 difference = left - right;
FPInt64 product = left * right;
FPInt64 quotient = left / right;
FPInt64 remainder = left % right;
~~~

Addition, subtraction, negation, multiplication, and representable-range overflow use explicit unchecked two's-complement wrapping. This behavior does not depend on the consumer's checked compiler setting.

Use checked methods at authored-data, protocol, save, and uncertain-range boundaries:

~~~csharp
public static bool TryCalculateScaledDamage(
    FPInt64 baseDamage,
    FPInt64 multiplier,
    FPInt64 divisor,
    out FPInt64 result)
{
    return FPInt64.TryMultiplyDivide(
        baseDamage,
        multiplier,
        divisor,
        out result);
}
~~~

Available checked scalar methods include:

- <code>TryAdd</code>
- <code>TrySubtract</code>
- <code>TryNegate</code>
- <code>TryMultiply</code>
- <code>TryDivide</code>
- <code>TryMultiplyDivide</code>
- <code>TryAbs</code>
- <code>TryCeil</code>
- <code>TryRound</code>
- <code>TrySqrt</code>

<code>TryMultiplyDivide(a, b, divisor)</code> evaluates <code>(a * b) / divisor</code> with a full-width intermediate. It is useful when a representable final result would be lost by first performing a wrapping multiplication.

### Clamp and Interpolation

<code>Lerp</code> clamps <code>t</code> to <code>[0, 1]</code>. <code>LerpUnclamped</code> permits extrapolation.

~~~csharp
FPInt64 start = 10;
FPInt64 end = 20;

FPInt64 clamped = FPInt64.Lerp(start, end, FPInt64.Parse("1.5"));
FPInt64 extrapolated = FPInt64.LerpUnclamped(
    start,
    end,
    FPInt64.Parse("1.5"));

// clamped == 20
// extrapolated == 25
~~~

The same naming rule applies to vector and quaternion interpolation: the method without the suffix clamps <code>t</code>; the <code>Unclamped</code> method permits values outside the unit interval.

### Rounding and Square Root

| Method | Rule |
| --- | --- |
| <code>Floor</code> | Toward negative infinity |
| <code>Ceil</code> | Toward positive infinity |
| <code>Round</code> | Nearest integer; midpoint away from zero |
| <code>Sqrt</code> | Floor fixed-point square root; negative input is invalid |

~~~csharp
FPInt64 value = FPInt64.Parse("-1.5");

FPInt64 floor = FPInt64.Floor(value); // -2
FPInt64 ceil = FPInt64.Ceil(value);   // -1
FPInt64 round = FPInt64.Round(value); // -2
FPInt64 root = FPInt64.Sqrt(9);       // 3
~~~

## Failure Strategy

The API distinguishes three intentions:

1. wrapping operators for ranges already proven by the simulation;
2. fail-fast methods for programmer or configuration errors;
3. <code>Try*</code> methods for expected boundary failure.

| Operation | Failure behavior |
| --- | --- |
| <code>FromFloat</code>, <code>FromDouble</code> | <code>ArgumentOutOfRangeException</code> for non-finite or out-of-range input |
| <code>Parse</code> | <code>FormatException</code> for invalid or out-of-range text |
| Division or remainder by zero | <code>DivideByZeroException</code> |
| <code>Abs(MinValue)</code>, unrepresentable <code>Ceil</code>/<code>Round</code> | <code>OverflowException</code> |
| <code>Sqrt</code> of a negative value | <code>ArgumentOutOfRangeException</code> |
| <code>Tan</code> at an exact asymptote or outside Q32.32 | <code>InvalidOperationException</code> |
| <code>Asin</code>/<code>Acos</code> outside <code>[-1, 1]</code> | <code>ArgumentOutOfRangeException</code> |
| <code>Normalized</code> for an undefined vector | <code>InvalidOperationException</code> |
| <code>NormalizedOrZero</code> for an undefined vector | Returns zero |
| Invalid quaternion normalization or inverse | <code>InvalidOperationException</code> |
| Invalid quaternion construction input | <code>ArgumentException</code> |
| Singular or unsupported matrix inverse | <code>InvalidOperationException</code> |
| Invalid projective point | <code>InvalidOperationException</code> |
| Invalid shape constructor input | <code>ArgumentException</code> or <code>ArgumentOutOfRangeException</code> |
| Ray miss, degenerate ray, invalid shape, or numeric failure | <code>TryRay*</code> returns <code>false</code> and default output |
| Uninitialized random stream | <code>InvalidOperationException</code> |
| Invalid random range | <code>ArgumentOutOfRangeException</code> |
| All-zero random state | <code>ArgumentException</code> |

For a <code>Try*</code> call, consume the output only when the returned Boolean is <code>true</code>.

## Vectors

### Construction and Basis

~~~csharp
FPVector2 input = new FPVector2(3, 4);
FPVector3 position = new FPVector3(10, 2, -5);

FPVector3 up = FPVector3.Up;
FPVector3 forward = FPVector3.Forward;
FPVector3 right = FPVector3.Right;
~~~

<code>FPVector2</code> provides <code>Zero</code>, <code>One</code>, <code>Right</code>, and <code>Up</code>. <code>FPVector3</code> also provides <code>Down</code>, <code>Forward</code>, <code>Back</code>, and <code>Left</code>.

Both vector types support value equality:

~~~csharp
bool same = new FPVector3(1, 2, 3) == new FPVector3(1, 2, 3);
bool different = FPVector2.Right != FPVector2.Up;
~~~

### Magnitude and Normalization

~~~csharp
FPVector3 velocity = new FPVector3(3, 4, 0);

FPInt64 squaredSpeed = velocity.SqrMagnitude; // 25
FPInt64 speed = velocity.Magnitude;           // 5
FPVector3 direction = velocity.Normalized;
~~~

Use the property that expresses the domain policy you need:

~~~csharp
FPVector3 requiredDirection = source.Normalized;
FPVector3 optionalDirection = source.NormalizedOrZero;

if (!source.TryNormalize(out FPVector3 checkedDirection))
{
    // Reject an invalid command, authored value, or protocol payload.
}
~~~

- <code>Normalized</code> requires a normalizable non-zero vector and fails fast otherwise.
- <code>NormalizedOrZero</code> explicitly chooses zero as the fallback.
- <code>TryNormalize</code> exposes the decision to the caller.

Magnitude uses scaled intermediates so that large vectors do not wrap during squaring and raw-1 micro vectors are not mistaken for zero. <code>SqrMagnitude</code> and <code>DistanceSqr</code> saturate to <code>FPInt64.MaxValue</code> when the exact squared result cannot fit Q32.32.

### Dot, Cross, Projection, and Reflection

~~~csharp
FPVector3 velocity = new FPVector3(4, -3, 2);
FPVector3 unitNormal = FPVector3.Up;

if (!FPVector3.TryDot(velocity, unitNormal, out FPInt64 normalSpeed))
{
    throw new OverflowException("Dot product is outside the numeric domain.");
}

if (!FPVector3.TryProject(velocity, unitNormal, out FPVector3 verticalPart))
{
    throw new InvalidOperationException("Projection is undefined.");
}

if (!FPVector3.TryReflect(velocity, unitNormal, out FPVector3 reflected))
{
    throw new OverflowException("Reflection is outside the numeric domain.");
}

FPVector3 tangent = FPVector3.Cross(unitNormal, FPVector3.Forward);
~~~

The reflection formula expects a unit normal. Normalize authored or calculated normals before calling it. Projection accepts a non-unit target vector but rejects a zero target.

<code>Dot</code>, <code>Cross</code>, and ordinary vector operators use wrapping scalar arithmetic. Use the available <code>Try*</code> methods when ranges are not proven.

### Vector Interpolation

~~~csharp
FPVector3 a = new FPVector3(0, 0, 0);
FPVector3 b = new FPVector3(10, 5, -2);

FPVector3 midpoint = FPVector3.Lerp(a, b, FPInt64.Half);
FPVector3 beyond = FPVector3.LerpUnclamped(
    a,
    b,
    FPInt64.Parse("1.25"));
~~~

## Trigonometry and Angles

<code>FPMath</code> uses a deterministic integer CORDIC implementation. Inputs and outputs are radians.

~~~csharp
FPInt64 degrees = 45;
FPInt64 radians = degrees * FPInt64.Deg2Rad;

FPMath.SinCos(
    radians,
    out FPInt64 sin,
    out FPInt64 cos);
~~~

The output order is <code>sin</code>, then <code>cos</code>. Use <code>SinCos</code> when both results are needed so the CORDIC pass is shared.

### Tangent and Inverse Functions

~~~csharp
if (!FPMath.TryTan(radians, out FPInt64 tangent))
{
    throw new InvalidOperationException("Tangent is undefined.");
}

FPInt64 heading = FPMath.Atan2(
    FPInt64.FromInt(1),
    FPInt64.FromInt(-1));

if (!FPMath.TryAsin(FPInt64.Half, out FPInt64 asin) ||
    !FPMath.TryAcos(FPInt64.Half, out FPInt64 acos))
{
    throw new ArgumentOutOfRangeException();
}
~~~

<code>Atan2</code> takes <code>(y, x)</code> and returns a value in <code>[-Pi, Pi]</code>. The origin <code>Atan2(0, 0)</code> is defined as zero.

<code>Tan</code> fails fast at an exact asymptote or when the quotient is outside Q32.32. <code>TryTan</code> is the expected-failure form. <code>Asin</code> and <code>Acos</code> require input in <code>[-1, 1]</code>.

### Normalize Angles

~~~csharp
FPInt64 signedAngle = FPMath.NormalizeAngle(angle);
FPInt64 positiveAngle = FPMath.NormalizeAnglePositive(angle);
~~~

- <code>NormalizeAngle</code> returns <code>[-Pi, Pi]</code>.
- <code>NormalizeAnglePositive</code> returns <code>[0, TwoPi)</code>.

## Quaternions and Euler Angles

### Axis-Angle and Vector Rotation

~~~csharp
FPInt64 quarterTurn = 90 * FPInt64.Deg2Rad;
FPQuaternion yaw = FPQuaternion.AngleAxis(
    quarterTurn,
    FPVector3.Up);

if (!FPQuaternion.TryRotate(
        yaw,
        FPVector3.Forward,
        out FPVector3 rotatedForward))
{
    throw new InvalidOperationException("Rotation result is outside the domain.");
}
~~~

<code>AngleAxis</code> normalizes its axis and rejects a zero axis. Positive angles follow the right-hand rule.

The quaternion-vector operator is a wrapping hot path intended for a normalized quaternion:

~~~csharp
FPVector3 fastResult = yaw * FPVector3.Forward;
~~~

Use <code>TryRotate</code> when the quaternion or vector comes from an untrusted boundary; it normalizes the quaternion and checks the result.

### Euler Construction

~~~csharp
FPInt64 pitch = 20 * FPInt64.Deg2Rad;
FPInt64 yawAngle = 35 * FPInt64.Deg2Rad;
FPInt64 roll = -10 * FPInt64.Deg2Rad;

FPQuaternion rotation = FPQuaternion.Euler(
    xRadians: pitch,
    yRadians: yawAngle,
    zRadians: roll,
    order: EulerOrder.ZXY);

FPVector3 extracted = rotation.ToEuler(EulerOrder.ZXY);
~~~

The three parameters always represent X, Y, and Z angles. <code>EulerOrder</code> controls intrinsic composition order; it does not reorder the parameter meanings. Available orders are <code>XYZ</code>, <code>XZY</code>, <code>YXZ</code>, <code>YZX</code>, <code>ZXY</code>, and <code>ZYX</code>. The overload without an order uses <code>ZXY</code>.

Euler triples are not unique. Near gimbal lock, compare the resulting rotation or rotated basis vectors instead of comparing extracted angle components.

### Direction Constructors

~~~csharp
if (!FPQuaternion.TryFromToRotation(
        FPVector3.Forward,
        targetDirection,
        out FPQuaternion turn))
{
    throw new ArgumentException("Directions must be non-zero.");
}

if (!FPQuaternion.TryLookRotation(
        targetDirection,
        FPVector3.Up,
        out FPQuaternion look))
{
    throw new ArgumentException("Forward direction must be non-zero.");
}
~~~

<code>TryLookRotation</code> uses a deterministic orthogonal reference when the supplied up vector is zero or collinear with forward.

### Normalize, Invert, and Compare

~~~csharp
FPQuaternion normalized = rotation.Normalized;

if (!normalized.TryInverse(out FPQuaternion inverse))
{
    throw new InvalidOperationException("Quaternion has no representable inverse.");
}

FPQuaternion identityRotation = normalized * inverse;
~~~

<code>Conjugate</code> equals the inverse only for a unit quaternion. Use <code>Inverse</code> or <code>TryInverse</code> for a general non-zero quaternion.

Quaternion equality compares raw components. A quaternion and its negation describe the same spatial rotation but are different raw values. When rotational equivalence matters, compare their effect on basis vectors or use the absolute quaternion dot product within a documented tolerance.

### Quaternion Interpolation

~~~csharp
FPQuaternion halfway = FPQuaternion.Slerp(
    start,
    end,
    FPInt64.Half);

FPQuaternion fastHalfway = FPQuaternion.Nlerp(
    start,
    end,
    FPInt64.Half);

FPQuaternion extrapolated = FPQuaternion.SlerpUnclamped(
    start,
    end,
    FPInt64.Parse("1.25"));
~~~

- <code>Slerp</code> provides spherical interpolation and clamps <code>t</code>.
- <code>Nlerp</code> provides normalized linear interpolation and clamps <code>t</code>.
- <code>SlerpUnclamped</code> and <code>NlerpUnclamped</code> permit extrapolation.
- Interpolation chooses the shortest quaternion arc.

Use normalized rotation inputs. Prefer <code>Nlerp</code> for a lower-cost blend when constant angular velocity is not required.

## Matrices

<code>FPMatrix4x4</code> uses column vectors. Matrix composition follows:

~~~text
result = left * right
~~~

The right matrix is applied first.

### TRS and Composition

~~~csharp
FPVector3 translation = new FPVector3(10, -4, 7);
FPQuaternion rotation = FPQuaternion.Euler(
    10 * FPInt64.Deg2Rad,
    25 * FPInt64.Deg2Rad,
    0);
FPVector3 scale = new FPVector3(2, 3, 4);

FPMatrix4x4 localToWorld = FPMatrix4x4.TRS(
    translation,
    rotation,
    scale);

FPMatrix4x4 composed =
    FPMatrix4x4.Translate(translation) *
    FPMatrix4x4.Rotate(rotation) *
    FPMatrix4x4.Scale(scale);
~~~

Both forms apply scale, then rotation, then translation. <code>Scale</code> supports non-uniform scale.

### Transform a Point or Direction

The API makes homogeneous intent explicit:

~~~csharp
FPVector3 localPoint = new FPVector3(1, 2, 3);
FPVector3 localDirection = FPVector3.Forward;

FPVector3 worldPoint = localToWorld.TransformPoint(localPoint);
FPVector3 worldDirection =
    localToWorld.TransformDirection(localDirection);
~~~

- <code>TransformPoint</code> applies the affine 3x4 transform, including translation.
- <code>TransformDirection</code> applies the upper 3x3 transform and ignores translation.
- <code>ProjectPoint</code> performs a homogeneous transform and divides by <code>w</code>.

Checked forms are available:

~~~csharp
if (!localToWorld.TryTransformPoint(
        localPoint,
        out FPVector3 checkedPoint))
{
    throw new OverflowException("Point transform is outside Q32.32.");
}

if (!localToWorld.TryTransformDirection(
        localDirection,
        out FPVector3 checkedDirection))
{
    throw new OverflowException("Direction transform is outside Q32.32.");
}
~~~

### Perspective Projection

~~~csharp
FPInt64 aspect = FPInt64.FromInt(16) / 9;

FPMatrix4x4 projection = FPMatrix4x4.Perspective(
    60 * FPInt64.Deg2Rad,
    aspect,
    FPInt64.Parse("0.1"),
    FPInt64.FromInt(1000));

FPVector3 viewPoint = new FPVector3(0, 0, -10);

if (!projection.TryProjectPoint(
        viewPoint,
        out FPVector3 normalizedDevicePoint))
{
    throw new InvalidOperationException("Projective point is invalid.");
}
~~~

Perspective uses a right-handed view space with visible points along negative Z and a depth range of <code>[0, 1]</code>. It requires:

- <code>0 &lt; fovRadians &lt; Pi</code>;
- <code>aspect &gt; 0</code>;
- <code>near &gt; 0</code>;
- <code>far &gt; near</code>.

Do not write <code>16 / 9</code> as an integer expression and then convert it; that expression is 1. Convert at least one operand first, as shown above.

### Matrix Fields and Inverse

Elements are named <code>M00</code> through <code>M33</code>, where the first digit is the row and the second digit is the column. The indexer also accepts <code>[row, column]</code>.

~~~csharp
FPInt64 translationX = localToWorld.M03;
FPInt64 sameValue = localToWorld[0, 3];

if (!localToWorld.TryInverse(out FPMatrix4x4 worldToLocal))
{
    throw new InvalidOperationException("Matrix inverse is unsupported.");
}

FPVector3 restored = worldToLocal.TransformPoint(worldPoint);
~~~

<code>TryInverse</code> rejects singular and near-singular matrices. It also checks both multiplication orders against identity with checked arithmetic before accepting the candidate.

## 2D Geometry

### Shapes and Invariants

~~~csharp
FPCircle circle = new FPCircle(
    new FPVector2(10, 0),
    FPInt64.FromInt(2));

FPAABB2D bounds = new FPAABB2D(
    new FPVector2(8, -3),
    new FPVector2(12, 3));
~~~

- A circle radius must be non-negative.
- Every AABB minimum component must be less than or equal to its maximum component.
- Boundary contact counts as overlap and containment.

### Overlap and Containment

~~~csharp
bool overlap = FPGeometry2D.CircleAABBOverlap(
    circle,
    bounds);

bool containsCenter = FPGeometry2D.AABBContainsPoint(
    bounds,
    circle.Center);

bool circlesTouch = FPGeometry2D.CircleOverlap(
    circle,
    new FPCircle(new FPVector2(14, 0), 2));
~~~

Circle distance decisions use full-width integer intermediates. They do not rely on a saturated public squared-magnitude display value.

### Ray Queries

~~~csharp
FPRay2D ray = new FPRay2D(
    FPVector2.Zero,
    FPVector2.Right);

if (FPGeometry2D.TryRayCircle(
        ray,
        circle,
        out FPInt64 t))
{
    FPVector2 hitPoint = ray.Origin + ray.Direction * t;
}

if (FPGeometry2D.TryRayAABB(
        ray,
        bounds,
        out FPInt64 boundsT))
{
    FPVector2 boundsHit =
        ray.Origin + ray.Direction * boundsT;
}
~~~

A <code>false</code> result leaves <code>t</code> at its default value. It can mean no forward hit, a degenerate direction, or an unrepresentable checked intermediate. Do not consume <code>t</code> after failure.

### Closest Points

~~~csharp
FPVector2 point = new FPVector2(20, 5);
FPVector2 onBounds = FPGeometry2D.ClosestPointOnAABB(
    bounds,
    point);

if (!FPGeometry2D.TryClosestPointOnCircle(
        circle,
        point,
        out FPVector2 onCircle))
{
    throw new OverflowException("Closest point is outside Q32.32.");
}
~~~

For a point inside an AABB, <code>ClosestPointOnAABB</code> returns the point itself. At the exact circle center, the circle query chooses a deterministic point on the X axis.

## 3D Geometry

### Shapes

~~~csharp
FPSphere sphere = new FPSphere(
    new FPVector3(0, 0, 10),
    FPInt64.FromInt(2));

FPAABB3D bounds = new FPAABB3D(
    new FPVector3(-5, -5, 5),
    new FPVector3(5, 5, 15));

FPOBB3D orientedBox = new FPOBB3D(
    center: new FPVector3(0, 0, 10),
    halfExtents: new FPVector3(2, 1, 4),
    orientation: FPQuaternion.AngleAxis(
        30 * FPInt64.Deg2Rad,
        FPVector3.Up));
~~~

- Sphere radius must be non-negative.
- AABB minimum components must not exceed maximum components.
- OBB half-extents must be non-negative.
- OBB orientation must be non-zero and is normalized by the constructor.
- A default <code>FPOBB3D</code> is invalid because its orientation is zero.

### Queries

~~~csharp
bool sphereInBounds = FPGeometry3D.SphereAABBOverlap(
    sphere,
    bounds);

bool obbOverlap = FPGeometry3D.OBBOverlap(
    orientedBox,
    new FPOBB3D(
        new FPVector3(3, 0, 10),
        new FPVector3(1, 1, 1),
        FPQuaternion.Identity));

bool containsPoint = FPGeometry3D.AABBContainsPoint(
    bounds,
    new FPVector3(0, 0, 8));
~~~

OBB overlap uses the complete 15-axis separating-axis test. Invalid OBB input is rejected.

### 3D Ray Queries

~~~csharp
FPRay3D ray = new FPRay3D(
    FPVector3.Zero,
    FPVector3.Forward);

if (FPGeometry3D.TryRaySphere(
        ray,
        sphere,
        out FPInt64 sphereT))
{
    FPVector3 hit =
        ray.Origin + ray.Direction * sphereT;
}

if (FPGeometry3D.TryRayAABB(
        ray,
        bounds,
        out FPInt64 boundsT))
{
    FPVector3 hit =
        ray.Origin + ray.Direction * boundsT;
}

if (FPGeometry3D.TryRayOBB(
        ray,
        orientedBox,
        out FPInt64 obbT))
{
    FPVector3 hit =
        ray.Origin + ray.Direction * obbT;
}
~~~

All ray methods are <code>TryRay*</code> methods. Failure leaves the output at default. OBB ray queries transform the ray into normalized OBB-local space with checked translation and rotation.

## Ray Parameter Semantics

Every ray result satisfies:

~~~text
point(t) = Origin + Direction * t
~~~

The returned <code>t</code> is a world-space distance only when <code>Direction</code> has unit magnitude. For example, doubling the direction halves the parameter for the same point.

~~~csharp
FPRay3D distanceRay = new FPRay3D(
    origin,
    direction.Normalized);

if (FPGeometry3D.TryRaySphere(
        distanceRay,
        sphere,
        out FPInt64 distance))
{
    FPVector3 hit =
        distanceRay.Origin +
        distanceRay.Direction * distance;
}
~~~

Ray queries accept forward intersections where <code>t &gt;= 0</code>. When the origin is inside a closed shape, the result is the first forward exit parameter rather than zero.

## Deterministic Random Streams

<code>DeterministicRandom</code> implements xoshiro256**. A SplitMix64 expansion turns one <code>ulong</code> seed into four state words.

~~~csharp
DeterministicRandom random =
    DeterministicRandom.Create(0xC0FFEEUL);

ulong raw = random.NextULong();
int cardIndex = random.NextInt(52);    // [0, 52)
int die = random.NextInt(1, 7);        // [1, 7)
FPInt64 unit = random.NextFP();        // [0, 1)
FPInt64 spread = random.NextFP(-1, 1); // [-1, 1)
~~~

All range maximums are exclusive. Integer bounded sampling uses rejection sampling so the mapping is unbiased.

### Ownership

The generator is a mutable struct. The simulation owner must retain the instance that it advances.

~~~csharp
public sealed class LootRoller
{
    private DeterministicRandom _random;

    public LootRoller(ulong seed)
    {
        _random = DeterministicRandom.Create(seed);
    }

    public int RollIndex(int itemCount)
    {
        return _random.NextInt(itemCount);
    }
}
~~~

When a helper must advance the caller's stream, pass it by <code>ref</code>:

~~~csharp
public static int RollInclusiveDie(
    ref DeterministicRandom random,
    int sideCount)
{
    return random.NextInt(1, sideCount + 1);
}
~~~

Assigning the struct copies all four state words and creates an intentional identical branch:

~~~csharp
DeterministicRandom branchA = random;
DeterministicRandom branchB = random;

bool firstValuesMatch =
    branchA.NextULong() == branchB.NextULong();
~~~

Do not concurrently mutate one logical stream. For parallel simulation, assign a deterministic stream to each stable owner and process owners in a defined order.

### State Save and Restore

~~~csharp
DeterministicRandom random =
    DeterministicRandom.Create(12345UL);

DeterministicRandomState checkpoint =
    random.SaveState();

ulong first = random.NextULong();
random.RestoreState(checkpoint);
ulong repeated = random.NextULong();

bool exactReplay = first == repeated;
~~~

The all-zero four-word state is invalid. <code>TryRestoreState</code> reports failure without replacing the current state.

The random contract is identified by:

~~~csharp
string algorithmId = DeterministicRandom.ALGORITHM_ID;
int algorithmVersion = DeterministicRandom.ALGORITHM_VERSION;
~~~

Persist both values together with <code>S0</code>, <code>S1</code>, <code>S2</code>, and <code>S3</code>. Replay also depends on making the same sampling calls in the same order. A bounded call may consume more than one raw output because rejection sampling retries rejected values.

## Rollback and Resimulation

A rollback snapshot stores every authoritative field needed to continue the simulation, including random state.

~~~csharp
using CycloneGames.DeterministicMath;

public readonly struct MoverSnapshot
{
    public readonly int Tick;
    public readonly long PositionXRaw;
    public readonly long PositionYRaw;
    public readonly long PositionZRaw;
    public readonly long VelocityXRaw;
    public readonly long VelocityYRaw;
    public readonly long VelocityZRaw;
    public readonly DeterministicRandomState RandomState;

    public MoverSnapshot(
        int tick,
        FPVector3 position,
        FPVector3 velocity,
        DeterministicRandomState randomState)
    {
        Tick = tick;
        PositionXRaw = position.X.RawValue;
        PositionYRaw = position.Y.RawValue;
        PositionZRaw = position.Z.RawValue;
        VelocityXRaw = velocity.X.RawValue;
        VelocityYRaw = velocity.Y.RawValue;
        VelocityZRaw = velocity.Z.RawValue;
        RandomState = randomState;
    }

    public FPVector3 RestorePosition()
    {
        return new FPVector3(
            FPInt64.FromRaw(PositionXRaw),
            FPInt64.FromRaw(PositionYRaw),
            FPInt64.FromRaw(PositionZRaw));
    }

    public FPVector3 RestoreVelocity()
    {
        return new FPVector3(
            FPInt64.FromRaw(VelocityXRaw),
            FPInt64.FromRaw(VelocityYRaw),
            FPInt64.FromRaw(VelocityZRaw));
    }
}
~~~

A bounded history can use the tick as a ring-buffer key:

~~~csharp
MoverSnapshot[] history = new MoverSnapshot[128];

history[currentTick % history.Length] =
    CaptureSnapshot(currentTick);

MoverSnapshot rewind =
    history[correctedTick % history.Length];

RestoreSnapshot(rewind);

for (int tick = correctedTick; tick < currentTick; tick++)
{
    SimulateTick(tick, recordedInputs[tick]);
}
~~~

The application owns entity existence, collection order, input history, event suppression during resimulation, and side-effect reconciliation. A numeric snapshot alone is insufficient if those states influence future ticks.

## Serialization Contract

### Fixed-Point Values

Serialize signed raw values explicitly:

~~~csharp
long positionX = position.X.RawValue;
long positionY = position.Y.RawValue;
long positionZ = position.Z.RawValue;

FPVector3 restored = new FPVector3(
    FPInt64.FromRaw(positionX),
    FPInt64.FromRaw(positionY),
    FPInt64.FromRaw(positionZ));
~~~

For quaternion and matrix state, serialize each named component in an explicitly documented order:

~~~text
Quaternion: X, Y, Z, W
Matrix: M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
        M30, M31, M32, M33
~~~

### Protocol Rules

The owning serializer must define:

- schema identifier and schema version;
- signed integer encoding;
- byte order;
- field order;
- optional compression;
- payload length limits;
- integrity validation;
- handling for unknown schema values;
- corruption and recovery policy.

Do not serialize private struct memory, rely on runtime padding, store <code>GetHashCode()</code>, or convert authoritative values through float. Reconstruct validated shapes through their constructors so invalid payloads are rejected.

### Random State

Persist:

~~~text
ALGORITHM_ID
ALGORITHM_VERSION
S0
S1
S2
S3
~~~

Validate the algorithm identity, algorithm version, payload schema, and non-zero state before restoring a stream.

## Unity Adapter Pattern

Core intentionally does not expose <code>UnityEngine.Vector2</code>, <code>UnityEngine.Vector3</code>, <code>Quaternion</code>, <code>Matrix4x4</code>, <code>MonoBehaviour</code>, or <code>ScriptableObject</code>. Place conversion code in a Unity-facing adapter assembly.

~~~csharp
using CycloneGames.DeterministicMath;
using UnityEngine;

public static class DeterministicVectorAdapter
{
    public static FPVector3 ToDeterministic(Vector3 value)
    {
        return new FPVector3(
            FPInt64.FromFloat(value.x),
            FPInt64.FromFloat(value.y),
            FPInt64.FromFloat(value.z));
    }

    public static Vector3 ToUnity(FPVector3 value)
    {
        return new Vector3(
            value.X.ToFloat(),
            value.Y.ToFloat(),
            value.Z.ToFloat());
    }
}
~~~

Recommended flow:

~~~mermaid
flowchart LR
    A["Inspector or input device"] --> B["Unity adapter validation"]
    N["Network raw payload"] --> C["Protocol validation"]
    B --> D["Fixed-point command"]
    C --> D
    D --> E["Pure deterministic tick"]
    E --> F["Raw snapshot"]
    E --> G["Unity presentation adapter"]
    G --> H["Transform, animation, VFX, UI"]
~~~

Convert authoring data once, before it enters authoritative state. Convert simulation results to Unity values for display. Rendering interpolation can remain floating-point if it never feeds back into the simulation:

~~~csharp
Vector3 previous = DeterministicVectorAdapter.ToUnity(
    previousSimulationPosition);
Vector3 current = DeterministicVectorAdapter.ToUnity(
    currentSimulationPosition);

transform.position = Vector3.Lerp(
    previous,
    current,
    renderAlpha);
~~~

Use an explicit integer tick scheduler for synchronized simulation. Unity frame rate and <code>FixedUpdate</code> scheduling are presentation or orchestration concerns unless the product formally defines them as the authoritative tick source.

## Performance

### Current Editor Measurements

The checked-in performance suite uses deterministic arrays containing 10,240 varying operands. Each benchmark performs five warmups and twenty measurements with one batch per measurement. Normalization processes 10,238 overlapping vector triples from that dataset. Results are consumed by a static sink to prevent dead-code removal.

The following measurements were recorded in the Windows Unity Editor managed runtime:

| Batch | Median | Mean | GC per measured sample |
| --- | ---: | ---: | ---: |
| 10,240 Q32.32 multiplications | 0.2964 ms | 0.2996 ms | 0 B |
| 10,240 non-power-of-two divisions | 3.87965 ms | 4.260645 ms | 0 B |
| 10,240 square roots | 9.08185 ms | 9.3754 ms | 0 B |
| 10,240 <code>SinCos</code> calls | 3.323 ms | 3.317385 ms | 0 B |
| 10,238 vector normalizations | 22.77975 ms | 23.26167 ms | 0 B |

The warmed Core arithmetic allocation gate also reported zero bytes from <code>GC.GetAllocatedBytesForCurrentThread()</code> across its selected steady-state batches.

These numbers are development evidence for this machine and Editor runtime. They are not Player, IL2CPP, Burst, mobile, console, server, or cross-architecture results, and they are not release budgets.

### Cost Model

- Addition, subtraction, comparison, and raw conversion are small integer operations.
- Multiplication uses full-width integer decomposition.
- General division and square root are substantially more expensive.
- <code>SinCos</code> uses iterative CORDIC work; call it once when both outputs are needed.
- Vector normalization combines scaling, square root, and division.
- Quaternion construction and interpolation combine multiple vector and trigonometric operations.
- OBB overlap evaluates up to 15 separating axes.
- Matrix inverse is intended for setup or infrequent queries, not an unchecked inner loop.

### Hot-Path Guidance

1. Convert constants and authored values before the tick loop.
2. Cache normalized directions that remain unchanged.
3. Prefer squared distance for ordering or thresholds when its saturation domain is acceptable.
4. Use full geometry queries for large-range collision decisions.
5. Share one <code>SinCos</code> call when both values are needed.
6. Use wrapping operators only after ranges are established.
7. Validate uncertain data with <code>Try*</code> before it reaches the inner loop.
8. Avoid <code>ToString</code>, boxing, interface dispatch, LINQ, and exception-driven control flow in hot paths.
9. Avoid accidental copies of large matrices and mutable random streams.
10. Measure the real Player/backend with representative game data.

Formatting, exception creation, boxing, caller collections, delegates, and Unity adapters can allocate even when the selected arithmetic batch does not.

## Ownership, Lifetime, and Threading

| Resource | Owner | Lifetime rule |
| --- | --- | --- |
| Scalar/vector/quaternion/matrix/shape | Caller value | Copy freely; values are immutable |
| Random stream | Simulation subsystem or stable entity | Retain and advance one owned mutable instance |
| Random checkpoint | Snapshot/replay owner | Persist all state words and algorithm identity |
| CORDIC lookup data | Core static initialization | Read-only after initialization |
| Native memory | None | No disposal |
| Worker threads | None | Scheduling belongs to the caller |
| Cancellation source | None | No shutdown path |

Independent immutable values and independently owned random streams can be processed concurrently. Concurrent writes to one logical random stream or shared simulation container require caller-defined synchronization and deterministic scheduling.

## Persistence Behavior

| Data category | Module behavior | Recommended owner |
| --- | --- | --- |
| Project settings | No file or asset is created | Project composition |
| User preferences | Nothing is written | Application settings service |
| Runtime saves | Nothing is written | Versioned save service |
| Replay data | Nothing is written | Replay recorder |
| Fixed-point snapshots | Exposes raw values only | Networking or rollback layer |
| Random checkpoints | Exposes four state words | Replay or snapshot layer |
| Cache | No cache is owned | Not applicable |

The module does not use <code>PlayerPrefs</code>, <code>EditorPrefs</code>, <code>SessionState</code>, registry keys, environment variables, scenes, Prefabs, or ScriptableObject settings. There is nothing module-owned to clean from disk.

## Directory and Dependency Structure

~~~text
CycloneGames.DeterministicMath/
  package.json
  README.md
  README.SCH.md
  Core/
    CycloneGames.DeterministicMath.Core.asmdef
    DeterministicRandom.cs
    DeterministicRandomState.cs
    EulerOrder.cs
    FPAABB2D.cs
    FPAABB3D.cs
    FPCircle.cs
    FPGeometry2D.cs
    FPGeometry3D.cs
    FPGeometryUtility.cs
    FPInt64.cs
    FPMagnitudeUtility.cs
    FPMath.cs
    FPMatrix4x4.cs
    FPOBB3D.cs
    FPQuaternion.cs
    FPRay2D.cs
    FPRay3D.cs
    FPSphere.cs
    FPVector2.cs
    FPVector3.cs
  Tests/
    Editor/
      CycloneGames.DeterministicMath.Tests.Editor.asmdef
      *Tests.cs
    Performance/
      CycloneGames.DeterministicMath.Tests.Performance.asmdef
      DeterministicMathPerformanceTests.cs
~~~

~~~mermaid
flowchart TD
    C["CycloneGames.DeterministicMath.Core<br/>pure C#, no assembly references"]:::core
    S["Game simulation assembly"]:::consumer --> C
    U["Unity adapter assembly"]:::adapter --> C
    N["Networking/save adapter"]:::adapter --> C
    E["Editor correctness tests"]:::test --> C
    P["Performance tests"]:::test --> C
    P --> PT["Unity Performance Testing"]:::external

    classDef core fill:#1f6f5f,color:#ffffff,stroke:#10463c
    classDef consumer fill:#315a8a,color:#ffffff,stroke:#1d3654
    classDef adapter fill:#7b5aa6,color:#ffffff,stroke:#4c3768
    classDef test fill:#9a6a24,color:#ffffff,stroke:#624316
    classDef external fill:#555555,color:#ffffff,stroke:#333333
~~~

Core has:

- no assembly references;
- <code>noEngineReferences: true</code>;
- no unsafe code;
- no conditional compilation symbols;
- no service container dependency.

The Editor correctness assembly references only Core and Unity test assemblies. The performance assembly is present only when the Unity Performance Testing package satisfies its asmdef capability definition.

## API Selection Checklist

Use this decision sequence:

1. Is the source already an authoritative raw value? Use <code>FromRaw</code>.
2. Is it an authored integer? Use <code>FromInt</code> or implicit <code>int</code>.
3. Is it human-readable decimal configuration? Use <code>Parse</code>/<code>TryParse</code>.
4. Is it a Unity or tooling boundary? Use <code>FromFloat</code>/<code>FromDouble</code> after validation.
5. Can the arithmetic range be proven? Use operators in the hot path.
6. Can failure occur from input or scale? Use <code>Try*</code>.
7. Must interpolation stay between endpoints? Use the clamped method.
8. Is extrapolation intentional? Use the <code>Unclamped</code> method.
9. Is a zero vector a valid fallback? Use <code>NormalizedOrZero</code>.
10. Must zero be rejected? Use <code>Normalized</code> or <code>TryNormalize</code>.
11. Are you transforming a point, direction, or projected point? Select the explicit matrix method.
12. Is ray <code>t</code> meant to be distance? Normalize direction first.
13. Will a random call affect replay? Save and restore the owning stream state.

## Common Mistakes

- Treating <code>RAW_ONE</code> as an <code>FPInt64</code> value or <code>One</code> as a raw <code>long</code>.
- Passing a raw integer to <code>FromInt</code> instead of <code>FromRaw</code>.
- Performing integer division such as <code>16 / 9</code> before fixed-point conversion.
- Expecting ordinary operators to report overflow.
- Calling <code>Normalized</code> when zero is an accepted input fallback.
- Using <code>NormalizedOrZero</code> when zero indicates invalid authored or protocol data.
- Supplying a non-unit normal to reflection.
- Reversing <code>SinCos</code> output order.
- Reversing <code>Atan2(y, x)</code>.
- Supplying degrees to a radians API.
- Comparing Euler triples instead of rotations.
- Treating quaternion raw equality as spatial-rotation equality.
- Applying <code>TransformPoint</code> to a direction.
- Applying <code>TransformDirection</code> to a position.
- Applying <code>TransformPoint</code> when perspective division is required.
- Reading a failed <code>TryRay*</code> output.
- Treating ray <code>t</code> as distance without a unit direction.
- Constructing reversed AABBs or negative radii/extents.
- Treating a default OBB as valid.
- Advancing a copy of <code>DeterministicRandom</code>.
- Sharing one random stream across nondeterministically scheduled jobs.
- Persisting random words without algorithm ID and algorithm version.
- Serializing struct memory rather than named raw fields.
- Feeding presentation floats back into authoritative state.

## Validation

### Unity Editor

1. Open <code>&lt;repo-root&gt;/UnityStarter</code> with the Unity version declared in <code>ProjectSettings/ProjectVersion.txt</code>.
2. Open <strong>Window &gt; General &gt; Test Runner</strong>.
3. Select EditMode.
4. Run <code>CycloneGames.DeterministicMath.Tests.Editor</code>.
5. If Unity Performance Testing is installed, run <code>CycloneGames.DeterministicMath.Tests.Performance</code>.
6. Confirm active consumer assemblies compile without errors.

### Batch Mode

~~~text
<Unity-executable> -batchmode -nographics -quit \
  -projectPath <repo-root>/UnityStarter \
  -runTests -testPlatform EditMode \
  -assemblyNames CycloneGames.DeterministicMath.Tests.Editor \
  -testResults <repo-root>/Artifacts/DeterministicMath.EditMode.xml \
  -logFile <repo-root>/Artifacts/DeterministicMath.EditMode.log
~~~

Create the artifact directory before running the command.

### Production Acceptance

Before using the module as an authoritative cross-process contract:

- round-trip minimum, maximum, negative, fractional, vector, quaternion, and matrix raw values;
- compile Core with overflow checking both disabled and enabled, then run the same correctness suite against each build;
- compare raw golden vectors on every supported runtime and architecture;
- verify overflow, zero, invalid-domain, and every <code>Try*</code> path;
- verify clamped and unclamped interpolation;
- verify all six Euler orders, gimbal-lock cases, and basis-vector rotation;
- verify affine point, direction, projective point, and inverse behavior;
- verify large-range circle, sphere, AABB, OBB, closest-point, and ray queries;
- verify the RNG seed expansion, golden sequence, bounded ranges, and state restore;
- capture and replay a representative rollback window;
- compare complete authoritative snapshots after resimulation;
- profile the actual Player/backend with production-scale data.

The release claim should name the exact platforms, runtimes, test corpus, raw contracts, and performance budgets that were verified.
