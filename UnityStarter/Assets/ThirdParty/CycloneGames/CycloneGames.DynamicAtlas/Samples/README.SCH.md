# CycloneGames.DynamicAtlas 示例

[English](README.md) | 简体中文

示例位于 `CycloneGames.DynamicAtlas.Samples`（`autoReferenced: false`），需要宿主程序集显式引用。

## 运行租约示例

1. 创建或选择一个带 `SpriteRenderer` 的 GameObject。
2. 添加 `DynamicAtlasLeaseSample`。
3. 指定 **Target Renderer** 与一个矩形的 **Source Sprite**。
4. 如果源精灵属于某个 `SpriteAtlas`，关闭该图集的 rotation 与 Tight Packing，或在 `CycloneGames.AtlasPipeline` 规则上启用 Runtime Extraction 预设。
5. 保留默认稳定键 `samples/dynamic-atlas/icon`，或换成带命名空间的 content identity。
6. 进入 Play Mode；禁用再启用组件，观察释放与重新获取。
7. 打开 `Tools > CycloneGames > Dynamic Atlas > Dynamic Atlas Debugger`，查看页、租约、拷贝路径与估算纹理字节数。

`Awake` 创建 `DynamicAtlasService`：512 像素单页、64 个条目、2 MiB 纹理预算、`RemoveWhenUnused`、`AllowSynchronousReadback`。`OnEnable` 获取一个 `DynamicAtlasSpriteLease`；`OnDisable` 释放；`OnDestroy` 销毁服务。

拷贝路径、保留策略、诊断与容量规划见[动态图集指南](../Documents~/DynamicAtlas.SCH.md)。
