using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using VYaml.Serialization;

namespace CycloneGames.Persistence.Tests.VYaml
{
    /// <summary>
    /// Verifies that the assembly-local ModuleInitializerAttribute shim and the generated
    /// registration type required by the VYaml source generator are present.
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
                "The generated [ModuleInitializer] requires an accessible assembly-local attribute.");
            Assert.That(shim!.IsNotPublic, Is.True,
                "The shim must remain internal so it does not join the assembly's public API.");
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
                "VYaml no longer embeds an internal ModuleInitializerAttribute; the shim is no longer required.");
        }

        // Matches VYaml's RegistrationEmitter.SanitizeAssemblyName to derive the generated type name.
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
