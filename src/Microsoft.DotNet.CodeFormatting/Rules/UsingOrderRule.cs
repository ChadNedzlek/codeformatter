// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    /// <summary>
    /// This will ensure that using directives are ordered
    /// </summary>
    [SyntaxRule(UsingOrderRule.Name, UsingOrderRule.Description, SyntaxRuleOrder.UsingOrderFormattingRule, DefaultRule = false)]
    internal sealed class UsingOrderRule : CSharpOnlyFormattingRule, ISyntaxFormattingRule
    {
        internal const string Name = "UsingOrder";
        internal const string Description = "Sort using alphabetically";

        private sealed class ReorderingRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node)
            {
                if (node.Usings.Count == 0)
                    return node;

                IEnumerable<IGrouping<int, UsingDirectiveSyntax>> groups = node.Usings.GroupBy(GetGroupIndex).OrderBy(g => g.Key);
                var chunked = groups.Select(AddNewLine).ToList();
                foreach (var c in chunked.Skip(1))
                {
                    c[0] = c[0].WithLeadingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
                }

                return node.WithUsings(SyntaxFactory.List(chunked.SelectMany(c => c)));
            }

            private List<UsingDirectiveSyntax> AddNewLine(IGrouping<int, UsingDirectiveSyntax> usingNodes)
            {
                return usingNodes.OrderBy(GetSortKey).Select(SingleTrailingTrivia).ToList();
            }

            private string GetSortKey(UsingDirectiveSyntax arg)
            {
                if (arg.Alias != null)
                    return arg.Alias.Name.GetText().ToString();

                return arg.Name.GetText().ToString();
            }

            private UsingDirectiveSyntax SingleTrailingTrivia(UsingDirectiveSyntax usingDirectiveSyntax)
            {
                if (usingDirectiveSyntax.HasTrailingTrivia)
                    return usingDirectiveSyntax.WithoutLeadingTrivia().WithTrailingTrivia(usingDirectiveSyntax.GetTrailingTrivia()[0]);

                if (usingDirectiveSyntax.HasLeadingTrivia)
                    return usingDirectiveSyntax.WithoutLeadingTrivia();

                return usingDirectiveSyntax;
            }
            
            private int GetGroupIndex(UsingDirectiveSyntax usingDirective)
            {
                if (usingDirective.Alias != null)
                {
                    return 1;
                }

                if (usingDirective.StaticKeyword.Kind() != SyntaxKind.None)
                {
                    return 2;
                }

                return 0;
            }
        }

        public SyntaxNode Process(SyntaxNode syntaxRoot, string languageName)
        {
            var rewriter = new ReorderingRewriter();
            var newNode = rewriter.Visit(syntaxRoot);
            return newNode;
        }
    }
}
