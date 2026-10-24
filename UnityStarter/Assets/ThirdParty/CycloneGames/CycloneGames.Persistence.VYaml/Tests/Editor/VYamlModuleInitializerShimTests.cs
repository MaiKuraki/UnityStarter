using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using VYaml.Serialization;

namespace CycloneGames.Persistence.Tests.VYaml
{
    /// <summary>
    /// Guards the assembly-local ModuleInitializerAttribute shim that the VYaml 1.5.0 generator
    /// requires. <see cref="UpstreamFix_AllowsShimRemoval"/> is the expiry signal: it fails once the
    /// upstream workaround is no longer needed.
    /// </summary>
    [TestFixture]
    public sealed class VYamlModuleInitializerShimTests
    {
        private const string AttributeMetadataName =
            "System.Runtime.CompilerServices.ModuleInitializerAttribute";

        private static readonly Assembly TestAssembly =
            typeof(VYamlModuleInitializerShimTests).Assembly;

        [Test]
        public void Shim_DefinesInternalModuleInitializerAttribute()
        {
            Type shim = TestAssembly.GetType(AttributeMetadataName, throwOnError: false);

            Assert.That(shim, Is.Not.Null,
                "This assembly declares [YamlObject] types, so it needs the shim. Without it the " +
                "generated [ModuleInitializer] fails with CS0122.");
            Assert.That(shim!.IsNotPublic, Is.True,
                "The shim must stay internal; a public copy would leak a framework type into this " +
                "module's API surface.");
        }

        [Test]
        public void Generator_EmitsRegistrationTypeForThisAssembly()
        {
            string expectedName =
                "VYaml.Generated.VYamlRegistration_" + SanitizeAssemblyName(TestAssembly.GetName().Name);
            Type registration = TestAssembly.GetType(expectedName, throwOnError: false);

            Assert.That(registration, Is.Not.Null,
                "No registration type was generated. Either this assembly no longer declares " +
                "[YamlObject]/[YamlSerializable] types, or the generator changed its naming.");
            Assert.That(
                registration!.GetMethod("Register",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                Is.Not.Null,
                "The registration type has no Register method, so the module initializer cannot run.");
        }

        [Test]
        public void UpstreamFix_AllowsShimRemoval()
        {
            Type upstream = typeof(YamlSerializer).Assembly.GetType(AttributeMetadataName, throwOnError: false);

            Assert.That(upstream, Is.Not.Null,
                "VYaml no longer embeds an internal ModuleInitializerAttribute, so the shim is dead " +
                "weight. Delete ModuleInitializerAttributeShim.cs from every assembly that declares " +
                "[YamlObject] types and remove this test.");
        }

        // Mirrors VYaml's RegistrationEmitter.SanitizeAssemblyName so the test derives the same type
        // name the generator emits.
        private static string SanitizeAssemblyName(string name)
        {
            var builder = new StringBuilder(name.Length + 1);

            if (name.Length > 0 && char.IsDigit(name[0]))
            {
                builder.Append('_');
            }

            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
