using System;
using System.IO;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AddressablesPlayerBuildEnvironmentGuardTests
    {
        [Test]
        public void SelectedAddressables_DoesNotCreateNestedSuppressionSession()
        {
            using (var fixture = EnvironmentRequestFixture.Create(includeAddressables: true))
            {
                int packageChecks = 0;
                var guard = new AddressablesPlayerBuildEnvironmentGuard(
                    () =>
                    {
                        packageChecks++;
                        return true;
                    },
                    () => throw new AssertionException(
                        "Selected Addressables must not validate a suppression session."),
                    _ => throw new AssertionException(
                        "Selected Addressables must not begin a suppression session."));

                Assert.That(guard.ValidateEnvironment(fixture.Request), Is.Empty);
                using (guard.BeginEnvironment(fixture.Request))
                {
                }

                Assert.That(packageChecks, Is.Zero);
            }
        }

        [Test]
        public void UnselectedAddressables_ValidatesAndOwnsSuppressionSession()
        {
            using (var fixture = EnvironmentRequestFixture.Create(includeAddressables: false))
            {
                int validationCount = 0;
                string receivedProjectRoot = null;
                var scope = new RecordingDisposable();
                var guard = new AddressablesPlayerBuildEnvironmentGuard(
                    () => true,
                    () =>
                    {
                        validationCount++;
                        return null;
                    },
                    projectRoot =>
                    {
                        receivedProjectRoot = projectRoot;
                        return scope;
                    });

                Assert.That(guard.ValidateEnvironment(fixture.Request), Is.Empty);
                Assert.That(validationCount, Is.EqualTo(1));

                IDisposable session = guard.BeginEnvironment(fixture.Request);
                Assert.That(session, Is.SameAs(scope));
                Assert.That(receivedProjectRoot, Is.EqualTo(fixture.ProjectRoot));

                session.Dispose();
                Assert.That(scope.Disposed, Is.True);
            }
        }

        [Test]
        public void MissingAddressablesPackage_IsNoOp()
        {
            using (var fixture = EnvironmentRequestFixture.Create(includeAddressables: false))
            {
                var guard = new AddressablesPlayerBuildEnvironmentGuard(
                    () => false,
                    () => throw new AssertionException(
                        "A missing package must not validate suppression APIs."),
                    _ => throw new AssertionException(
                        "A missing package must not begin a suppression session."));

                Assert.That(guard.ValidateEnvironment(fixture.Request), Is.Empty);
                Assert.DoesNotThrow(() => guard.BeginEnvironment(fixture.Request).Dispose());
            }
        }

        [Test]
        public void UnsupportedSuppressionApi_IsReportedByPreflight()
        {
            using (var fixture = EnvironmentRequestFixture.Create(includeAddressables: false))
            {
                var guard = new AddressablesPlayerBuildEnvironmentGuard(
                    () => true,
                    () => "required hook is unavailable",
                    _ => throw new AssertionException("Preflight must fail before Begin."));

                var errors = guard.ValidateEnvironment(fixture.Request);

                Assert.That(errors.Count, Is.EqualTo(1));
                StringAssert.Contains("required hook is unavailable", errors[0]);
            }
        }

        [Test]
        public void AddressablesContentSession_DeclaresStableExclusiveKey()
        {
            var adapter = new AddressablesContentBuildAdapter();

            Assert.That(adapter.ExclusivePlayerSessionKey,
                Is.EqualTo(AddressablesContentBuildAdapter.PlayerSessionKey));
            Assert.That(adapter.ExclusivePlayerSessionKey, Is.Not.Empty);
        }

        private sealed class EnvironmentRequestFixture : IDisposable
        {
            private readonly AddressablesBuildConfig contentConfiguration;

            private EnvironmentRequestFixture(
                string projectRoot,
                PlayerBuildEnvironmentRequest request,
                AddressablesBuildConfig contentConfiguration)
            {
                ProjectRoot = projectRoot;
                Request = request;
                this.contentConfiguration = contentConfiguration;
            }

            internal string ProjectRoot { get; }
            internal PlayerBuildEnvironmentRequest Request { get; }

            internal static EnvironmentRequestFixture Create(bool includeAddressables)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(
                    Path.GetTempPath(),
                    "BuildPipelineAddressablesGuardTests"));
                var playerInvocation = new BuildStepInvocation(
                    "player",
                    BuildStepTypeIds.Player);
                var buildRequest = new BuildRequest(
                    "Company",
                    "Product",
                    "com.company.product",
                    "Assets/Build/Runtime/Resources/VersionInfoData.asset",
                    new[] { "Assets/Test.unity" },
                    CheatBuildMode.Disabled,
                    BuildTarget.StandaloneWindows64,
                    NamedBuildTarget.Standalone,
                    ScriptingImplementation.Mono2x,
                    projectRoot,
                    Path.Combine(projectRoot, "Build"),
                    Path.Combine(projectRoot, "Build", "Product.exe"),
                    Path.Combine(projectRoot, "Build"),
                    outputIsFolder: false,
                    deleteDebugFiles: false,
                    debugBuild: false,
                    exportAndroidProject: false,
                    allowExternalOutput: false,
                    cheatOverride: null,
                    batchMode: true,
                    applicationVersion: "1.0.0",
                    identityOverride: BuildIdentityOverride.Empty,
                    steps: new[] { playerInvocation },
                    sourceCleanlinessPolicy: BuildSourceCleanlinessPolicy.RequireClean,
                    purpose: BuildPurpose.Release);

                AddressablesBuildConfig configuration = null;
                AssetContentBuildRequest[] contentRequests;
                if (includeAddressables)
                {
                    configuration = UnityEngine.ScriptableObject
                        .CreateInstance<AddressablesBuildConfig>();
                    contentRequests = new[]
                    {
                        new AssetContentBuildRequest(
                            "content",
                            BuildTarget.StandaloneWindows64,
                            "1.0.0.1",
                            projectRoot,
                            configuration,
                            BuildIncrementality.Clean,
                            batchMode: true,
                            purpose: BuildPurpose.Release)
                    };
                }
                else
                {
                    contentRequests = Array.Empty<AssetContentBuildRequest>();
                }

                return new EnvironmentRequestFixture(
                    projectRoot,
                    new PlayerBuildEnvironmentRequest(
                        buildRequest,
                        playerInvocation,
                        contentRequests,
                        Array.Empty<PlayerBuildExtensionRequest>()),
                    configuration);
            }

            public void Dispose()
            {
                if (contentConfiguration != null)
                {
                    UnityEngine.Object.DestroyImmediate(contentConfiguration);
                }
            }
        }

        private sealed class RecordingDisposable : IDisposable
        {
            internal bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
