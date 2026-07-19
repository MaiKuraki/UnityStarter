using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Editor
{
    internal sealed class UIPerformanceAuditUtility
    {
        internal enum AuditSeverity
        {
            Info = 0,
            Warning = 1,
            Error = 2,
        }

        internal readonly struct AuditIssue
        {
            public AuditIssue(AuditSeverity severity, string message)
            {
                Severity = severity;
                Message = message;
            }

            public AuditSeverity Severity { get; }
            public string Message { get; }
        }

        internal sealed class AuditReport
        {
            public GameObject Prefab;
            public int GraphicsCount;
            public int RaycastTargetCount;
            public int LikelyDecorativeRaycastTargetCount;
            public int LayoutGroupCount;
            public int ContentSizeFitterCount;
            public int LayoutAuthorityConflictCount;
            public int MaskCount;
            public int RectMaskCount;
            public int ScrollRectCount;
            public int CanvasCount;
            public int NestedCanvasCount;
            public int AnimatorCount;
            public int MaterialCount;
            public int TextureCount;
            public readonly List<AuditIssue> Issues = new List<AuditIssue>(8);

            public AuditSeverity HighestSeverity
            {
                get
                {
                    AuditSeverity result = AuditSeverity.Info;
                    for (int i = 0; i < Issues.Count; i++)
                    {
                        if (Issues[i].Severity > result)
                        {
                            result = Issues[i].Severity;
                        }
                    }

                    return result;
                }
            }

            public int WarningCount => Count(AuditSeverity.Warning);
            public int ErrorCount => Count(AuditSeverity.Error);

            private int Count(AuditSeverity severity)
            {
                int count = 0;
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == severity)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private readonly List<Component> _componentBuffer = new List<Component>(256);
        private readonly HashSet<UnityEngine.Object> _materialBuffer =
            new HashSet<UnityEngine.Object>();
        private readonly HashSet<UnityEngine.Object> _textureBuffer =
            new HashSet<UnityEngine.Object>();

        public AuditReport Audit(UIWindowConfiguration configuration)
        {
            return Audit(ResolveInspectionPrefab(configuration));
        }

        public AuditReport Audit(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            var report = new AuditReport { Prefab = prefab };
            _componentBuffer.Clear();
            _materialBuffer.Clear();
            _textureBuffer.Clear();

            try
            {
                prefab.GetComponentsInChildren(true, _componentBuffer);
                for (int i = 0; i < _componentBuffer.Count; i++)
                {
                    Component component = _componentBuffer[i];
                    if (component == null)
                    {
                        continue;
                    }

                    if (component is Graphic graphic)
                    {
                        InspectGraphic(graphic, report);
                    }

                    if (component is LayoutGroup)
                    {
                        report.LayoutGroupCount++;
                    }

                    if (component is ContentSizeFitter)
                    {
                        report.ContentSizeFitterCount++;
                    }

                    if (component is LayoutGroup && component.GetComponent<ContentSizeFitter>() != null)
                    {
                        report.LayoutAuthorityConflictCount++;
                    }

                    if (component is Mask)
                    {
                        report.MaskCount++;
                    }
                    else if (component is RectMask2D)
                    {
                        report.RectMaskCount++;
                    }

                    if (component is ScrollRect)
                    {
                        report.ScrollRectCount++;
                    }

                    if (component is Canvas canvas)
                    {
                        report.CanvasCount++;
                        if (canvas.gameObject != prefab)
                        {
                            report.NestedCanvasCount++;
                        }
                    }

                    if (component is Animator)
                    {
                        report.AnimatorCount++;
                    }
                }

                report.MaterialCount = _materialBuffer.Count;
                report.TextureCount = _textureBuffer.Count;
                PopulateIssues(report);
                return report;
            }
            finally
            {
                _componentBuffer.Clear();
                _materialBuffer.Clear();
                _textureBuffer.Clear();
            }
        }

        public static GameObject ResolveInspectionPrefab(UIWindowConfiguration configuration)
        {
            if (configuration == null)
            {
                return null;
            }

            if (configuration.Source == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                return configuration.WindowPrefab != null
                    ? configuration.WindowPrefab.gameObject
                    : null;
            }

            string assetPath = string.Empty;
            if (configuration.Source == UIWindowConfiguration.PrefabSource.AssetReference)
            {
                UIAssetReference reference = configuration.PrefabAssetReference;
                if (!string.IsNullOrEmpty(reference.EditorGuid))
                {
                    assetPath = AssetDatabase.GUIDToAssetPath(reference.EditorGuid);
                }

                if (string.IsNullOrEmpty(assetPath) &&
                    reference.Location.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    assetPath = reference.Location;
                }
            }
            else if (configuration.Source == UIWindowConfiguration.PrefabSource.PathLocation &&
                     configuration.PrefabLocation.StartsWith("Assets/", StringComparison.Ordinal))
            {
                assetPath = configuration.PrefabLocation;
            }

            return string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        private void InspectGraphic(Graphic graphic, AuditReport report)
        {
            report.GraphicsCount++;
            if (graphic.raycastTarget)
            {
                report.RaycastTargetCount++;
                if (!IsLikelyInteractive(graphic))
                {
                    report.LikelyDecorativeRaycastTargetCount++;
                }
            }

            Material material = graphic.material;
            if (material != null)
            {
                _materialBuffer.Add(material);
            }

            Texture texture = graphic.mainTexture;
            if (texture != null)
            {
                _textureBuffer.Add(texture);
            }
        }

        private static bool IsLikelyInteractive(Graphic graphic)
        {
            return graphic.GetComponent<Selectable>() != null ||
                   graphic.GetComponent<ScrollRect>() != null ||
                   graphic.GetComponent<EventTrigger>() != null;
        }

        private static void PopulateIssues(AuditReport report)
        {
            if (report.LayoutAuthorityConflictCount > 0)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"{report.LayoutAuthorityConflictCount} object(s) combine LayoutGroup and ContentSizeFitter. Confirm the layout authority and profile rebuild behavior."));
            }

            if (report.LikelyDecorativeRaycastTargetCount >= 6)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"{report.LikelyDecorativeRaycastTargetCount} likely decorative Graphics accept raycasts. Review them before disabling raycastTarget."));
            }

            if (report.MaterialCount >= 3)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"{report.MaterialCount} Graphic materials are present. Verify SetPass and batching cost with Frame Debugger."));
            }

            if (report.MaskCount + report.RectMaskCount >= 2)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Warning,
                    $"{report.MaskCount + report.RectMaskCount} mask components are present. Verify clipping and batching cost on target hardware."));
            }

            if (report.TextureCount >= 4)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    $"{report.TextureCount} Graphic textures are present. Use Frame Debugger to decide whether atlas consolidation is beneficial."));
            }

            if (report.ScrollRectCount > 0 && report.NestedCanvasCount == 0)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    "A ScrollRect is present without a nested Canvas. Profile rebuild isolation before changing the canvas boundary."));
            }

            if (report.GraphicsCount >= 80)
            {
                report.Issues.Add(new AuditIssue(
                    AuditSeverity.Info,
                    $"{report.GraphicsCount} Graphics are present. Validate rebuild, raycast, overdraw, and batching cost with the product workload."));
            }
        }
    }
}
