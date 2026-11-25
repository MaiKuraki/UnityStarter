// The VYaml source generator emits [ModuleInitializer] based on the attribute metadata name,
// while VYaml.dll exposes only an internal copy of that attribute (CS0122). Every assembly that
// declares [YamlObject] types needs its own internal, assembly-local attribute definition.
namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : global::System.Attribute
    {
    }
}
