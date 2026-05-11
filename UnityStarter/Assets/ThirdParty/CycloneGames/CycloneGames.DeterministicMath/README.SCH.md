# CycloneGames.DeterministicMath

[English](./README.md) | 简体中文

跨平台确定性定点数学库。Q32.32 定点运算、CORDIC 三角函数、四元数旋转、3D 矩阵变换和碰撞检测——所有结果在所有平台上**位级一致**。

## 特性

- **Q32.32 定点运算** — 64 位 `FPInt64`，范围 ±21 亿，精度 ~2.3e-10；Floor/Ceil/Round；快速除法（2 的幂次路径）
- **CORDIC 三角函数** — `Sin`、`Cos`、`Tan`、`Atan`、`Atan2`、`Asin`、`Acos`，32 迭代 CORDIC 算法，零浮点
- **向量数学** — `FPVector2`、`FPVector3`，含点积、叉积、距离、归一化、反射、投影
- **四元数旋转** — `FPQuaternion`，支持 6 种 Euler 顺序（ZXY/Unity、YXZ/Godot、ZYX/Unreal 等），Slerp/Nlerp、ToEuler
- **4×4 矩阵** — `FPMatrix4x4`：Translate、Scale、Rotate、TRS、Perspective、Inverse、行列式
- **2D 碰撞** — Circle、AABB、Ray 图元 + 重叠测试 + 射线投射
- **3D 碰撞** — Sphere、AABB、OBB、Ray 图元 + SAT 重叠 + 射线投射
- **确定性随机数** — xoshiro256** struct（零 GC），支持 SaveState/RestoreState 用于回滚
- **纯 C# Core** — `noEngineReferences: true`，零 Unity 依赖，netstandard2.0
- **零 GC** — 所有类型为 `readonly struct`，热路径无堆分配，含随机数生成器

## 架构

```text
CycloneGames.DeterministicMath.Core   ← 纯 C#，引擎无关
```

无需 Unity 适配层——本模块除日志外无平台特定行为。日志通过静态委托桥接，默认写入 Console。

### 跨引擎兼容性

核心数学（乘法、Slerp、向量旋转）是引擎无关的——在 Unity、Godot、Unreal 或独立 .NET 服务端结果完全一致。仅静态构造方法编码了引擎特定约定：

| 构造方法 | 默认约定 | 跨引擎用法 |
| --- | --- | --- |
| `Euler(pitch, yaw, roll)` | Unity ZXY、Y-up、左手 | Godot: `Euler(pitch, yaw, roll, EulerOrder.YXZ)` |
| `LookRotation(forward, up)` | Unity（forward = +Z, up = +Y） | Godot: 传入 `-godotForward` |
| `AngleAxis(angle, axis)` | 引擎无关 | 始终正确 |
| 四元数数学（×, Slerp） | 引擎无关 | 始终正确 |

**Godot 示例:**

```csharp
// Godot 使用 YXZ 顺序，右手坐标系，Y-up，forward = -Z
var rot = FPQuaternion.Euler(pitch, yaw, roll, EulerOrder.YXZ);
var lookRot = FPQuaternion.LookRotation(-godotForward, godotUp);
```

## 快速上手

### 定点数

```csharp
using CycloneGames.DeterministicMath;

var a = FPInt64.FromFloat(3.14f);
var b = FPInt64.FromInt(2);
var result = a * b;              // 6.28 (Q32.32)

// 取整
var floor = FPInt64.Floor(a);     // 3
var ceil = FPInt64.Ceil(a);       // 4
var round = FPInt64.Round(a);     // 3
```

### 三角函数

```csharp
var angle = FPInt64.Pi / 3;      // 60 度
var sin = FPMath.Sin(angle);     // ~0.866
var cos = FPMath.Cos(angle);     // ~0.5

// 一次 CORDIC 同时计算 Sin 和 Cos（节省一半开销）
FPMath.SinCos(angle, out var s, out var c);
```

### 四元数旋转

```csharp
// Unity ZXY（默认）
var rot = FPQuaternion.Euler(FPInt64.Pi / 2, 0, 0); // 90° pitch
var rotated = rot * new FPVector3(1, 0, 0);           // (1, 0, 0) → (1, 0, 1)

// 反解欧拉角
var euler = rot.ToEuler();  // (~1.57, 0, 0)

// 指定其他引擎的旋转顺序
var godotRot = FPQuaternion.Euler(pitch, yaw, roll, EulerOrder.YXZ);
```

### 矩阵变换

```csharp
var mat = FPMatrix4x4.TRS(translation, rotation, scale);
var worldPoint = mat * localPoint;
var invMat = mat.Inverse;             // 逆矩阵（View 矩阵用）
var det = mat.Determinant();          // 行列式
```

### 2D/3D 碰撞

```csharp
// 2D
var circle = new FPCircle(new FPVector2(0, 0), 5);
var aabb = new FPAABB(new FPVector2(3, 3), new FPVector2(8, 8));
bool hit = FPCollision2D.CircleAABBOverlap(circle, aabb);

// 3D
var sphere = new FPSphere(FPVector3.Zero, 5);
var bounds = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
var ray = new FPRay(new FPVector3(-1, 2, 2), new FPVector3(1, 0, 0));
var t = FPRaycast3D.RayBounds(ray, bounds);

// OBB 碰撞 (SAT 15 分离轴)
bool obbHit = FPRaycast3D.OBBOverlap(obbA, obbB);
```

### 确定性随机数

```csharp
var rand = DeterministicRandom.Create(12345UL);
var value = rand.NextFP();                        // [0, 1)

// 保存/恢复状态用于回滚
var state = rand.SaveState();
rand.RestoreState(state);
```

## API 参考

### FPInt64

| 成员 | 说明 |
| --- | --- |
| `RawValue` | 底层 `long` 原始位 |
| `FromInt/Float/Double` | 从标准类型转换 |
| `ToInt/ToFloat/ToDouble` | 转为标准类型 |
| `Sqrt(v)` | 牛顿迭代法，8 iterations |
| `Floor/Ceil/Round(v)` | 取整（下、上、四舍五入） |
| `Abs/Min/Max/Clamp/Lerp` | 标准数学函数 |

### FPMath

| 方法 | 说明 |
| --- | --- |
| `Sin/Cos(angle)` | CORDIC 旋转模式，32 iterations |
| `SinCos(angle, out s, out c)` | 合并调用，节省一半开销 |
| `Tan(angle)` | Sin/Cos，含渐近线检测 |
| `Atan(y)/Atan2(y, x)` | CORDIC 向量模式 |
| `Asin(x)/Acos(x)` | Atan2 + Sqrt，定义域 [-1, 1] |
| `NormalizeAngle(a)` | 规约到 [-π, π] |

### FPQuaternion

| 成员 | 说明 |
| --- | --- |
| `AngleAxis(angle, axis)` | 轴角旋转（引擎无关） |
| `Euler(pitch, yaw, roll)` | Unity ZXY（默认） |
| `Euler(a, b, c, EulerOrder)` | 显式旋转顺序 |
| `LookRotation(forward, up)` | 方向→旋转 |
| `Slerp/Nlerp(a, b, t)` | 球面/归一化线性插值 |
| `ToEuler(order)` | 反解欧拉角 |
| `q * p`（运算符） | 旋转向量 |
| `q1 * q2`（运算符） | Hamilton 乘积 |

### EulerOrder

| 值 | 引擎 |
| --- | --- |
| `ZXY` | Unity（默认） |
| `YXZ` | Godot |
| `ZYX` | Unreal |
| `XYZ`, `XZY`, `YZX` | 其他自定义 |

### FPMatrix4x4

| 成员 | 说明 |
| --- | --- |
| `Identity` / `Zero` | 静态默认值 |
| `Translate/Scale/Rotate` | 变换矩阵 |
| `TRS(t, r, s)` | 平移×旋转×缩放 |
| `Perspective(...)` | 投影矩阵 |
| `Inverse` | 逆矩阵（View 矩阵用） |
| `Determinant()` | 标量行列式 |
| `m * m` / `m * v` | 乘法 / 点变换 |

### FPRaycast3D / 3D 图元

| 类/方法 | 说明 |
| --- | --- |
| `FPSphere/Center/Radius` | 球体 |
| `FPBounds/Min/Max/Extents` | AABB |
| `FPOBB/Center/HalfExtents/Orientation` | 朝向包围盒 |
| `FPRay/Origin/Direction` | 射线 |
| `RaySphere/RayBounds/RayOBB` | 3D 射线投射 |
| `OBBOverlap` | SAT 15 轴 OBB 重叠 |
| `SphereBoundsOverlap` | 球-AABB 重叠 |

### FPVector2 / FPVector3

| 新增方法 | 说明 |
| --- | --- |
| `Reflect(v, normal)` | 镜面反射 |
| `Project(v, onto)` | 向量投影 |
| `-(v)`（一元负号） | 取反 |

## 性能说明

- 所有类型为 `readonly struct`——每次操作零堆分配（含 `DeterministicRandom`）
- `SinCos` 复用一次 CORDIC 迭代；单独调用 `Sin` 和 `Cos` 成本翻倍
- 常量除法（`/2`、`/4` 等 2 的幂次）走 de Bruijn 位扫描快速路径
- Slerp 成本 ~1 Acos + 3 SinCos；Nlerp 仅需 1 Normalize（约 5-8x 更快）
- OBB 碰撞使用 SAT 15 轴，跳过近平行边的无效测试
- 矩阵 Inverse 在行列式 < ~2.3e-8 时返回 Identity（退化防护）
- 运行 `FPBenchmarkTests` 获取本机性能基线

## 模块文件索引

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
    ├── FPGeometry3DTests.cs
    └── FPBenchmarkTests.cs
```
