using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    internal class AssertArgumentOrderRule : ILocalSemanticFormattingRule
    {
        private class Rewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;

            private static readonly HashSet<string> TargetMethods =
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "Xunit.Assert.Equal",
                    "Xunit.Assert.NotEqual",
                };

            private static readonly HashSet<string> ActualParamterNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "actual",
                };

            private static readonly HashSet<string> ExpectedParamterNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "expected",
                };

            public Rewriter(SemanticModel model)
            {
                _model = model;
            }

            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbol = _model.GetSymbolInfo(node.Expression).Symbol as IMethodSymbol;
                if (symbol == null || !TargetMethods.Contains(GetFullName(symbol)))
                {
                    return base.VisitInvocationExpression(node);
                }

                int actualIndex = ParameterWithNameIndex(symbol, ActualParamterNames);
                int expectedIndex = ParameterWithNameIndex(symbol, ExpectedParamterNames);

                if (actualIndex == -1 || expectedIndex == -1)
                {
                    return base.VisitInvocationExpression(node);
                }

                var argumentList = (ArgumentListSyntax) Visit(node.ArgumentList);

                if (!IsConstant(argumentList.Arguments[actualIndex].Expression) ||
                    IsConstant(argumentList.Arguments[expectedIndex].Expression))
                {
                    return base.VisitInvocationExpression(node);
                }

                List<ArgumentSyntax> arguments = argumentList.Arguments.ToList();
                ArgumentSyntax actualArgument = arguments[actualIndex];
                ArgumentSyntax expectedArgument = arguments[expectedIndex];
                arguments[actualIndex] = expectedArgument;
                arguments[expectedIndex] = actualArgument;
                
                return node.Update(
                    (ExpressionSyntax) Visit(node.Expression),
                    argumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)));
            }

            private bool IsConstant(ExpressionSyntax expression)
            {
                switch (expression.Kind())
                {
                    case SyntaxKind.CharacterLiteralExpression:
                    case SyntaxKind.FalseLiteralExpression:
                    case SyntaxKind.NullLiteralExpression:
                    case SyntaxKind.NumericLiteralExpression:
                    case SyntaxKind.StringLiteralExpression:
                    case SyntaxKind.TrueLiteralExpression:
                    {
                        return true;
                    }

                    case SyntaxKind.SimpleMemberAccessExpression:
                    case SyntaxKind.IdentifierName:
                        {
                        var symbol = _model.GetSymbolInfo(expression).Symbol;

                        if (symbol?.Kind == SymbolKind.Field)
                        {
                            return ((IFieldSymbol) symbol).IsConst;
                        }

                        break;
                    }
                }

                return false;
            }

            private int ParameterWithNameIndex(IMethodSymbol symbol, HashSet<string> names)
            {
                for (int i = 0; i < symbol.Parameters.Length; i++)
                {
                    if (names.Contains(symbol.Parameters[i].Name))
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static string GetFullName(IMethodSymbol symbol)
            {
                return GetFullName(symbol.ContainingType) + "." + symbol.Name;
            }

            private static string GetFullName(INamedTypeSymbol type)
            {
                if (type.ContainingType != null)
                {
                    return GetFullName(type.ContainingType) + "." + type.Name;
                }

                return GetFullName(type.ContainingNamespace) + "." + type.Name;
            }

            private static string GetFullName(INamespaceSymbol namespaceSymbol)
            {
                if (namespaceSymbol.ContainingNamespace != null &&
                    !namespaceSymbol.ContainingNamespace.IsGlobalNamespace)
                {
                    return GetFullName(namespaceSymbol.ContainingNamespace) + "." + namespaceSymbol.Name;
                }

                return namespaceSymbol.Name;
            }
        }

        public bool SupportsLanguage(string languageName)
        {
            return languageName == LanguageNames.CSharp;
        }

        public async Task<SyntaxNode> ProcessAsync(Document document, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
        {
            Rewriter rewriter = new Rewriter(await document.GetSemanticModelAsync(cancellationToken));
            return rewriter.Visit(await document.GetSyntaxRootAsync(cancellationToken));
        }
    }
}
