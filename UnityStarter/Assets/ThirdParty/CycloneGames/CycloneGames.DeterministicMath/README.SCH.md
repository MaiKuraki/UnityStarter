# CycloneGames.DeterministicMath

[English](./README.md) | 简体中文

CycloneGames.DeterministicMath 是一个纯 C# 确定性数学底座，适用于必须根据相同有序输入重现完全相同 raw 数值状态的模拟。它提供有符号 Q32.32 定点数运算、向量、三角函数、旋转、矩阵、2D/3D 几何查询，以及由调用方持有的确定性随机数流。

Core 程序集不引用 Unity 引擎，也不依赖外部 package。Unity 玩法代码、服务器进程、命令行工具、replay 校验器和测试运行器都可以使用同一套 value contract（值契约）。

本文先通过一个可运行的 fixed tick（固定离散模拟步）示例完成入门，再逐层说明 lockstep（锁步同步）、rollback（回滚重演）、snapshot（状态快照）、网络协议和生产级性能所需的深层契约。

## 阅读路径

- 从这里开始：[模块解决什么问题](#模块解决什么问题)、[添加程序集引用](#添加程序集引用)和[五分钟示例](#五分钟-fixed-tick-示例)。
- 理解数值核心：[Q32.32](#q3232-基础)、[算术策略](#算术策略)、[失败策略](#失败策略)和[向量](#向量)。
- 构建空间逻辑：[三角函数](#三角函数与角度)、[Quaternion](#quaternion-与-euler-angle)、[矩阵](#矩阵)、[2D 几何](#2d-几何)和[3D 几何](#3d-几何)。
- 构建确定性系统：[随机数流](#确定性随机数流)、[Rollback](#rollback-与-resimulation)、[序列化](#序列化契约)和[Unity Adapter](#unity-adapter-pattern)。
- 准备生产使用：[性能](#性能)、[所有权与线程](#所有权生命周期与线程)、[常见错误](#常见错误)和[验证](#验证)。

## 模块解决什么问题

浮点数适合渲染、authoring（内容制作）和许多本地效果。同步模拟有不同要求：每个参与者必须在每个有序 tick 结束时得到完全一致的状态。细小的数值差异可能逐步累积，最终导致位置、决策、碰撞结果或随机调用路径发生分歧。

DeterministicMath 为这个问题提供数值层：

- <code>FPInt64</code> 使用一个 <code>long</code> 保存有符号 Q32.32 值。
- <code>FPVector2</code> 和 <code>FPVector3</code> 提供定点向量运算。
- <code>FPMath</code> 提供确定性三角函数与角度函数。
- <code>FPQuaternion</code> 和 <code>EulerOrder</code> 提供 3D 旋转。
- <code>FPMatrix4x4</code> 提供 affine（仿射）与 projective（投影）变换，并显式区分 point 和 direction。
- <code>FPGeometry2D</code> 和 <code>FPGeometry3D</code> 提供带校验的图元与无分配查询。
- <code>DeterministicRandom</code> 提供可保存状态的显式 xoshiro256** 随机数流。

典型使用场景包括：

- 确定性 fixed-tick 角色或 ability 模拟；
- 收到修正后的网络输入后执行 rollback 与 resimulation（重新模拟）；
- 策略、战棋或桌游状态的 lockstep；
- 共享 raw 数值协议的权威客户端/服务器计算；
- replay 录制与校验；
- headless simulation（无图形模拟）与 CI golden-vector（黄金向量）检查；
- 由已保存随机数流驱动的确定性程序化选择。

本模块不提供完整网络栈、物理引擎、tick scheduler、存档系统、加密系统、渲染层或密码学随机数生成器。这些系统分别负责顺序、传输、持久化、表现和安全。DeterministicMath 为它们提供精确的数值底座。

## 设计概览

| 关注点 | 设计 |
| --- | --- |
| 标量表示 | <code>FPInt64.RawValue</code> 中的有符号 Q32.32 |
| 标量存储 | 一个有符号 64-bit integer |
| Public 构造 | 显式 factory；raw constructor 为 private |
| 算术热路径 | 显式 two's-complement wrapping operator |
| Checked 算术 | 成组 <code>Try*</code> 方法，常规失败不抛异常 |
| 角度 | Radians（弧度） |
| 坐标基 | +X right、+Y up、+Z forward |
| 矩阵约定 | Column vector；<code>left * right</code> 先应用 <code>right</code> |
| 几何 | 带 invariant 校验的 value-type shape 与静态查询类 |
| 随机数所有权 | 推进随机流的模拟对象持有 mutable struct |
| Unity 依赖 | Core 中无 |
| Runtime 持久化 | 无；调用方序列化显式 raw 字段与 state word |
| Runtime 全局状态 | 无 |

绝大多数 public 数据类型是 immutable value type（不可变值类型）。<code>DeterministicRandom</code> 是有意设计的 mutable struct，因为生成数值会推进它的四个 state word。

## 添加程序集引用

模块位于：

~~~text
UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeterministicMath/
~~~

Unity 消费程序集引用 <code>CycloneGames.DeterministicMath.Core</code>。例如：

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

然后导入 namespace：

~~~csharp
using CycloneGames.DeterministicMath;
~~~

位于 <code>Assets/</code> 下的内容由 asmdef 引用控制。本地 package manifest 是描述性 metadata；在当前仓库中，真正决定编译期依赖的是 assembly reference。

## 五分钟 Fixed-Tick 示例

下面的类是纯 C#。它接收定点输入，以安全方式归一化，并按每秒 60 tick 推进位置。

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

使用 integer tick index 和定点输入驱动：

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

Tick duration、输入、速度、位置与 velocity 在整个模拟过程中都保持为定点数。权威 tick 完成后，presentation layer（表现层）仍可使用 Unity vector 或浮点 interpolation。

### 为什么它可以重现

当以下条件完全相同时，另一个进程会得到相同的 raw position：

1. Core 实现与数值契约；
2. 初始 raw state；
3. tick 数量与 tick 顺序；
4. 每个输入值及其应用 tick；
5. 分支与 collection iteration order；
6. 随机调用的顺序与次数；
7. 注入模拟的所有外部查询结果。

定点算术不能修复不确定的 update order、无序输入源、job race，或每个 peer 独立计算出的浮点值。确定性是建立在显式 raw contract 之上的完整模拟属性。

## Q32.32 基础

<code>FPInt64</code> 将一个有符号 <code>long</code> 分成 32 个整数位和 32 个小数位：

~~~text
numericValue = RawValue / 4294967296
RawValue     = numericValue * 4294967296
~~~

重要常量：

| 常量 | 类型 | 含义 |
| --- | --- | --- |
| <code>FPInt64.FractionalBits</code> | <code>int</code> | 32 |
| <code>FPInt64.RAW_ONE</code> | <code>long</code> | 数值 1 的 raw 表示 |
| <code>FPInt64.RAW_HALF</code> | <code>long</code> | 数值 0.5 的 raw 表示 |
| <code>FPInt64.Zero</code> | <code>FPInt64</code> | 数值 0 |
| <code>FPInt64.One</code> | <code>FPInt64</code> | 数值 1 |
| <code>FPInt64.Half</code> | <code>FPInt64</code> | 数值 0.5 |
| <code>FPInt64.MinusOne</code> | <code>FPInt64</code> | 数值 -1 |

分辨率精确为 <code>2^-32</code>，约为 <code>2.3283064365386963e-10</code>。数值范围为：

~~~text
minimum: -2147483648
maximum:  2147483647.99999999976716935634613037109375
~~~

<code>FPInt64</code> constructor 是 private。请使用能够明确表达数据来自 integer、decimal text、floating-point boundary 还是 raw protocol 的 factory。

### 构造数值

~~~csharp
FPInt64 whole = FPInt64.FromInt(12);
FPInt64 fraction = FPInt64.Parse("3.125");
FPInt64 authored = FPInt64.FromDouble(0.75);
FPInt64 protocolValue = FPInt64.FromRaw(13_421_772_800L);

FPInt64 implicitWhole = 5;
~~~

只有 <code>int</code> 支持 implicit conversion。浮点值必须使用显式 factory，使边界在 code review 中清晰可见。

<code>FromFloat</code> 和 <code>FromDouble</code> 会拒绝 NaN、infinity 以及超出 Q32.32 范围的值。对应的 <code>TryFromFloat</code> 和 <code>TryFromDouble</code> 返回 <code>false</code> 与 default result。

~~~csharp
if (!FPInt64.TryFromDouble(authoringValue, out FPInt64 simulationValue))
{
    throw new ArgumentOutOfRangeException(
        nameof(authoringValue),
        "The authored value is outside the deterministic numeric domain.");
}
~~~

Decimal text 使用 invariant period（固定小数点句点）。<code>ToString()</code> 输出能够恢复相同 raw bits 的精确十进制展开。

~~~csharp
FPInt64 source = FPInt64.FromRaw(long.MaxValue);
string text = source.ToString();

if (!FPInt64.TryParse(text, out FPInt64 parsed))
{
    throw new FormatException("Invalid fixed-point text.");
}

bool sameBits = parsed.RawValue == source.RawValue;
~~~

人类可读配置和诊断使用 decimal text。紧凑 snapshot 与 protocol 使用 <code>RawValue</code>。

### 转换输出

~~~csharp
int truncated = fraction.ToInt();
float presentationValue = fraction.ToFloat();
double analysisValue = fraction.ToDouble();
~~~

<code>ToInt()</code> 向零截断。浮点输出应位于表现、工具、日志或 adapter 边界；不要把每个参与者独立重算的 display value 写回同步状态。

## 算术策略

普通标量 operator 是低开销路径：

~~~csharp
FPInt64 sum = left + right;
FPInt64 difference = left - right;
FPInt64 product = left * right;
FPInt64 quotient = left / right;
FPInt64 remainder = left % right;
~~~

Addition、subtraction、negation、multiplication 以及可表示范围 overflow 使用显式 unchecked two's-complement wrapping。这一行为不依赖消费项目的 checked compiler setting。

在 authored data、protocol、save 与范围不确定的边界使用 checked method：

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

可用的 checked scalar method 包括：

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

<code>TryMultiplyDivide(a, b, divisor)</code> 使用 full-width intermediate 计算 <code>(a * b) / divisor</code>。如果最终结果可表示，但先做 wrapping multiplication 会丢失结果，应使用此方法。

### Clamp 与 Interpolation

<code>Lerp</code> 将 <code>t</code> clamp 到 <code>[0, 1]</code>。<code>LerpUnclamped</code> 允许 extrapolation（外推）。

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

Vector 与 quaternion interpolation 遵循相同命名规则：不带 suffix 的方法会 clamp <code>t</code>；带 <code>Unclamped</code> 的方法允许超出 unit interval。

### 舍入与平方根

| 方法 | 规则 |
| --- | --- |
| <code>Floor</code> | 向负无穷 |
| <code>Ceil</code> | 向正无穷 |
| <code>Round</code> | 最近整数；midpoint 远离零 |
| <code>Sqrt</code> | 定点平方根的 floor；负输入无效 |

~~~csharp
FPInt64 value = FPInt64.Parse("-1.5");

FPInt64 floor = FPInt64.Floor(value); // -2
FPInt64 ceil = FPInt64.Ceil(value);   // -1
FPInt64 round = FPInt64.Round(value); // -2
FPInt64 root = FPInt64.Sqrt(9);       // 3
~~~

## 失败策略

API 区分三种意图：

1. 用于模拟已证明范围的 wrapping operator；
2. 用于 programmer error 或 configuration error 的 fail-fast method；
3. 用于预期边界失败的 <code>Try*</code> method。

| 操作 | 失败行为 |
| --- | --- |
| <code>FromFloat</code>, <code>FromDouble</code> | 非 finite 或超出范围时抛 <code>ArgumentOutOfRangeException</code> |
| <code>Parse</code> | 文本无效或超范围时抛 <code>FormatException</code> |
| 除数或 remainder divisor 为零 | <code>DivideByZeroException</code> |
| <code>Abs(MinValue)</code>、不可表示的 <code>Ceil</code>/<code>Round</code> | <code>OverflowException</code> |
| 对负数执行 <code>Sqrt</code> | <code>ArgumentOutOfRangeException</code> |
| <code>Tan</code> 位于精确渐近线或超出 Q32.32 | <code>InvalidOperationException</code> |
| <code>Asin</code>/<code>Acos</code> 超出 <code>[-1, 1]</code> | <code>ArgumentOutOfRangeException</code> |
| 对无定义 vector 使用 <code>Normalized</code> | <code>InvalidOperationException</code> |
| 对无定义 vector 使用 <code>NormalizedOrZero</code> | 返回 zero |
| Quaternion normalization 或 inverse 无效 | <code>InvalidOperationException</code> |
| Quaternion 构造输入无效 | <code>ArgumentException</code> |
| Matrix inverse 为 singular 或不受支持 | <code>InvalidOperationException</code> |
| Projective point 无效 | <code>InvalidOperationException</code> |
| Shape constructor 输入无效 | <code>ArgumentException</code> 或 <code>ArgumentOutOfRangeException</code> |
| Ray 未命中、退化、shape 无效或数值失败 | <code>TryRay*</code> 返回 <code>false</code> 和 default output |
| Random stream 未初始化 | <code>InvalidOperationException</code> |
| Random range 无效 | <code>ArgumentOutOfRangeException</code> |
| Random state 全零 | <code>ArgumentException</code> |

调用 <code>Try*</code> 时，只能在 Boolean result 为 <code>true</code> 后消费 output。

## 向量

### 构造与坐标基

~~~csharp
FPVector2 input = new FPVector2(3, 4);
FPVector3 position = new FPVector3(10, 2, -5);

FPVector3 up = FPVector3.Up;
FPVector3 forward = FPVector3.Forward;
FPVector3 right = FPVector3.Right;
~~~

<code>FPVector2</code> 提供 <code>Zero</code>、<code>One</code>、<code>Right</code> 与 <code>Up</code>。<code>FPVector3</code> 还提供 <code>Down</code>、<code>Forward</code>、<code>Back</code> 与 <code>Left</code>。

两种 vector 都支持 value equality：

~~~csharp
bool same = new FPVector3(1, 2, 3) == new FPVector3(1, 2, 3);
bool different = FPVector2.Right != FPVector2.Up;
~~~

### Magnitude 与 Normalization

~~~csharp
FPVector3 velocity = new FPVector3(3, 4, 0);

FPInt64 squaredSpeed = velocity.SqrMagnitude; // 25
FPInt64 speed = velocity.Magnitude;           // 5
FPVector3 direction = velocity.Normalized;
~~~

根据领域策略选择 property：

~~~csharp
FPVector3 requiredDirection = source.Normalized;
FPVector3 optionalDirection = source.NormalizedOrZero;

if (!source.TryNormalize(out FPVector3 checkedDirection))
{
    // Reject an invalid command, authored value, or protocol payload.
}
~~~

- <code>Normalized</code> 要求 vector 非零且可归一化，否则 fail fast。
- <code>NormalizedOrZero</code> 显式选择 zero 作为 fallback。
- <code>TryNormalize</code> 把决定权交给调用方。

Magnitude 使用 scaled intermediate，避免大 vector 在平方时 wrap，也不会把 raw-1 micro vector 错判为 zero。当精确 squared result 无法放入 Q32.32 时，<code>SqrMagnitude</code> 与 <code>DistanceSqr</code> 饱和为 <code>FPInt64.MaxValue</code>。

### Dot、Cross、Projection 与 Reflection

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

Reflection 公式要求 unit normal。调用前应归一化 authored 或 calculated normal。Projection 接受非单位 target vector，但拒绝 zero target。

<code>Dot</code>、<code>Cross</code> 与普通 vector operator 使用 wrapping scalar arithmetic。范围未证明时使用相应 <code>Try*</code>。

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

## 三角函数与角度

<code>FPMath</code> 使用确定性 integer CORDIC 实现。输入与输出均为 radians。

~~~csharp
FPInt64 degrees = 45;
FPInt64 radians = degrees * FPInt64.Deg2Rad;

FPMath.SinCos(
    radians,
    out FPInt64 sin,
    out FPInt64 cos);
~~~

Output 顺序是 <code>sin</code>，然后是 <code>cos</code>。同时需要两个结果时使用 <code>SinCos</code>，从而共享一次 CORDIC pass。

### Tangent 与 Inverse Function

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

<code>Atan2</code> 参数顺序为 <code>(y, x)</code>，返回 <code>[-Pi, Pi]</code>。原点 <code>Atan2(0, 0)</code> 被定义为 zero。

<code>Tan</code> 在精确渐近线或 quotient 超出 Q32.32 时 fail fast；<code>TryTan</code> 用于预期失败。<code>Asin</code> 和 <code>Acos</code> 要求输入位于 <code>[-1, 1]</code>。

### 归一化角度

~~~csharp
FPInt64 signedAngle = FPMath.NormalizeAngle(angle);
FPInt64 positiveAngle = FPMath.NormalizeAnglePositive(angle);
~~~

- <code>NormalizeAngle</code> 返回 <code>[-Pi, Pi]</code>。
- <code>NormalizeAnglePositive</code> 返回 <code>[0, TwoPi)</code>。

## Quaternion 与 Euler Angle

### Axis-Angle 与 Vector Rotation

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

<code>AngleAxis</code> 会归一化 axis，并拒绝 zero axis。正角度遵循 right-hand rule（右手定则）。

Quaternion-vector operator 是面向 normalized quaternion 的 wrapping hot path：

~~~csharp
FPVector3 fastResult = yaw * FPVector3.Forward;
~~~

Quaternion 或 vector 来自不可信边界时使用 <code>TryRotate</code>；它会归一化 quaternion 并检查结果。

### Euler 构造

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

三个参数始终表示 X、Y、Z angle。<code>EulerOrder</code> 控制 intrinsic composition order，而不改变参数含义。可用顺序为 <code>XYZ</code>、<code>XZY</code>、<code>YXZ</code>、<code>YZX</code>、<code>ZXY</code> 和 <code>ZYX</code>。不指定 order 的 overload 使用 <code>ZXY</code>。

Euler triple 不唯一。接近 gimbal lock（万向节锁）时，应比较最终 rotation 或旋转后的 basis vector，而不是比较提取出的 angle component。

### Direction Constructor

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

当 supplied up vector 为 zero 或与 forward 共线时，<code>TryLookRotation</code> 使用确定性的 orthogonal reference。

### Normalize、Inverse 与比较

~~~csharp
FPQuaternion normalized = rotation.Normalized;

if (!normalized.TryInverse(out FPQuaternion inverse))
{
    throw new InvalidOperationException("Quaternion has no representable inverse.");
}

FPQuaternion identityRotation = normalized * inverse;
~~~

只有 unit quaternion 的 <code>Conjugate</code> 才等于 inverse。一般非零 quaternion 使用 <code>Inverse</code> 或 <code>TryInverse</code>。

Quaternion equality 比较 raw component。一个 quaternion 与其 negation 描述同一个空间旋转，但 raw value 不相等。需要比较 rotation equivalence（旋转等价性）时，应比较它们对 basis vector 的作用，或在明确 tolerance 下使用 quaternion dot 的绝对值。

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

- <code>Slerp</code> 提供 spherical interpolation，并 clamp <code>t</code>。
- <code>Nlerp</code> 提供 normalized linear interpolation，并 clamp <code>t</code>。
- <code>SlerpUnclamped</code> 与 <code>NlerpUnclamped</code> 允许 extrapolation。
- Interpolation 选择最短 quaternion arc。

使用 normalized rotation input。当不要求恒定 angular velocity 时，优先用成本更低的 <code>Nlerp</code>。

## 矩阵

<code>FPMatrix4x4</code> 使用 column vector。矩阵组合遵循：

~~~text
result = left * right
~~~

先应用右侧 matrix。

### TRS 与组合

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

两种写法都按 scale、rotation、translation 的顺序应用。<code>Scale</code> 支持 non-uniform scale（非均匀缩放）。

### 变换 Point 或 Direction

API 显式表达 homogeneous intent（齐次坐标意图）：

~~~csharp
FPVector3 localPoint = new FPVector3(1, 2, 3);
FPVector3 localDirection = FPVector3.Forward;

FPVector3 worldPoint = localToWorld.TransformPoint(localPoint);
FPVector3 worldDirection =
    localToWorld.TransformDirection(localDirection);
~~~

- <code>TransformPoint</code> 应用 affine 3x4 transform，包含 translation。
- <code>TransformDirection</code> 应用上方 3x3 transform，忽略 translation。
- <code>ProjectPoint</code> 执行 homogeneous transform，并除以 <code>w</code>。

同时提供 checked form：

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

Perspective 使用 right-handed view space（右手观察空间），可见点位于 negative Z，depth range 为 <code>[0, 1]</code>。它要求：

- <code>0 &lt; fovRadians &lt; Pi</code>；
- <code>aspect &gt; 0</code>；
- <code>near &gt; 0</code>；
- <code>far &gt; near</code>。

不要先执行 integer expression <code>16 / 9</code> 再转换；该表达式结果是 1。应像示例一样先转换至少一个 operand。

### Matrix Field 与 Inverse

Element 命名为 <code>M00</code> 到 <code>M33</code>，第一个数字是 row，第二个数字是 column。Indexer 同样接受 <code>[row, column]</code>。

~~~csharp
FPInt64 translationX = localToWorld.M03;
FPInt64 sameValue = localToWorld[0, 3];

if (!localToWorld.TryInverse(out FPMatrix4x4 worldToLocal))
{
    throw new InvalidOperationException("Matrix inverse is unsupported.");
}

FPVector3 restored = worldToLocal.TransformPoint(worldPoint);
~~~

<code>TryInverse</code> 拒绝 singular 和 near-singular matrix。接受 candidate 前，它还会使用 checked arithmetic 校验两个 multiplication order 是否都接近 identity。

## 2D 几何

### Shape 与 Invariant

~~~csharp
FPCircle circle = new FPCircle(
    new FPVector2(10, 0),
    FPInt64.FromInt(2));

FPAABB2D bounds = new FPAABB2D(
    new FPVector2(8, -3),
    new FPVector2(12, 3));
~~~

- Circle radius 必须非负。
- AABB 的每个 minimum component 必须小于或等于 maximum component。
- 边界接触计入 overlap 与 containment。

### Overlap 与 Containment

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

Circle distance decision 使用 full-width integer intermediate，不依赖已饱和的 public squared-magnitude display value。

### Ray Query

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

返回 <code>false</code> 时，<code>t</code> 保持 default。失败可能表示没有 forward hit、direction 退化，或 checked intermediate 无法表示。失败后不要消费 <code>t</code>。

### Closest Point

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

输入点位于 AABB 内部时，<code>ClosestPointOnAABB</code> 返回该点本身。输入恰好位于 circle center 时，circle query 在 X axis 上选择一个确定性点。

## 3D 几何

### Shape

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

- Sphere radius 必须非负。
- AABB minimum component 不能超过 maximum component。
- OBB half-extents 必须非负。
- OBB orientation 必须非零，并由 constructor 归一化。
- Default <code>FPOBB3D</code> 的 orientation 为零，因此无效。

### Query

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

OBB overlap 使用完整 15-axis separating-axis test（分离轴测试）。无效 OBB 输入会被拒绝。

### 3D Ray Query

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

所有 ray method 都是 <code>TryRay*</code>。失败时 output 保持 default。OBB ray query 使用 checked translation 与 rotation 将 ray 变换到 normalized OBB local space。

## Ray 参数语义

每个 ray result 都满足：

~~~text
point(t) = Origin + Direction * t
~~~

只有 <code>Direction</code> magnitude 为 1 时，返回的 <code>t</code> 才是 world-space distance。例如，将 direction 加倍会让同一点对应的 parameter 减半。

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

Ray query 接受 <code>t &gt;= 0</code> 的 forward intersection。当 origin 位于闭合 shape 内部时，结果是第一个 forward exit parameter，而不是 zero。

## 确定性随机数流

<code>DeterministicRandom</code> 实现 xoshiro256**。SplitMix64 expansion 将一个 <code>ulong</code> seed 展开为四个 state word。

~~~csharp
DeterministicRandom random =
    DeterministicRandom.Create(0xC0FFEEUL);

ulong raw = random.NextULong();
int cardIndex = random.NextInt(52);    // [0, 52)
int die = random.NextInt(1, 7);        // [1, 7)
FPInt64 unit = random.NextFP();        // [0, 1)
FPInt64 spread = random.NextFP(-1, 1); // [-1, 1)
~~~

所有 range maximum 都是 exclusive。Integer bounded sampling 使用 rejection sampling（拒绝采样），因此 mapping 无偏。

### 所有权

Generator 是 mutable struct。模拟 owner 必须保留自己推进的 instance。

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

Helper 需要推进 caller stream 时，使用 <code>ref</code> 传递：

~~~csharp
public static int RollInclusiveDie(
    ref DeterministicRandom random,
    int sideCount)
{
    return random.NextInt(1, sideCount + 1);
}
~~~

赋值 struct 会复制四个 state word，并创建一个有意的相同分支：

~~~csharp
DeterministicRandom branchA = random;
DeterministicRandom branchB = random;

bool firstValuesMatch =
    branchA.NextULong() == branchB.NextULong();
~~~

不要并发修改同一个 logical stream。并行模拟应为每个 stable owner 分配确定性 stream，并按定义好的顺序处理 owner。

### State 保存与恢复

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

四个 word 全零的 state 无效。<code>TryRestoreState</code> 会报告失败，且不会替换当前 state。

Random contract 使用以下标识：

~~~csharp
string algorithmId = DeterministicRandom.ALGORITHM_ID;
int algorithmVersion = DeterministicRandom.ALGORITHM_VERSION;
~~~

持久化 <code>S0</code>、<code>S1</code>、<code>S2</code>、<code>S3</code> 时必须同时保存这两个值。Replay 还要求 sampling call 的种类、顺序和次数一致。Bounded call 可能因 rejection sampling 重试而消费多个 raw output。

## Rollback 与 Resimulation

Rollback snapshot 必须保存继续模拟所需的每个 authoritative field，包括 random state。

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

Bounded history 可以使用 tick 作为 ring-buffer key：

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

Application 负责 entity existence、collection order、input history、resimulation 期间的 event suppression，以及 side-effect reconciliation。如果这些状态会影响后续 tick，只保存数值 snapshot 并不足够。

## 序列化契约

### 定点数值

显式序列化 signed raw value：

~~~csharp
long positionX = position.X.RawValue;
long positionY = position.Y.RawValue;
long positionZ = position.Z.RawValue;

FPVector3 restored = new FPVector3(
    FPInt64.FromRaw(positionX),
    FPInt64.FromRaw(positionY),
    FPInt64.FromRaw(positionZ));
~~~

Quaternion 与 matrix state 应按明确记录的顺序序列化每个 named component：

~~~text
Quaternion: X, Y, Z, W
Matrix: M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
        M30, M31, M32, M33
~~~

### Protocol 规则

所属 serializer 必须定义：

- schema identifier 与 schema version；
- signed integer encoding；
- byte order；
- field order；
- optional compression；
- payload length limit；
- integrity validation；
- unknown schema value 的处理方式；
- corruption 与 recovery policy。

不要序列化 private struct memory，不要依赖 runtime padding，不要保存 <code>GetHashCode()</code>，也不要让 authoritative value 经过 float 转换。通过 public constructor 重建带校验的 shape，从而拒绝无效 payload。

### Random State

持久化：

~~~text
ALGORITHM_ID
ALGORITHM_VERSION
S0
S1
S2
S3
~~~

恢复 stream 前校验 algorithm identity、algorithm version、payload schema 以及非零 state。

## Unity Adapter Pattern

Core 有意不暴露 <code>UnityEngine.Vector2</code>、<code>UnityEngine.Vector3</code>、<code>Quaternion</code>、<code>Matrix4x4</code>、<code>MonoBehaviour</code> 或 <code>ScriptableObject</code>。转换代码应位于 Unity-facing adapter assembly。

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

推荐数据流：

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

Authoring data 进入 authoritative state 前只转换一次。模拟结果转换为 Unity value 用于显示。只要 rendering interpolation 永不写回模拟，它可以继续使用浮点：

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

同步模拟使用显式 integer tick scheduler。Unity frame rate 与 <code>FixedUpdate</code> scheduling 属于表现或 orchestration；只有产品正式将其定义为 authoritative tick source 时才能例外。

## 性能

### 当前 Editor 实测

已提交的 performance suite 使用包含 10,240 个变化 operand 的确定性 array。每项 benchmark 执行 5 次 warmup 和 20 次 measurement，每次 measurement 执行一个 batch。Normalization 从该 dataset 处理 10,238 个 overlapping vector triple。Result 被写入 static sink，避免 dead-code removal。

以下结果记录于 Windows Unity Editor managed runtime：

| Batch | Median | Mean | 每个 measured sample 的 GC |
| --- | ---: | ---: | ---: |
| 10,240 次 Q32.32 multiplication | 0.2964 ms | 0.2996 ms | 0 B |
| 10,240 次 non-power-of-two division | 3.87965 ms | 4.260645 ms | 0 B |
| 10,240 次 square root | 9.08185 ms | 9.3754 ms | 0 B |
| 10,240 次 <code>SinCos</code> | 3.323 ms | 3.317385 ms | 0 B |
| 10,238 次 vector normalization | 22.77975 ms | 23.26167 ms | 0 B |

Warmed Core arithmetic allocation gate 通过 <code>GC.GetAllocatedBytesForCurrentThread()</code> 检查，其选定 steady-state batch 同样报告 zero bytes。

这些数值是当前机器与 Editor runtime 的开发证据，不是 Player、IL2CPP、Burst、mobile、console、server 或 cross-architecture 结果，也不是 release budget。

### 成本模型

- Addition、subtraction、comparison 与 raw conversion 是较小的 integer operation。
- Multiplication 使用 full-width integer decomposition。
- General division 与 square root 明显更昂贵。
- <code>SinCos</code> 使用 iterative CORDIC；需要两个结果时只调用一次。
- Vector normalization 组合 scaling、square root 与 division。
- Quaternion construction 与 interpolation 组合多次 vector 和 trigonometric operation。
- OBB overlap 最多检查 15 个 separating axis。
- Matrix inverse 适合 setup 或低频查询，不适合作为未审查的 inner loop。

### 热路径建议

1. 在 tick loop 外转换 constant 与 authored value。
2. 缓存不会改变的 normalized direction。
3. 当其 saturation domain 可接受时，用 squared distance 做排序或 threshold。
4. 大范围碰撞判定使用完整 geometry query。
5. 同时需要两个结果时共享一次 <code>SinCos</code>。
6. 只有在范围已确立后才使用 wrapping operator。
7. 不确定数据进入 inner loop 前用 <code>Try*</code> 校验。
8. 热路径避免 <code>ToString</code>、boxing、interface dispatch、LINQ 和 exception-driven control flow。
9. 避免无意复制大型 matrix 和 mutable random stream。
10. 使用代表性游戏数据测量真实 Player/backend。

即使选定 arithmetic batch 不分配，formatting、exception creation、boxing、caller collection、delegate 与 Unity adapter 仍可能分配。

## 所有权、生命周期与线程

| 资源 | Owner | 生命周期规则 |
| --- | --- | --- |
| Scalar/vector/quaternion/matrix/shape | Caller value | 可以复制；value immutable |
| Random stream | Simulation subsystem 或 stable entity | 保留并推进一个 owned mutable instance |
| Random checkpoint | Snapshot/replay owner | 持久化所有 state word 与 algorithm identity |
| CORDIC lookup data | Core static initialization | 初始化后 read-only |
| Native memory | 无 | 无需 dispose |
| Worker thread | 无 | Scheduling 属于 caller |
| Cancellation source | 无 | 无 shutdown path |

独立 immutable value 和独立 owned random stream 可以并发处理。对同一个 logical random stream 或 shared simulation container 的并发写入，需要 caller 定义同步与确定性 scheduling。

## 持久化行为

| 数据类别 | 模块行为 | 推荐 owner |
| --- | --- | --- |
| Project settings | 不创建文件或 asset | Project composition |
| User preferences | 不写入任何内容 | Application settings service |
| Runtime saves | 不写入任何内容 | 带 schema version 的 save service |
| Replay data | 不写入任何内容 | Replay recorder |
| Fixed-point snapshot | 只暴露 raw value | Networking 或 rollback layer |
| Random checkpoint | 暴露四个 state word | Replay 或 snapshot layer |
| Cache | 模块不持有 cache | 不适用 |

模块不使用 <code>PlayerPrefs</code>、<code>EditorPrefs</code>、<code>SessionState</code>、registry key、environment variable、scene、Prefab 或 ScriptableObject setting。磁盘上没有需要清理的 module-owned 内容。

## 目录与依赖结构

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

Core：

- 无 assembly reference；
- <code>noEngineReferences: true</code>；
- 无 unsafe code；
- 无 conditional compilation symbol；
- 无 service container dependency。

Editor correctness assembly 只引用 Core 与 Unity test assembly。只有 Unity Performance Testing package 满足 asmdef capability definition 时，performance assembly 才会参与。

## API 选择清单

按以下顺序做决定：

1. Source 已经是 authoritative raw value？使用 <code>FromRaw</code>。
2. 它是 authored integer？使用 <code>FromInt</code> 或 implicit <code>int</code>。
3. 它是人类可读 decimal configuration？使用 <code>Parse</code>/<code>TryParse</code>。
4. 它位于 Unity 或 tooling boundary？校验后使用 <code>FromFloat</code>/<code>FromDouble</code>。
5. Arithmetic range 可以证明？在 hot path 使用 operator。
6. Input 或 scale 可能失败？使用 <code>Try*</code>。
7. Interpolation 必须停留在 endpoint 之间？使用 clamped method。
8. 有意 extrapolation？使用 <code>Unclamped</code> method。
9. Zero vector 是合法 fallback？使用 <code>NormalizedOrZero</code>。
10. Zero 必须被拒绝？使用 <code>Normalized</code> 或 <code>TryNormalize</code>。
11. 正在变换 point、direction 还是 projected point？选择对应显式 matrix method。
12. Ray <code>t</code> 需要表示 distance？先归一化 direction。
13. Random call 会影响 replay？保存并恢复所属 stream state。

## 常见错误

- 把 <code>RAW_ONE</code> 当作 <code>FPInt64</code> value，或把 <code>One</code> 当作 raw <code>long</code>。
- 把 raw integer 传给 <code>FromInt</code>，而不是 <code>FromRaw</code>。
- 在定点转换前先执行 <code>16 / 9</code> 这类 integer division。
- 期待普通 operator 报告 overflow。
- Zero 是可接受 fallback 时调用 <code>Normalized</code>。
- Zero 表示 authored 或 protocol data 无效时调用 <code>NormalizedOrZero</code>。
- 向 reflection 提供非 unit normal。
- 颠倒 <code>SinCos</code> output order。
- 颠倒 <code>Atan2(y, x)</code>。
- 向 radians API 传 degrees。
- 比较 Euler triple，而不是 rotation。
- 把 quaternion raw equality 当作 spatial-rotation equality。
- 对 direction 使用 <code>TransformPoint</code>。
- 对 position 使用 <code>TransformDirection</code>。
- 需要 perspective divide 时使用 <code>TransformPoint</code>。
- 读取失败后的 <code>TryRay*</code> output。
- Direction 非 unit 时把 ray <code>t</code> 当作 distance。
- 构造 reversed AABB、negative radius 或 negative extent。
- 把 default OBB 当作有效值。
- 推进 <code>DeterministicRandom</code> 的副本。
- 在不确定 job scheduling 中共享一个 random stream。
- 持久化 random word 时不保存 algorithm ID 与 algorithm version。
- 序列化 struct memory，而不是 named raw field。
- 把 presentation float 写回 authoritative state。

## 验证

### Unity Editor

1. 使用 <code>ProjectSettings/ProjectVersion.txt</code> 声明的 Unity 版本打开 <code>&lt;repo-root&gt;/UnityStarter</code>。
2. 打开 <strong>Window &gt; General &gt; Test Runner</strong>。
3. 选择 EditMode。
4. 运行 <code>CycloneGames.DeterministicMath.Tests.Editor</code>。
5. 如果已安装 Unity Performance Testing，运行 <code>CycloneGames.DeterministicMath.Tests.Performance</code>。
6. 确认 active consumer assembly 没有编译错误。

### Batch Mode

~~~text
<Unity-executable> -batchmode -nographics -quit \
  -projectPath <repo-root>/UnityStarter \
  -runTests -testPlatform EditMode \
  -assemblyNames CycloneGames.DeterministicMath.Tests.Editor \
  -testResults <repo-root>/Artifacts/DeterministicMath.EditMode.xml \
  -logFile <repo-root>/Artifacts/DeterministicMath.EditMode.log
~~~

运行命令前创建 artifact directory。

### 生产验收

将模块用作 authoritative cross-process contract 前：

- 对 minimum、maximum、negative、fractional、vector、quaternion 与 matrix raw value 做 round-trip；
- 分别关闭和启用 overflow checking 编译 Core，并对两个 build 运行同一套正确性测试；
- 在每个支持的 runtime 与 architecture 比较 raw golden vector；
- 校验 overflow、zero、invalid-domain 与每个 <code>Try*</code> path；
- 校验 clamped 与 unclamped interpolation；
- 校验全部六种 Euler order、gimbal-lock case 与 basis-vector rotation；
- 校验 affine point、direction、projective point 与 inverse 行为；
- 校验大范围 circle、sphere、AABB、OBB、closest-point 与 ray query；
- 校验 RNG seed expansion、golden sequence、bounded range 与 state restore；
- Capture 并 replay 一个代表性 rollback window；
- Resimulation 后比较完整 authoritative snapshot；
- 使用生产规模数据 profile 实际 Player/backend。

Release claim 应明确写出已验证的具体 platform、runtime、test corpus、raw contract 与 performance budget。
