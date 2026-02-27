// Polyfill for C# 9 init-only setters on .NET Framework 4.8
// This type is required by the compiler when using 'init' accessors or records
// in projects targeting frameworks earlier than .NET 5.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
