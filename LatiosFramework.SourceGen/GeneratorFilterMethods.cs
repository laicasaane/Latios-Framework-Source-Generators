using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    /// <summary>
    /// A matched generator candidate: the declaration to emit from, plus the fully-qualified name of the
    /// type it declares, which is the only name unique enough to build an AddSource hint name from.
    /// </summary>
    /// <remarks>
    /// Equality is left at reference equality, matching what passing the raw syntax node used to do: a
    /// fresh node is produced on every edit either way, so nothing downstream caches across edits today.
    /// Value equality on the name alone would be wrong - an edit inside the type would stop regenerating.
    /// </remarks>
    public sealed class GeneratorCandidate<TDeclaration> where TDeclaration : TypeDeclarationSyntax
    {
        public readonly TDeclaration declaration;
        public readonly string       typeFullName;

        public GeneratorCandidate(TDeclaration declaration, string typeFullName)
        {
            this.declaration  = declaration;
            this.typeFullName = typeFullName;
        }

        /// <summary>
        /// Builds the hint name to pass to AddSource, which must be unique across the whole compilation.
        /// Roslyn answers a duplicate by disabling the generator for every candidate in the assembly, so
        /// this is derived from the fully-qualified type name and never from the source file name.
        /// </summary>
        /// <param name="suffix">The generator's own suffix, e.g. "_IJobEach.gen.cs"</param>
        public string HintName(string suffix)
        {
            var builder = new StringBuilder(typeFullName.Length + suffix.Length);
            foreach (var c in typeFullName)
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            return builder.Append(suffix).ToString();
        }
    }

    public static class GeneratorFilterMethods
    {
        #region Structs implementing a marker interface
        // Based on Unity's IJobEntity source generator
        public static bool IsSyntaxStructInterfaceMatch(SyntaxNode syntaxNode, CancellationToken cancellationToken, in string interfaceName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return syntaxNode is StructDeclarationSyntax declaration && HasInterfaceInBaseList(declaration, interfaceName) && IsPartial(declaration);
        }

        public static GeneratorCandidate<StructDeclarationSyntax> GetSemanticStructInterfaceMatch(GeneratorSyntaxContext ctx,
                                                                                                  CancellationToken cancellationToken,
                                                                                                  string fullSemanticInterfaceName,
                                                                                                  string interfaceName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = (StructDeclarationSyntax)ctx.Node;
            if (!AnyBaseTypeIs(ctx, declaration, fullSemanticInterfaceName))
                return null;
            return MakeCandidate(ctx, declaration, interfaceName, false, cancellationToken);
        }
        #endregion

        #region Interfaces extending a marker interface
        public static bool IsSyntaxInterfaceInterfaceMatch(SyntaxNode syntaxNode, CancellationToken cancellationToken, in string interfaceName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return syntaxNode is InterfaceDeclarationSyntax declaration && HasInterfaceInBaseList(declaration, interfaceName) && IsPartial(declaration);
        }

        public static GeneratorCandidate<InterfaceDeclarationSyntax> GetSemanticInterfaceInterfaceMatch(GeneratorSyntaxContext ctx,
                                                                                                        CancellationToken cancellationToken,
                                                                                                        string fullSemanticInterfaceName,
                                                                                                        string interfaceName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = (InterfaceDeclarationSyntax)ctx.Node;
            if (!AnyBaseTypeIs(ctx, declaration, fullSemanticInterfaceName))
                return null;
            return MakeCandidate(ctx, declaration, interfaceName, false, cancellationToken);
        }
        #endregion

        #region Classes deriving from a generic base class
        public static bool IsSyntaxClassGenericMatch(SyntaxNode syntaxNode, CancellationToken cancellationToken, in string classNameWithoutGenerics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return syntaxNode is ClassDeclarationSyntax declaration && HasGenericBaseInBaseList(declaration, classNameWithoutGenerics) && IsPartial(declaration);
        }

        /// <summary>
        /// Matches a class whose base type's own base type is the named type, e.g.
        /// UnikaScriptAuthoring&lt;T&gt; deriving from UnikaScriptAuthoringBase.
        /// </summary>
        public static GeneratorCandidate<ClassDeclarationSyntax> GetSemanticClassGenericMatch(GeneratorSyntaxContext ctx,
                                                                                              CancellationToken cancellationToken,
                                                                                              string fullSemanticBaseClassName,
                                                                                              string classNameWithoutGenerics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = (ClassDeclarationSyntax)ctx.Node;
            var matched     = false;
            foreach (var baseTypeSyntax in declaration.BaseList !.Types)
            {
                var type = ctx.SemanticModel.GetTypeInfo(baseTypeSyntax.Type, cancellationToken).Type;
                if (type?.BaseType != null && type.BaseType.ToFullName() == fullSemanticBaseClassName)
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                return null;
            return MakeCandidate(ctx, declaration, classNameWithoutGenerics, true, cancellationToken);
        }

        /// <summary>
        /// Matches a class whose base type itself is the named generic type, e.g.
        /// UnikaScriptAutoAuthoring&lt;T&gt;, which is the type the user derives from directly.
        /// </summary>
        public static GeneratorCandidate<ClassDeclarationSyntax> GetSemanticDirectGenericMatch(GeneratorSyntaxContext ctx,
                                                                                               CancellationToken cancellationToken,
                                                                                               string fullSemanticGenericDefinitionName,
                                                                                               string classNameWithoutGenerics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = (ClassDeclarationSyntax)ctx.Node;
            var matched     = false;
            foreach (var baseTypeSyntax in declaration.BaseList !.Types)
            {
                var type = ctx.SemanticModel.GetTypeInfo(baseTypeSyntax.Type, cancellationToken).Type;
                if (type != null && type.OriginalDefinition.ToFullName() == fullSemanticGenericDefinitionName)
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
                return null;
            return MakeCandidate(ctx, declaration, classNameWithoutGenerics, true, cancellationToken);
        }
        #endregion

        #region Shared
        static GeneratorCandidate<TDeclaration> MakeCandidate<TDeclaration>(GeneratorSyntaxContext ctx,
                                                                            TDeclaration declaration,
                                                                            string markerIdentifier,
                                                                            bool markerIsGenericBase,
                                                                            CancellationToken cancellationToken) where TDeclaration : TypeDeclarationSyntax
        {
            if (!(ctx.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol symbol))
                return null;
            if (!IsPrimaryDeclaration(symbol, declaration, markerIdentifier, markerIsGenericBase, cancellationToken))
                return null;
            return new GeneratorCandidate<TDeclaration>(declaration, symbol.ToFullName());
        }

        // C# lets a partial type restate its marker on every declaration, and the syntax provider yields
        // each one, so only the first marker-bearing declaration is allowed to generate. A second AddSource
        // with the same hint name disables the generator for the entire compilation.
        //
        // Choosing among marker-bearing declarations rather than taking DeclaringSyntaxReferences[0]
        // outright keeps the choice stable when an unrelated partial declaration is added in a file that
        // sorts earlier, and keeps the modifiers the code writers copy coming from a declaration that
        // actually names the marker.
        static bool IsPrimaryDeclaration(INamedTypeSymbol symbol,
                                         TypeDeclarationSyntax declaration,
                                         string markerIdentifier,
                                         bool markerIsGenericBase,
                                         CancellationToken cancellationToken)
        {
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!(reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax other))
                    continue;
                if (markerIsGenericBase ? !HasGenericBaseInBaseList(other, markerIdentifier) : !HasInterfaceInBaseList(other, markerIdentifier))
                    continue;
                return ReferenceEquals(other, declaration) || (other.SyntaxTree == declaration.SyntaxTree && other.Span == declaration.Span);
            }
            return true;
        }

        static bool HasInterfaceInBaseList(TypeDeclarationSyntax declaration, string interfaceName)
        {
            if (declaration.BaseList == null)
                return false;

            foreach (var baseType in declaration.BaseList.Types)
            {
                if (baseType.Type is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == interfaceName)
                    return true;
                if (baseType.Type is QualifiedNameSyntax qualified && qualified.Right.Identifier.ValueText == interfaceName)
                    return true;
            }
            return false;
        }

        static bool HasGenericBaseInBaseList(TypeDeclarationSyntax declaration, string classNameWithoutGenerics)
        {
            if (declaration.BaseList == null)
                return false;

            foreach (var baseType in declaration.BaseList.Types)
            {
                if (baseType.Type is GenericNameSyntax generic && generic.Identifier.ValueText == classNameWithoutGenerics)
                    return true;
                if (baseType.Type is QualifiedNameSyntax qualified && qualified.Right.Identifier.ValueText == classNameWithoutGenerics)
                    return true;
            }
            return false;
        }

        // Null when the base type does not resolve, which happens while the user is mid-edit.
        static bool AnyBaseTypeIs(GeneratorSyntaxContext ctx, TypeDeclarationSyntax declaration, string fullSemanticName)
        {
            foreach (var baseTypeSyntax in declaration.BaseList !.Types)
            {
                var type = ctx.SemanticModel.GetTypeInfo(baseTypeSyntax.Type).Type;
                if (type != null && type.ToFullName() == fullSemanticName)
                    return true;
            }
            return false;
        }

        static bool IsPartial(TypeDeclarationSyntax declaration)
        {
            foreach (var modifier in declaration.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PartialKeyword))
                    return true;
            }
            return false;
        }
        #endregion
    }
}
