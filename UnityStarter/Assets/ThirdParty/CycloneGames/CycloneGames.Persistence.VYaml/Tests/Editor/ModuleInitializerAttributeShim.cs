// VYaml 1.5.0's generator decides on its ModuleInitializerAttribute polyfill by metadata-name presence
// only, while VYaml.dll carries just an internal copy, so the emitted [ModuleInitializer] fails with
// CS0122. This assembly-local declaration is the accessible target it needs.
//
// Keep it internal. Any assembly that declares [YamlObject]/[YamlSerializable] types needs its own copy.
namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : global::System.Attribute
    {
    }
}
