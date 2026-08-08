using System;
using System.Collections.Generic;
using System.Linq;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildDataInspectorSerializedFieldContractTests
    {
        private sealed class PublicSerializedFieldFixture
        {
            public int value = 0;
        }

        [Test]
        public void DeclaredContract_CoversExactCurrentSerializedFields()
        {
            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredContract();

            Assert.That(report.IsValid, Is.True, report.Diagnostic);
            CollectionAssert.AreEqual(
                new[]
                {
                    "launchScene",
                    "applicationVersion",
                    "outputBasePath",
                    "companyName",
                    "productName",
                    "applicationIdentifier",
                    "versionInfoAssetPath",
                    "additionalScenes",
                    "recipeInvocations",
                    "cheatBuildMode"
                },
                BuildDataInspectorFieldNames.Profile.All);
            CollectionAssert.AreEqual(
                new[]
                {
                    "enabled",
                    "invocationId",
                    "stepTypeId",
                    "configuration",
                    "incrementality",
                    "dependencies"
                },
                BuildDataInspectorFieldNames.Invocation.All);
            CollectionAssert.AreEqual(
                new[] { "invocationId", "mode" },
                BuildDataInspectorFieldNames.Dependency.All);
        }

        [Test]
        public void Bind_DefaultProfile_ResolvesEveryRootProperty()
        {
            BuildData profile = ScriptableObject.CreateInstance<BuildData>();
            try
            {
                var serialized = new SerializedObject(profile);
                BuildDataInspectorPropertyBinding binding =
                    BuildDataInspectorSerializedFieldContract.Bind(serialized);

                Assert.That(binding.Report.IsValid, Is.True, binding.Report.Diagnostic);
                foreach (string fieldName in BuildDataInspectorFieldNames.Profile.All)
                {
                    Assert.That(
                        binding.TryGet(fieldName, out SerializedProperty property),
                        Is.True,
                        fieldName);
                    Assert.That(property, Is.Not.Null, fieldName);
                }
                Assert.That(
                    binding.GetRequired(
                        BuildDataInspectorFieldNames.Profile.RecipeInvocations),
                    Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DeclaredType_ReportsUnknownSerializedField()
        {
            string[] claims = BuildDataInspectorFieldNames.Profile.All
                .Where(name => !string.Equals(
                    name,
                    BuildDataInspectorFieldNames.Profile.CheatBuildMode,
                    StringComparison.Ordinal))
                .ToArray();

            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredType(
                    typeof(BuildData),
                    claims);

            AssertIssue(
                report,
                BuildDataInspectorContractIssueKind.UnknownSerializedField,
                BuildDataInspectorFieldNames.Profile.CheatBuildMode);
        }

        [Test]
        public void DeclaredType_ReportsStaleFieldClaim()
        {
            var claims = new List<string>(BuildDataInspectorFieldNames.Profile.All)
            {
                "futureField"
            };

            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredType(
                    typeof(BuildData),
                    claims);

            AssertIssue(
                report,
                BuildDataInspectorContractIssueKind.StaleFieldClaim,
                "futureField");
        }

        [Test]
        public void DeclaredType_ReportsDuplicateFieldClaim()
        {
            var claims = new List<string>(BuildDataInspectorFieldNames.Profile.All)
            {
                BuildDataInspectorFieldNames.Profile.LaunchScene
            };

            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredType(
                    typeof(BuildData),
                    claims);

            AssertIssue(
                report,
                BuildDataInspectorContractIssueKind.DuplicateFieldClaim,
                BuildDataInspectorFieldNames.Profile.LaunchScene);
        }

        [Test]
        public void DeclaredType_RejectsPublicSerializedFieldConvention()
        {
            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredType(
                    typeof(PublicSerializedFieldFixture),
                    new[] { "value" });

            AssertIssue(
                report,
                BuildDataInspectorContractIssueKind.PublicSerializedField,
                "value");
        }

        [Test]
        public void DeclaredType_ReportsFindPropertyBindingFailure()
        {
            BuildDataInspectorContractReport report =
                BuildDataInspectorSerializedFieldContract.InspectDeclaredType(
                    typeof(BuildData),
                    BuildDataInspectorFieldNames.Profile.All,
                    fieldName => !string.Equals(
                        fieldName,
                        BuildDataInspectorFieldNames.Profile.OutputBasePath,
                        StringComparison.Ordinal));

            AssertIssue(
                report,
                BuildDataInspectorContractIssueKind.SerializedPropertyBindingMissing,
                BuildDataInspectorFieldNames.Profile.OutputBasePath);
        }

        private static void AssertIssue(
            BuildDataInspectorContractReport report,
            BuildDataInspectorContractIssueKind kind,
            string fieldName)
        {
            Assert.That(report.IsValid, Is.False);
            Assert.That(
                report.Issues.Any(issue => issue.Kind == kind
                    && string.Equals(
                        issue.FieldName,
                        fieldName,
                        StringComparison.Ordinal)),
                Is.True,
                report.Diagnostic);
        }
    }
}
