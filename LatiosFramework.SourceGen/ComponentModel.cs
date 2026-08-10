using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LatiosFramework.SourceGen
{
    internal readonly struct ComponentScope : IEquatable<ComponentScope>
    {
        public readonly string Declaration;
        public readonly bool   HasBurstCompile;

        public ComponentScope(string declaration, bool hasBurstCompile)
        {
            Declaration     = declaration;
            HasBurstCompile = hasBurstCompile;
        }

        public bool Equals(ComponentScope other)
            => string.Equals(Declaration, other.Declaration, StringComparison.Ordinal)
            && HasBurstCompile == other.HasBurstCompile;

        public override bool Equals(object obj)
            => obj is ComponentScope other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Declaration ?? string.Empty);
                hash = hash * 31 + HasBurstCompile.GetHashCode();
                return hash;
            }
        }
    }

    internal readonly struct ComponentModel : IEquatable<ComponentModel>
    {
        public readonly bool                           IsValid;
        public readonly string                         HintName;
        public readonly string                         Namespace;
        public readonly ImmutableArray<ComponentScope> ContainingScopes;
        public readonly string                         TargetModifiers;
        public readonly string                         TargetIdentifier;

        public ComponentModel(
            bool isValid,
            string hintName,
            string namespaceName,
            ImmutableArray<ComponentScope> containingScopes,
            string targetModifiers,
            string targetIdentifier
        )
        {
            IsValid          = isValid;
            HintName         = hintName;
            Namespace        = namespaceName;
            ContainingScopes = containingScopes;
            TargetModifiers  = targetModifiers;
            TargetIdentifier = targetIdentifier;
        }

        public static bool IsCandidate(
            SyntaxNode syntaxNode,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(syntaxNode is StructDeclarationSyntax structDeclaration))
                return false;

            foreach (var modifier in structDeclaration.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PartialKeyword))
                    return true;
            }
            return false;
        }

        public static ComponentModel Create(
            GeneratorSyntaxContext context,
            CancellationToken cancellationToken,
            string fullSemanticInterfaceName,
            string outputRole
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!(context.Node is StructDeclarationSyntax structDeclaration) || structDeclaration.BaseList == null)
                return default;

            const string globalPrefix = "global::";
            var metadataName = fullSemanticInterfaceName.StartsWith(globalPrefix, StringComparison.Ordinal)
                ? fullSemanticInterfaceName.Substring(globalPrefix.Length)
                : fullSemanticInterfaceName;
            var expectedInterface = context.SemanticModel.Compilation.GetTypeByMetadataName(metadataName);
            if (expectedInterface == null)
                return default;

            var matchesInterface = false;
            foreach (var baseType in structDeclaration.BaseList.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = context.SemanticModel.GetTypeInfo(baseType.Type, cancellationToken).Type;
                if (SymbolEqualityComparer.Default.Equals(type, expectedInterface))
                {
                    matchesInterface = true;
                    break;
                }
            }
            if (!matchesInterface)
                return default;

            var targetSymbol = context.SemanticModel.GetDeclaredSymbol(structDeclaration, cancellationToken);
            if (targetSymbol == null)
                return default;

            var scopes = new List<ComponentScope>();
            for (var parent = structDeclaration.Parent; parent != null; parent = parent.Parent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (parent is TypeDeclarationSyntax typeDeclaration)
                    scopes.Add(CreateScope(typeDeclaration));
            }
            scopes.Reverse();

            var semanticIdentity = targetSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var namespaceName    = targetSymbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : targetSymbol.ContainingNamespace.ToDisplayString();

            return new ComponentModel(
                true,
                CreateHintName(semanticIdentity, outputRole),
                namespaceName,
                ImmutableArray.CreateRange(scopes),
                JoinModifiers(structDeclaration.Modifiers),
                structDeclaration.Identifier.Text
            );
        }

        public bool Equals(ComponentModel other)
        {
            if (IsValid != other.IsValid
                || !string.Equals(HintName, other.HintName, StringComparison.Ordinal)
                || !string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
                || !string.Equals(TargetModifiers, other.TargetModifiers, StringComparison.Ordinal)
                || !string.Equals(TargetIdentifier, other.TargetIdentifier, StringComparison.Ordinal)
                || GetScopeCount(ContainingScopes) != GetScopeCount(other.ContainingScopes))
                return false;

            for (var i = 0; i < GetScopeCount(ContainingScopes); i++)
            {
                if (!ContainingScopes[i].Equals(other.ContainingScopes[i]))
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj)
            => obj is ComponentModel other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + IsValid.GetHashCode();
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(HintName ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Namespace ?? string.Empty);
                for (var i = 0; i < GetScopeCount(ContainingScopes); i++)
                    hash = hash * 31 + ContainingScopes[i].GetHashCode();
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(TargetModifiers ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(TargetIdentifier ?? string.Empty);
                return hash;
            }
        }

        private static ComponentScope CreateScope(TypeDeclarationSyntax declaration)
            => new ComponentScope(CreateScopeDeclaration(declaration), HasBurstCompile(declaration));

        private static string CreateScopeDeclaration(TypeDeclarationSyntax declaration)
        {
            var builder   = new StringBuilder();
            var modifiers = JoinModifiers(declaration.Modifiers);
            if (modifiers.Length > 0)
                builder.Append(modifiers).Append(' ');
            builder.Append(declaration.Keyword.Text);
            if (declaration is RecordDeclarationSyntax record && record.ClassOrStructKeyword.RawKind != 0)
                builder.Append(' ').Append(record.ClassOrStructKeyword.Text);
            builder.Append(' ').Append(declaration.Identifier.Text);
            if (declaration.TypeParameterList != null)
                builder.Append(declaration.TypeParameterList.ToString());
            foreach (var constraintClause in declaration.ConstraintClauses)
                builder.Append(' ').Append(constraintClause.ToString());
            return builder.ToString();
        }

        private static string CreateHintName(string semanticIdentity, string outputRole)
        {
            var builder = new StringBuilder(semanticIdentity.Length + outputRole.Length + 16);
            foreach (var character in semanticIdentity)
            {
                if ((character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9'))
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('_')
                        .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture))
                        .Append('_');
                }
            }
            return builder.Append('.').Append(outputRole).Append(".g.cs").ToString();
        }

        private static int GetScopeCount(ImmutableArray<ComponentScope> scopes)
            => scopes.IsDefault ? 0 : scopes.Length;

        private static string GetRightmostIdentifier(TypeSyntax type)
        {
            switch (type)
            {
                case SimpleNameSyntax simpleName:
                    return simpleName.Identifier.ValueText;
                case QualifiedNameSyntax qualifiedName:
                    return qualifiedName.Right.Identifier.ValueText;
                case AliasQualifiedNameSyntax aliasQualifiedName:
                    return aliasQualifiedName.Name.Identifier.ValueText;
                default:
                    return string.Empty;
            }
        }

        private static bool HasBurstCompile(TypeDeclarationSyntax declaration)
        {
            foreach (var attributeList in declaration.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var identifier = GetRightmostIdentifier(attribute.Name);
                    if (identifier == "BurstCompile" || identifier == "BurstCompileAttribute")
                        return true;
                }
            }
            return false;
        }

        private static string JoinModifiers(SyntaxTokenList modifiers)
        {
            if (modifiers.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (var i = 0; i < modifiers.Count; i++)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(modifiers[i].Text);
            }
            return builder.ToString();
        }
    }
}
