#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIPerformanceAuditUtility
    {
        internal enum AuditSeverity
        {
            Info = 0,
            Warning = 1,
            Error = 2
        }

        internal readonly struct AuditIssue
        {
            public readonly AuditSeverity Severity;
            public readonly string Message;

            public AuditIssue(AuditSeverity severity, string message)
            {
                Severity = severity;
                Message = message;
            }
        }

        internal sealed class AuditReport
        {
            public GameObject Prefab;
            public int GraphicsCount;
            public int RaycastTargets;
            public int NonInteractiveRaycastTargets;
            public int LayoutGroupCount;
            public int ContentSizeFitterCount;
            public int MaskCount;
            public int RectMaskCount;
            public int AnimatorCount;
            public int AnimationCount;
            public int ScrollRectCount;
            public int CanvasCount;
            public int NestedCanvasCount;
            public int MaterialVariantCount;
            public int TextureVariantCount;
            public int LayoutFitterComboCount;
            public bool HasTextMeshPro;
            public UIWindowConfiguration.SubCanvasPolicy SuggestedSubCanvasPolicy;
            public readonly List<AuditIssue> Issues = new List<AuditIssue>(12);

            public bool HasIssues => Issues.Count > 0;
            public AuditSeverity HighestSeverity
            {
                get
                {
                    AuditSeverity highest = AuditSeverity.Info;
                    for (int i = 0; i < Issues.Count; i++)
                    {
                        if (Issues[i].Severity > highest) highest = Issues[i].Severity;
                    }
                    return highest;
                }
            }
            public int ErrorCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < Issues.Count; i++)
                    {
                        if (Issues[i].Severity == AuditSeverity.Error) count++;
                    }
                    return count;
                }
            }
            public int WarningCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < Issues.Count; i++)
                    {
                        if (Issues[i].Severity == AuditSeverity.Warning) count++;
                    }
                    return count;
                }
            }
            public int InfoCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < Issues.Count; i++)
                    {
                        if (Issues[i].Severity == AuditSeverity.Info) count++;
                    }
                    return count;
                }
            }
        }

        private static readonly List<Component> ComponentScratch = new List<Component>(256);
        private static readonly HashSet<Object> UniqueMaterialScratch = new HashSet<Object>();
        private static readonly HashSet<Object> UniqueTextureScratch = new HashSet<Object>();

        public static AuditReport AuditWindowConfiguration(UIWindowConfiguration config)
        {
            return AuditPrefab(ResolveInspectionPrefab(config));
        }

        public static AuditReport AuditPrefab(GameObject prefab)
        {
            if (prefab == null) return null;

            AuditReport report = new AuditReport { Prefab = prefab };
            ComponentScratch.Clear();
            UniqueMaterialScratch.Clear();
            UniqueTextureScratch.Clear();

            prefab.GetComponentsInChildren(true, ComponentScratch);

            for (int i = 0; i < ComponentScratch.Count; i++)
            {
                Component component = ComponentScratch[i];
                if (component == null) continue;

                if (component is Graphic graphic)
                {
                    report.GraphicsCount++;
                    if (graphic.raycastTarget)
                    {
                        report.RaycastTargets++;
                        if (!IsLikelyInteractiveGraphic(graphic))
                        {
                            report.NonInteractiveRaycastTargets++;
                        }
                    }

                    if (graphic.material != null)
                    {
                        UniqueMaterialScratch.Add(graphic.material);
                    }

                    if (graphic.mainTexture != null)
                    {
                        UniqueTextureScratch.Add(graphic.mainTexture);
                    }
                }
                else if (component is LayoutGroup)
                {
                    report.LayoutGroupCount++;
                    if (component.GetComponent<ContentSizeFitter>() != null)
                    {
                        report.LayoutFitterComboCount++;
                    }
                }
                else if (component is ContentSizeFitter)
                {
                    report.ContentSizeFitterCount++;
                    if (component.GetComponent<LayoutGroup>() != null)
                    {
                        report.LayoutFitterComboCount++;
                    }
                }
                else if (component is Mask)
                {
                    report.MaskCount++;
                }
                else if (component is RectMask2D)
                {
                    report.RectMaskCount++;
                }
                else if (component is Animator)
                {
                    report.AnimatorCount++;
                }
                else if (component is Animation)
                {
                    report.AnimationCount++;
                }
                else if (component is ScrollRect)
                {
                    report.ScrollRectCount++;
                }
                else if (component is Canvas)
                {
                    report.CanvasCount++;
                    if (component.gameObject != prefab)
                    {
                        report.NestedCanvasCount++;
                    }
                }
                else
                {
                    string typeName = component.GetType().Name;
                    if (typeName == "TextMeshProUGUI" || typeName == "TMP_InputField" || typeName == "TMP_Dropdown")
                    {
                        report.HasTextMeshPro = true;
                    }
                }
            }

            report.LayoutFitterComboCount /= 2;
            report.MaterialVariantCount = UniqueMaterialScratch.Count;
            report.TextureVariantCount = UniqueTextureScratch.Count;
            report.SuggestedSubCanvasPolicy = DetermineSuggestedPolicy(report);

            PopulateIssues(report);

            ComponentScratch.Clear();
            UniqueMaterialScratch.Clear();
            UniqueTextureScratch.Clear();
            return report;
        }

        public static GameObject ResolveInspectionPrefab(UIWindowConfiguration config)
        {
            if (config == null) return null;

            if (config.Source == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                return config.WindowPrefab != null ? config.WindowPrefab.gameObject : null;
            }

            string location = config.EffectiveLocation;
            if (string.IsNullOrEmpty(location) || !location.StartsWith("Assets/", System.StringComparison.Ordinal))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(location);
        }

        private static UIWindowConfiguration.SubCanvasPolicy DetermineSuggestedPolicy(AuditReport report)
        {
            bool hasHighChurn =
                report.AnimatorCount > 0 ||
                report.AnimationCount > 0 ||
                report.ScrollRectCount > 0 ||
                report.LayoutGroupCount > 0 ||
                report.ContentSizeFitterCount > 0 ||
                report.MaskCount > 0 ||
                report.RectMaskCount > 0;

            if (!hasHighChurn)
            {
                return UIWindowConfiguration.SubCanvasPolicy.InheritLayerCanvas;
            }

            if (report.ScrollRectCount > 0 || report.LayoutGroupCount > 0 || report.ContentSizeFitterCount > 0)
            {
                return UIWindowConfiguration.SubCanvasPolicy.AutoDetect;
            }

            return UIWindowConfiguration.SubCanvasPolicy.AutoDetect;
        }

        private static void PopulateIssues(AuditReport report)
        {
            if (report.LayoutFitterComboCount > 0)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"Detected {report.LayoutFitterComboCount} object(s) combining LayoutGroup and ContentSizeFitter. This is a common LayoutRebuild hotspot."));
            }

            if (report.LayoutGroupCount + report.ContentSizeFitterCount >= 3)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"Layout-driven components are dense ({report.LayoutGroupCount} LayoutGroup, {report.ContentSizeFitterCount} ContentSizeFitter). Consider reducing rebuild depth or isolating this window with a sub-canvas."));
            }

            if (report.MaskCount + report.RectMaskCount >= 2)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"Multiple masks detected ({report.MaskCount} Mask, {report.RectMaskCount} RectMask2D). This increases clipping cost and can fragment batching."));
            }

            if (report.MaterialVariantCount >= 3)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"Detected {report.MaterialVariantCount} distinct Graphic materials. This is a likely Batch / SetPass risk."));
            }

            if (report.TextureVariantCount >= 4)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    $"Detected {report.TextureVariantCount} distinct Graphic textures. Consider atlasing or material consolidation if this window is frequently visible."));
            }

            if (report.NonInteractiveRaycastTargets >= 6)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"Detected {report.NonInteractiveRaycastTargets} likely non-interactive Graphics with raycastTarget enabled. This can add avoidable UI raycast cost."));
            }

            if (report.ScrollRectCount > 0 && report.NestedCanvasCount == 0)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    "ScrollRect detected without a nested canvas in the prefab. AutoDetect or ForceOwnSubCanvas is usually safer for rebuild isolation."));
            }

            if (report.CanvasCount > 1)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    $"Detected {report.CanvasCount} Canvas components ({report.NestedCanvasCount} nested). This may already isolate rebuilds, but it can also fragment batching."));
            }

            if (report.GraphicsCount >= 80)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    $"High Graphic count detected ({report.GraphicsCount}). Large windows benefit from careful raycast, layout, and atlas discipline."));
            }
        }

        private static bool IsLikelyInteractiveGraphic(Graphic graphic)
        {
            if (graphic == null) return false;
            if (graphic.GetComponent<Selectable>() != null) return true;
            if (graphic.GetComponent<ScrollRect>() != null) return true;
            if (graphic.GetComponent<UnityEngine.EventSystems.EventTrigger>() != null) return true;

            string typeName = graphic.GetType().Name;
            return typeName == "TMP_InputField" || typeName == "TMP_Dropdown";
        }
    }
}
#endif
