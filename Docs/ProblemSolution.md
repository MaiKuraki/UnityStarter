## Problem Solution
如果你的项目中遇到了以下报错 (Unity6000.0.30) 中 
```
Trying to use a texture (_CameraDepthTexture) that was already released or not yet created. Make sure you declare it for reading in your pass or you don't read it before it's been written to at least once.
```
尝试以下设置解决报错以及打包后 UI Camera 黑屏问题(此 Bug Unity 官方声称在 Unity 6000.0.36 中解决)
```
Try to set the “Camera->Rendering->Anti-aliasing” and “Universial-Rende-rPipneline-Asset->Quality->Anti-Aliasing(MSAA)” to deactive. This issue is solved after Unity 6000.0.36
```
同样的问题打包后 UICamera 导致黑屏，出现在 (Unity6000.0.32)中，编辑器不报错，但是打包后 Persistent 路径下 Log 记录(此 Bug Unity 官方声称在 Unity 6000.0.36 中解决)。

```
RenderPass: Attachment 0 was created with 1 samples but 4 samples were requested.
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderSingleCamera(ScriptableRenderContext, UniversalCameraData)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderCameraStack(ScriptableRenderContext, Camera)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:Render(ScriptableRenderContext, List`1)
UnityEngine.Rendering.RenderPipelineManager:DoRenderLoop_Internal(RenderPipelineAsset, IntPtr, Object)

[ line 1090520]

NextSubPass: Not inside a Renderpass
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderSingleCamera(ScriptableRenderContext, UniversalCameraData)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderCameraStack(ScriptableRenderContext, Camera)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:Render(ScriptableRenderContext, List`1)
UnityEngine.Rendering.RenderPipelineManager:DoRenderLoop_Internal(RenderPipelineAsset, IntPtr, Object)

[ line 1091112]

EndRenderPass: Not inside a Renderpass
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderSingleCamera(ScriptableRenderContext, UniversalCameraData)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderCameraStack(ScriptableRenderContext, Camera)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:Render(ScriptableRenderContext, List`1)
UnityEngine.Rendering.RenderPipelineManager:DoRenderLoop_Internal(RenderPipelineAsset, IntPtr, Object)

[ line 1091112]

RenderPass: Attachment 0 was created with 1 samples but 4 samples were requested.
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderSingleCamera(ScriptableRenderContext, UniversalCameraData)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:RenderCameraStack(ScriptableRenderContext, Camera)
UnityEngine.Rendering.Universal.UniversalRenderPipeline:Render(ScriptableRenderContext, List`1)
UnityEngine.Rendering.RenderPipelineManager:DoRenderLoop_Internal(RenderPipelineAsset, IntPtr, Object)
```
同样使用这个办法可以解决
```
Try to set the “Camera->Rendering->Anti-aliasing” and “Universial-Rende-rPipneline-Asset->Quality->Anti-Aliasing(MSAA)” to deactive.This issue is solved after Unity 6000.0.36
```