using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    internal static class NameHelper
    {
        internal static string GetFullName(ISymbol symbol)
        {
            return GetFullName(symbol.ContainingType) + "." + symbol.Name;
        }

        internal static string GetFullName(INamedTypeSymbol type)
        {
            if (type.ContainingType != null)
            {
                return GetFullName(type.ContainingType) + "." + type.Name;
            }

            return GetFullName(type.ContainingNamespace) + "." + type.Name;
        }

        internal static string GetFullName(INamespaceSymbol namespaceSymbol)
        {
            if (namespaceSymbol.ContainingNamespace != null &&
                !namespaceSymbol.ContainingNamespace.IsGlobalNamespace)
            {
                return GetFullName(namespaceSymbol.ContainingNamespace) + "." + namespaceSymbol.Name;
            }

            return namespaceSymbol.Name;
        }
    }
}