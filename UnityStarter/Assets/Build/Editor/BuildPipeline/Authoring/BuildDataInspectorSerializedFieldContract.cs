using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class BuildDataInspectorFieldNames
    {
        internal static class Profile
        {
            public const string LaunchScene = "launchScene";
            public const string ApplicationVersion = "applicationVersion";
            public const string OutputBasePath = "outputBasePath";
            public const string CompanyName = "companyName";
            public const string ProductName = "productName";
            public const string ApplicationIdentifier = "applicationIdentifier";
            public const string VersionInfoAssetPath = "versionInfoAssetPath";
            public const string AdditionalScenes = "additionalScenes";
            public const string RecipeInvocations = "recipeInvocations";
            public const string SourceCleanlinessPolicy = "sourceCleanlinessPolicy";
            public const string CheatBuildMode = "cheatBuildMode";

            public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
            {
                LaunchScene,
                ApplicationVersion,
                OutputBasePath,
                CompanyName,
                ProductName,
                ApplicationIdentifier,
                VersionInfoAssetPath,
                AdditionalScenes,
                RecipeInvocations,
                SourceCleanlinessPolicy,
                CheatBuildMode
            });
        }

        internal static class Invocation
        {
            public const string Enabled = "enabled";
            public const string InvocationId = "invocationId";
            public const string StepTypeId = "stepTypeId";
            public const string Configuration = "configuration";
            public const string Incrementality = "incrementality";
            public const string Dependencies = "dependencies";

            public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
            {
                Enabled,
                InvocationId,
                StepTypeId,
                Configuration,
                Incrementality,
                Dependencies
            });
        }

        internal static class Dependency
        {
            public const string InvocationId = "invocationId";
            public const string Mode = "mode";

            public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[]
            {
                InvocationId,
                Mode
            });
        }
    }

    internal enum BuildDataInspectorContractIssueKind
    {
        UnknownSerializedField,
        StaleFieldClaim,
        DuplicateFieldClaim,
        PublicSerializedField,
        SerializedPropertyBindingMissing
    }

    internal sealed class BuildDataInspectorContractIssue
    {
        public BuildDataInspectorContractIssue(
            BuildDataInspectorContractIssueKind kind,
            Type declaringType,
            string fieldName,
            string message)
        {
            Kind = kind;
            DeclaringType = declaringType
                ?? throw new ArgumentNullException(nameof(declaringType));
            FieldName = fieldName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public BuildDataInspectorContractIssueKind Kind { get; }
        public Type DeclaringType { get; }
        public string FieldName { get; }
        public string Message { get; }
    }

    internal sealed class BuildDataInspectorContractReport
    {
        internal BuildDataInspectorContractReport(
            IEnumerable<BuildDataInspectorContractIssue> issues)
        {
            Issues = new ReadOnlyCollection<BuildDataInspectorContractIssue>(
                (issues ?? Array.Empty<BuildDataInspectorContractIssue>()).ToArray());
            Diagnostic = Issues.Count == 0
                ? string.Empty
                : string.Join("\n", Issues.Select(issue => issue.Message));
        }

        public bool IsValid => Issues.Count == 0;
        public IReadOnlyList<BuildDataInspectorContractIssue> Issues { get; }
        public string Diagnostic { get; }
    }

    internal sealed class BuildDataInspectorPropertyBinding
    {
        private readonly IReadOnlyDictionary<string, SerializedProperty> properties;

        internal BuildDataInspectorPropertyBinding(
            BuildDataInspectorContractReport report,
            IReadOnlyDictionary<string, SerializedProperty> properties)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            this.properties = properties
                ?? throw new ArgumentNullException(nameof(properties));
        }

        public BuildDataInspectorContractReport Report { get; }

        public bool TryGet(
            string fieldName,
            out SerializedProperty property)
        {
            return properties.TryGetValue(fieldName, out property)
                && property != null;
        }

        public SerializedProperty GetRequired(string fieldName)
        {
            if (TryGet(fieldName, out SerializedProperty property))
            {
                return property;
            }

            throw new InvalidOperationException(
                $"BuildData serialized property '{fieldName}' was not bound. " +
                Report.Diagnostic);
        }
    }

    internal static class BuildDataInspectorSerializedFieldContract
    {
        private const BindingFlags DeclaredInstanceFields =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        public static BuildDataInspectorContractReport InspectDeclaredContract()
        {
            var issues = new List<BuildDataInspectorContractIssue>();
            AppendDeclaredTypeIssues(
                typeof(BuildData),
                BuildDataInspectorFieldNames.Profile.All,
                issues);
            AppendDeclaredTypeIssues(
                typeof(BuildRecipeInvocation),
                BuildDataInspectorFieldNames.Invocation.All,
                issues);
            AppendDeclaredTypeIssues(
                typeof(BuildInvocationDependency),
                BuildDataInspectorFieldNames.Dependency.All,
                issues);
            return new BuildDataInspectorContractReport(issues);
        }

        public static BuildDataInspectorPropertyBinding Bind(
            SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (!(serializedObject.targetObject is BuildData))
            {
                throw new ArgumentException(
                    "The serialized-field contract can bind only BuildData targets.",
                    nameof(serializedObject));
            }

            BuildDataInspectorContractReport declared = InspectDeclaredContract();
            var issues = new List<BuildDataInspectorContractIssue>(declared.Issues);
            var properties = new Dictionary<string, SerializedProperty>(
                StringComparer.Ordinal);
            foreach (string fieldName in BuildDataInspectorFieldNames.Profile.All)
            {
                SerializedProperty property = serializedObject.FindProperty(fieldName);
                if (property == null)
                {
                    issues.Add(CreateBindingIssue(typeof(BuildData), fieldName));
                }
                else
                {
                    if (!properties.ContainsKey(fieldName))
                    {
                        properties.Add(fieldName, property);
                    }
                }
            }

            return new BuildDataInspectorPropertyBinding(
                new BuildDataInspectorContractReport(issues),
                new ReadOnlyDictionary<string, SerializedProperty>(properties));
        }

        internal static BuildDataInspectorContractReport InspectDeclaredType(
            Type declaringType,
            IReadOnlyList<string> handledFieldNames,
            Func<string, bool> propertyBindingProbe = null)
        {
            var issues = new List<BuildDataInspectorContractIssue>();
            AppendDeclaredTypeIssues(
                declaringType,
                handledFieldNames,
                issues);
            if (propertyBindingProbe != null)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                foreach (string fieldName in handledFieldNames)
                {
                    if (string.IsNullOrEmpty(fieldName)
                        || !visited.Add(fieldName))
                    {
                        continue;
                    }

                    if (!propertyBindingProbe(fieldName))
                    {
                        issues.Add(CreateBindingIssue(declaringType, fieldName));
                    }
                }
            }

            return new BuildDataInspectorContractReport(issues);
        }

        private static void AppendDeclaredTypeIssues(
            Type declaringType,
            IReadOnlyList<string> handledFieldNames,
            ICollection<BuildDataInspectorContractIssue> issues)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            if (handledFieldNames == null)
            {
                throw new ArgumentNullException(nameof(handledFieldNames));
            }

            FieldInfo[] serializedFields = declaringType
                .GetFields(DeclaredInstanceFields)
                .Where(IsUnitySerializedField)
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .ToArray();
            var serializedNames = new HashSet<string>(
                serializedFields.Select(field => field.Name),
                StringComparer.Ordinal);
            var claims = new HashSet<string>(StringComparer.Ordinal);

            foreach (string fieldName in handledFieldNames)
            {
                if (string.IsNullOrEmpty(fieldName))
                {
                    issues.Add(CreateIssue(
                        BuildDataInspectorContractIssueKind.StaleFieldClaim,
                        declaringType,
                        string.Empty,
                        "declares an empty serialized-field claim"));
                }
                else if (!claims.Add(fieldName))
                {
                    issues.Add(CreateIssue(
                        BuildDataInspectorContractIssueKind.DuplicateFieldClaim,
                        declaringType,
                        fieldName,
                        "is claimed more than once"));
                }
            }

            foreach (FieldInfo field in serializedFields)
            {
                if (field.IsPublic)
                {
                    issues.Add(CreateIssue(
                        BuildDataInspectorContractIssueKind.PublicSerializedField,
                        declaringType,
                        field.Name,
                        "must be private and explicitly marked with SerializeField"));
                }

                if (!claims.Contains(field.Name))
                {
                    issues.Add(CreateIssue(
                        BuildDataInspectorContractIssueKind.UnknownSerializedField,
                        declaringType,
                        field.Name,
                        "has no explicit Inspector owner"));
                }
            }

            foreach (string claim in claims.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!serializedNames.Contains(claim))
                {
                    issues.Add(CreateIssue(
                        BuildDataInspectorContractIssueKind.StaleFieldClaim,
                        declaringType,
                        claim,
                        "is claimed but is not a declared Unity serialized field"));
                }
            }
        }

        private static BuildDataInspectorContractIssue CreateBindingIssue(
            Type declaringType,
            string fieldName)
        {
            return CreateIssue(
                BuildDataInspectorContractIssueKind.SerializedPropertyBindingMissing,
                declaringType,
                fieldName,
                "cannot be bound through SerializedObject.FindProperty");
        }

        private static BuildDataInspectorContractIssue CreateIssue(
            BuildDataInspectorContractIssueKind kind,
            Type declaringType,
            string fieldName,
            string description)
        {
            return new BuildDataInspectorContractIssue(
                kind,
                declaringType,
                fieldName,
                $"BuildData Inspector contract '{declaringType.Name}.{fieldName}' {description}.");
        }

        private static bool IsUnitySerializedField(FieldInfo field)
        {
            if (field == null
                || field.IsStatic
                || field.IsLiteral
                || field.IsInitOnly
                || field.IsNotSerialized)
            {
                return false;
            }

            return field.IsPublic
                || field.IsDefined(typeof(SerializeField), inherit: false)
                || field.IsDefined(typeof(SerializeReference), inherit: false);
        }
    }
}
