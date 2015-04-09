// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.CodeFormatting.Rules
{
    [GlobalSemanticRuleOrder(GlobalSemanticRuleOrder.MarkReadonlyFieldsRule)]
    internal sealed class MarkReadonlyFieldsRule : CSharpSyntaxRewriter, IGlobalSemanticFormattingRule
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (!node.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword)))
            {
                var declaration = node.Declaration.Variables[0];
                if (!writtenFields.Contains(declaration) && fieldsInScope.Contains(declaration))
                {
                    return node.WithModifiers(node.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
                }
            }

            return node;
        }

        public bool SupportsLanguage(string languageName)
        {
            return languageName == LanguageNames.CSharp;
        }

        private HashSet<SyntaxNode> writtenFields;
        private HashSet<SyntaxNode> fieldsInScope; 
        private Dictionary<IAssemblySymbol, bool> visibleOutsideAsembly = new Dictionary<IAssemblySymbol, bool>(); 

        public async Task<Solution> ProcessAsync(Document document, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
        {
            if (writtenFields == null)
            {
                await BuildReferenceSet(document.Project.Solution, cancellationToken);
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, Visit(root));
        }

        private async Task BuildReferenceSet(Solution solution, CancellationToken cancellationToken)
        {
            writtenFields = new HashSet<SyntaxNode>();
            fieldsInScope = new HashSet<SyntaxNode>();

            foreach (var document in solution.Projects.SelectMany(p => p.Documents))
            {
                await BuildReferencesInDocument(document, cancellationToken);
            }
        }

        private async Task BuildReferencesInDocument(Document document, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var internalsVisibleToAttribute = semanticModel.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InternalsVisibleToAttribute");

            foreach (var node in root.DescendantNodes())
            {
                if (node.IsKind(SyntaxKind.FieldDeclaration))
                {
                    await CheckFieldScope(node, semanticModel, internalsVisibleToAttribute, document.Project.Solution, cancellationToken);
                }
                else
                {
                    await CheckFieldReferences(node, semanticModel, cancellationToken);
                }
            }
        }

        private async Task CheckFieldReferences(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            ISymbol assignedSymbol = GetAssignedSymbol(node, semanticModel);
            if (assignedSymbol != null && assignedSymbol.Kind == SymbolKind.Field)
            {
                IFieldSymbol field = (IFieldSymbol) assignedSymbol;

                if (field.DeclaredAccessibility == Accessibility.Public ||
                    field.DeclaredAccessibility == Accessibility.Protected)
                {
                    // Skip externally visible fields to save time
                    return;
                }

                if (!IsInsideOwnConstructor(node, field.ContainingType, semanticModel))
                {
                    foreach (var declarationSyntax in field.DeclaringSyntaxReferences)
                    {
                        writtenFields.Add(await declarationSyntax.GetSyntaxAsync(cancellationToken));
                    }
                }
            }
        }

        private async Task CheckFieldScope(SyntaxNode node, SemanticModel semanticModel,
            ISymbol internalsVisibleToAttribute, Solution solution, CancellationToken cancellationToken)
        {
            var fieldDeclaration = (FieldDeclarationSyntax) node;
            var variableDeclaration = fieldDeclaration.Declaration.Variables[0];
            ISymbol field = semanticModel.GetDeclaredSymbol(variableDeclaration);

            if (IsSymbolVisibleOutsideSolution(field, internalsVisibleToAttribute, solution))
            {
                return;
            }

            foreach (var declarationSyntax in field.DeclaringSyntaxReferences)
            {
                fieldsInScope.Add(await declarationSyntax.GetSyntaxAsync(cancellationToken));
            }
        }

        private bool IsSymbolVisibleOutsideSolution(ISymbol symbol, ISymbol internalsVisibleToAttribute, Solution solution)
        {
            if (symbol.DeclaredAccessibility == Accessibility.Public ||
                symbol.DeclaredAccessibility == Accessibility.Protected)
            {
                if(symbol.ContainingType != null)
                {
                    // a public symbol in a non-visible class isn't visible
                    return IsSymbolVisibleOutsideSolution(symbol.ContainingType, internalsVisibleToAttribute, solution);
                }

                // They are public, we are going to skip them.
                return true;
            }

            if (symbol.DeclaredAccessibility > Accessibility.Private)
            {
                var visibleOutsideSolution = IsVisibleOutsideSolution(symbol, internalsVisibleToAttribute,
                    solution);

                if (visibleOutsideSolution)
                {
                    if (symbol.ContainingType != null)
                    {
                        // a visible symbol in a non-visible class isn't visible
                        return IsSymbolVisibleOutsideSolution(symbol.ContainingType, internalsVisibleToAttribute, solution);
                    }
                }
            }

            return false;
        }

        private  bool IsVisibleOutsideSolution(ISymbol field, ISymbol internalsVisibleToAttribute, Solution solution)
        {
            bool isVisible = false;
            var assembly = field.ContainingAssembly;
            if (visibleOutsideAsembly.TryGetValue(assembly, out isVisible))
            {
                return isVisible;
            }

            foreach (var internalsVisibleInstance in assembly.GetAttributes()
                .Where(a => Equals(a.AttributeClass, internalsVisibleToAttribute)))
            {
                if (internalsVisibleInstance.ConstructorArguments.Length != 1)
                {
                    // Unexpected number of agruments, isn't really the correct type.
                    continue;
                }

                var assemblyNameArgument = internalsVisibleInstance.ConstructorArguments[0];
                if (assemblyNameArgument.Kind != TypedConstantKind.Primitive)
                {
                    // The first argument wasn't a primitave value, isn't really the correct type
                    continue;
                }

                string assemblyName = assemblyNameArgument.Value as string;
                if (String.IsNullOrEmpty(assemblyName))
                {
                    // The first argument wasn't a string, isn't really the correct type
                    continue;
                }

                if (!solution.Projects.Any(p => ProjectHasName(p, assemblyName)))
                {
                    // None of the projects in this solution have the target name
                    // Which means that this is intended to reference something outside of this
                    // solution, in which the assignments cannot be found
                    isVisible = true;
                    break;
                }
            }

            visibleOutsideAsembly.Add(assembly, isVisible);
            return isVisible;
        }

        private bool ProjectHasName(Project project, string assemblyName)
        {
            AssemblyName projectName = new AssemblyName(project.AssemblyName);
            AssemblyName target = new AssemblyName(assemblyName);
            if (String.Equals(projectName.Name, target.Name, StringComparison.Ordinal))
            {
                // Technically we should check version and public key token
                // But if signing happens out of band, or there is a version bump
                // That's almost certainly the same assembly anyway, so just go with true
                return true;
            }
            return false;
        }

        private bool IsInsideOwnConstructor(SyntaxNode node, ITypeSymbol type, SemanticModel semanticModel)
        {
            while (node != null)
            {
                if (node.IsKind(SyntaxKind.ConstructorDeclaration))
                {
                    return IsInType(node.Parent, type, semanticModel);
                }

                node = node.Parent;
            }
            return false;
        }

        private bool IsInType(SyntaxNode node, ITypeSymbol containingType, SemanticModel semanticModel)
        {
            while (node != null)
            {
                if (node.IsKind(SyntaxKind.ClassDeclaration) || node.IsKind(SyntaxKind.StructDeclaration))
                {
                    return Equals(semanticModel.GetDeclaredSymbol(node), containingType);
                }

                node = node.Parent;
            }
            return false;
        }

        private ISymbol GetAssignedSymbol(SyntaxNode node, SemanticModel semanticModel)
        {
            switch (node.Kind())
            {
                case SyntaxKind.AddAssignmentExpression:
                case SyntaxKind.AndAssignmentExpression:
                case SyntaxKind.DivideAssignmentExpression:
                case SyntaxKind.ExclusiveOrAssignmentExpression:
                case SyntaxKind.LeftShiftAssignmentExpression:
                case SyntaxKind.ModuloAssignmentExpression:
                case SyntaxKind.MultiplyAssignmentExpression:
                case SyntaxKind.OrAssignmentExpression:
                case SyntaxKind.RightShiftAssignmentExpression:
                case SyntaxKind.SimpleAssignmentExpression:
                case SyntaxKind.SubtractAssignmentExpression:
                {
                    AssignmentExpressionSyntax binary = (AssignmentExpressionSyntax) node;
                    return semanticModel.GetSymbolInfo(binary.Left).Symbol;
                }

                case SyntaxKind.Argument:
                {
                    ArgumentSyntax argument = (ArgumentSyntax) node;
                    if (!argument.RefOrOutKeyword.IsKind(CodeAnalysis.VisualBasic.SyntaxKind.EmptyToken))
                    {
                        return semanticModel.GetSymbolInfo(argument.Expression).Symbol;
                    }
                    return null;
                }
                default:
                    return null;
            }
        }
    }
}