// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
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
    /// <summary>
    /// Mark any fields that can provably be marked as readonly.
    /// </summary>
    [GlobalSemanticRuleOrder(GlobalSemanticRuleOrder.MarkReadonlyFieldsRule)]
    internal sealed class MarkReadonlyFieldsRule : IGlobalSemanticFormattingRule
    {
        /// <summary>
        /// This is the first walker, which looks for fields that are valid to transform to readonly.
        /// It returns any private or internal fields that are not already marked readonly, and returns a hash set of them.
        /// Internal fields are only considered if the "InternalsVisibleTo" is a reference to something in the same solution,
        /// since it's possible to analyse the global usages of it. Otherwise there is an assembly we don't have access to
        /// that can see that field, so we have to treat is as public
        /// </summary>
        private sealed class WritableFieldScanner : CSharpSyntaxWalker
        {
            private readonly SemanticModel _model;
            private readonly HashSet<IFieldSymbol> _fields = new HashSet<IFieldSymbol>();
            private readonly Dictionary<IAssemblySymbol, bool> _visibleOutsideAsembly = new Dictionary<IAssemblySymbol, bool>();
            private readonly Solution _solution;
            private ISymbol _internalsVisibleToAttribute;

            private WritableFieldScanner(SemanticModel model, Solution solution)
            {
                _model = model;
                _solution = solution;
                _internalsVisibleToAttribute = model.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InternalsVisibleToAttribute");
            }

            public static async Task<HashSet<IFieldSymbol>> Scan(Document document, CancellationToken cancellationToken)
            {
                WritableFieldScanner scanner = new WritableFieldScanner(await document.GetSemanticModelAsync(cancellationToken), document.Project.Solution);
                scanner.Visit(await document.GetSyntaxRootAsync(cancellationToken));
                return scanner._fields;
            }

            public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                IFieldSymbol fieldSymbol = (IFieldSymbol) _model.GetDeclaredSymbol(node.Declaration.Variables[0]);

                if (fieldSymbol.IsReadOnly || fieldSymbol.IsConst || fieldSymbol.IsExtern)
                {
                    return;
                }

                if (IsSymbolVisibleOutsideSolution(fieldSymbol, _internalsVisibleToAttribute, _solution))
                {
                    return;
                }

                _fields.Add(fieldSymbol);
            }

            private bool IsSymbolVisibleOutsideSolution(ISymbol symbol, ISymbol internalsVisibleToAttribute, Solution solution)
            {
                Accessibility accessibility = symbol.DeclaredAccessibility;

                if (accessibility == Accessibility.NotApplicable)
                {
                    if (symbol.Kind == SymbolKind.Field)
                    {
                        accessibility = Accessibility.Private;
                    }
                    else
                    {
                        accessibility = Accessibility.Internal;
                    }
                }

                if (accessibility == Accessibility.Public || accessibility == Accessibility.Protected)
                {
                    if (symbol.ContainingType != null)
                    {
                        // a public symbol in a non-visible class isn't visible
                        return IsSymbolVisibleOutsideSolution(symbol.ContainingType, internalsVisibleToAttribute, solution);
                    }

                    // They are public, we are going to skip them.
                    return true;
                }

                if (accessibility > Accessibility.Private)
                {
                    var visibleOutsideSolution = IsVisibleOutsideSolution(symbol, internalsVisibleToAttribute, solution);

                    if (visibleOutsideSolution)
                    {
                        if (symbol.ContainingType != null)
                        {
                            // a visible symbol in a non-visible class isn't visible
                            return IsSymbolVisibleOutsideSolution(symbol.ContainingType, internalsVisibleToAttribute, solution);
                        }

                        return true;
                    }
                }

                return false;
            }

            private bool IsVisibleOutsideSolution(ISymbol field, ISymbol internalsVisibleToAttribute, Solution solution)
            {
                bool isVisible;
                var assembly = field.ContainingAssembly;
                if (_visibleOutsideAsembly.TryGetValue(assembly, out isVisible))
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
                        // solution, in which the assignments cannot be found.
                        isVisible = true;
                        break;
                    }
                }

                _visibleOutsideAsembly.Add(assembly, isVisible);
                return isVisible;
            }

            private static bool ProjectHasName(Project project, string assemblyName)
            {
                var projectName = new AssemblyName(project.AssemblyName);
                var target = new AssemblyName(assemblyName);
                if (String.Equals(projectName.Name, target.Name, StringComparison.Ordinal))
                {
                    // Technically we should check version and public key token
                    // But if signing happens out of band, or there is a version bump
                    // That's almost certainly the same assembly anyway, so just go with true
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// This is the second walker. It checks all code for instances where one of the writable fields (as
        /// calculated by <see cref="WritableFieldScanner"/>) is written to, and removes it from the set.
        /// Once the scan is complete, the set will not contain any fields written in the specified document.
        /// </summary>
        private sealed class WriteUsagesScanner : CSharpSyntaxWalker
        {
            private readonly SemanticModel _semanticModel;
            private readonly ConcurrentDictionary<IFieldSymbol, bool> _writableFields;

            private WriteUsagesScanner(SemanticModel semanticModel, ConcurrentDictionary<IFieldSymbol, bool> writableFields)
            {
                _semanticModel = semanticModel;
                _writableFields = writableFields;
            }

            public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
            {
                base.VisitAssignmentExpression(node);

                CheckForFieldWrite(node.Left);
            }

            public override void VisitArgument(ArgumentSyntax node)
            {
                base.VisitArgument(node);

                if (!node.RefOrOutKeyword.IsKind(SyntaxKind.None))
                {
                    CheckForFieldWrite(node.Expression);
                }
            }

            public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                base.VisitMethodDeclaration(node);

                if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword)))
                {
                    // This method body is unable to be analysed, so may contain writer instances
                    CheckForRefParametersForExternMethod(node.ParameterList.Parameters);
                }
            }

            public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
            {
                base.VisitIndexerDeclaration(node);

                if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.ExternKeyword)))
                {
                    // This method body is unable to be analysed, so may contain writer instances
                    CheckForRefParametersForExternMethod(node.ParameterList.Parameters);
                }
            }

            private void CheckForRefParametersForExternMethod(IEnumerable<ParameterSyntax> parameters)
            {
                foreach (var parameter in parameters)
                {
                    ITypeSymbol parameterType = _semanticModel.GetTypeInfo(parameter.Type).Type;
                    if (parameterType == null)
                    {
                        continue;
                    }

                    bool canModify = true;
                    if (parameterType.TypeKind == TypeKind.Struct)
                    {
                        canModify = parameter.Modifiers.Any(m => m.IsKind(SyntaxKind.RefKeyword));
                    }

                    if (canModify)
                    {
                        // This parameter might be used to modify one of the fields, since the
                        // implmentation is hidden from this analysys. Assume all fields
                        // of the type are written to

                        foreach (var field in parameterType.GetMembers().OfType<IFieldSymbol>())
                        {
                            MarkWriteInstance(field);
                        }
                    }
                }
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                base.VisitBinaryExpression(node);
                switch (node.OperatorToken.Kind())
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
                    case SyntaxKind.SubtractAssignmentExpression:
                    {
                        CheckForFieldWrite(node.Left);
                        break;
                    }
                }
            }

            private void CheckForFieldWrite(ExpressionSyntax node)
            {
                var fieldSymbol = _semanticModel.GetSymbolInfo(node).Symbol as IFieldSymbol;

                if (fieldSymbol != null)
                {
                    if (IsInsideOwnConstructor(node, fieldSymbol.ContainingType))
                    {
                        return;
                    }

                    MarkWriteInstance(fieldSymbol);
                }
            }

            private void MarkWriteInstance(IFieldSymbol fieldSymbol)
            {
                bool ignored;
                _writableFields.TryRemove(fieldSymbol, out ignored);
            }

            private bool IsInsideOwnConstructor(SyntaxNode node, ITypeSymbol type)
            {
                while (node != null)
                {
                    if (node.IsKind(SyntaxKind.ConstructorDeclaration))
                    {
                        return IsInType(node.Parent, type);
                    }

                    node = node.Parent;
                }
                return false;
            }

            private bool IsInType(SyntaxNode node, ITypeSymbol containingType)
            {
                while (node != null)
                {
                    if (node.IsKind(SyntaxKind.ClassDeclaration) || node.IsKind(SyntaxKind.StructDeclaration))
                    {
                        return Equals(containingType, _semanticModel.GetDeclaredSymbol(node));
                    }

                    node = node.Parent;
                }
                return false;
            }

            public static async Task RemoveWrittenFields(Document document, ConcurrentDictionary<IFieldSymbol, bool> writableFields, CancellationToken cancellationToken)
            {
                WriteUsagesScanner scanner = new WriteUsagesScanner(await document.GetSemanticModelAsync(cancellationToken), writableFields);
                scanner.Visit(await document.GetSyntaxRootAsync(cancellationToken));
            }
        }

        /// <summary>
        /// This is the actually rewriter, and should be run third, using the data gathered from the other two
        /// (<see cref="WritableFieldScanner"/> and <see cref="WriteUsagesScanner"/>).
        ///
        /// Any field in the set is both writeable, but not actually written to, which means the "readonly"
        /// modifier should be applied to it.
        /// </summary>
        private sealed class ReadonlyApplication : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _model;
            private readonly ConcurrentDictionary<IFieldSymbol, bool> _unwrittenFields;

            public ReadonlyApplication(ConcurrentDictionary<IFieldSymbol, bool> unwrittenFields, SemanticModel model)
            {
                _model = model;
                _unwrittenFields = unwrittenFields;
            }

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                IFieldSymbol fieldSymbol = (IFieldSymbol)_model.GetDeclaredSymbol(node.Declaration.Variables[0]);
                bool ignored;
                if (_unwrittenFields.TryRemove(fieldSymbol, out ignored))
                {
                    return node.WithModifiers(node.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
                }

                return node;
            }
        }

        public bool SupportsLanguage(string languageName)
        {
            return languageName == LanguageNames.CSharp;
        }
        
        private ConcurrentDictionary<IFieldSymbol, bool> _unwrittenWritableFields;
        private readonly SemaphoreSlim _processUsagesLock = new SemaphoreSlim(1, 1);

        public async Task<Solution> ProcessAsync(Document document, SyntaxNode syntaxRoot, CancellationToken cancellationToken)
        {
            if (_unwrittenWritableFields == null)
            {
                using (await SemaphoreLock.GetAsync(_processUsagesLock))
                {
                    // A global analysis must be run before we can do any actual processing, because a field might be written
                    // in a different file than it is declared (even private ones may be split between partial classes).

                    // It's also quite expensive, which is why it's being done inside the lock, so
                    // that the entire solution is not processed for each input file individually
                    if (_unwrittenWritableFields == null)
                    {
                        var allDocuments = document.Project.Solution.Projects.SelectMany(p => p.Documents).ToList();
                        var fields = await Task.WhenAll(
                            allDocuments
                                .AsParallel()
                                .Select(
                                    async doc => await WritableFieldScanner.Scan(doc, cancellationToken)));

                        var writableFields = new ConcurrentDictionary<IFieldSymbol, bool>(
                            fields.SelectMany(s => s).Select(f => new KeyValuePair<IFieldSymbol, bool>(f, true)));

                        await Task.WhenAll(
                            allDocuments.AsParallel()
                                .Select(async doc => await WriteUsagesScanner.RemoveWrittenFields(
                                    doc,
                                    writableFields,
                                    cancellationToken)));

                        _unwrittenWritableFields = writableFields;
                    }
                }
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var application = new ReadonlyApplication(_unwrittenWritableFields, await document.GetSemanticModelAsync(cancellationToken));
            return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, application.Visit(root));
        }
    }
}