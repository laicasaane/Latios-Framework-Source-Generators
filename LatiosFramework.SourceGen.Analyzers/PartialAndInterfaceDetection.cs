using System.Linq;
using LatiosFramework.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LatiosFramework.SourceGen.Analyzers
{
    // Shared between the Core and Unika analyzer projects (linked in via <Compile Include>, since Roslyn
    // analyzer DLLs are loaded by Unity as independent, self-contained plugins and don't reference each
    // other at runtime).
    internal static class PartialAndInterfaceDetection
    {
        public static void AnalyzeTypeForMissingPartial(SyntaxNodeAnalysisContext context, MarkerInfo[] markers, string excludedModulePath,
                                                          DiagnosticDescriptor selfDescriptor, DiagnosticDescriptor parentDescriptor)
        {
            var typeDeclaration = (TypeDeclarationSyntax)context.Node;

            if (IsExcludedFromAnalysis(typeDeclaration, excludedModulePath))
                return;

            if (!TryFindMatchingMarker(typeDeclaration, context.SemanticModel, markers, out var markerShortName, out var typeSymbol))
                return;

            for (var parent = typeDeclaration.Parent; parent is TypeDeclarationSyntax parentType; parent = parent.Parent)
            {
                if (!HasPartialModifier(parentType))
                {
                    var parentSymbol = context.SemanticModel.GetDeclaredSymbol(parentType) as ITypeSymbol;
                    context.ReportDiagnostic(Diagnostic.Create(parentDescriptor, parentType.Identifier.GetLocation(),
                                                               markerShortName, typeSymbol.ToFullName(), parentSymbol?.ToFullName() ?? parentType.Identifier.Text));
                }
            }

            if (!HasPartialModifier(typeDeclaration))
            {
                context.ReportDiagnostic(Diagnostic.Create(selfDescriptor, typeDeclaration.Identifier.GetLocation(),
                                                           markerShortName, typeSymbol.ToFullName()));
            }
        }

        public static void AnalyzeTypeForMissingExplicitInterface(SyntaxNodeAnalysisContext context, MarkerInfo[] markers, string excludedModulePath,
                                                                    DiagnosticDescriptor descriptor)
        {
            var typeDeclaration = context.Node as TypeDeclarationSyntax;
            bool isStruct        = typeDeclaration is StructDeclarationSyntax;
            bool isInterface     = typeDeclaration is InterfaceDeclarationSyntax;
            if (typeDeclaration == null || (!isStruct && !isInterface))
                return;

            if (IsExcludedFromAnalysis(typeDeclaration, excludedModulePath))
                return;

            var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
            if (typeSymbol == null)
                return;

            foreach (var marker in markers)
            {
                if (!marker.RequiresExplicitListing)
                    continue;
                if (marker.TargetKind == MarkerTargetKind.Struct && !isStruct)
                    continue;
                if (marker.TargetKind == MarkerTargetKind.Interface && !isInterface)
                    continue;

                bool implementsTransitively = typeSymbol.AllInterfaces.Any(i => i.ToFullName() == marker.FullName);
                if (!implementsTransitively)
                    continue;

                bool explicitlyListed = typeDeclaration.BaseList != null &&
                                         typeDeclaration.BaseList.Types.Any(bt => context.SemanticModel.GetTypeInfo(bt.Type).Type?.ToFullName() == marker.FullName);

                if (!explicitlyListed)
                {
                    context.ReportDiagnostic(Diagnostic.Create(descriptor, typeDeclaration.Identifier.GetLocation(), marker.ShortName, typeSymbol.ToFullName()));
                }
            }
        }

        static bool TryFindMatchingMarker(TypeDeclarationSyntax typeDeclaration, SemanticModel semanticModel, MarkerInfo[] markers,
                                           out string markerShortName, out ITypeSymbol typeSymbol)
        {
            markerShortName = null;
            typeSymbol       = null;

            bool isStruct    = typeDeclaration is StructDeclarationSyntax;
            bool isInterface = typeDeclaration is InterfaceDeclarationSyntax;
            bool isClass     = typeDeclaration is ClassDeclarationSyntax;

            if (typeDeclaration.BaseList == null || typeDeclaration.BaseList.Types.Count == 0)
                return false;

            foreach (var marker in markers)
            {
                switch (marker.TargetKind)
                {
                    case MarkerTargetKind.Struct:
                        if (!isStruct)
                            continue;
                        break;
                    case MarkerTargetKind.Interface:
                        if (!isInterface)
                            continue;
                        break;
                    case MarkerTargetKind.ClassBaseTypesBaseType:
                    case MarkerTargetKind.ClassDirectOriginalDefinition:
                        // An abstract class can never be the leaf candidate the generator is looking for (a
                        // concrete user authoring component) — only a structural side effect of an abstract
                        // framework/addon base class re-deriving from the same generic base one level down
                        // (e.g. UnikaScriptAutoAuthoring<T> : UnikaScriptAuthoring<T>).
                        if (!isClass || HasAbstractModifier((ClassDeclarationSyntax)typeDeclaration))
                            continue;
                        break;
                }

                if (marker.TargetKind == MarkerTargetKind.Struct || marker.TargetKind == MarkerTargetKind.Interface)
                {
                    if (marker.AllowIndirect)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as ITypeSymbol;
                        if (symbol != null && symbol.AllInterfaces.Any(i => i.ToFullName() == marker.FullName))
                        {
                            markerShortName = marker.ShortName;
                            typeSymbol       = symbol;
                            return true;
                        }
                    }
                    else
                    {
                        foreach (var baseType in typeDeclaration.BaseList.Types)
                        {
                            if (BaseTypeTextMatches(baseType.Type, marker.ShortName) &&
                                semanticModel.GetTypeInfo(baseType.Type).Type?.ToFullName() == marker.FullName)
                            {
                                markerShortName = marker.ShortName;
                                typeSymbol       = semanticModel.GetDeclaredSymbol(typeDeclaration) as ITypeSymbol;
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var baseType in typeDeclaration.BaseList.Types)
                    {
                        if (!BaseTypeTextMatches(baseType.Type, marker.ShortName))
                            continue;

                        var type = semanticModel.GetTypeInfo(baseType.Type).Type;
                        if (type == null)
                            continue;

                        bool matched = marker.TargetKind == MarkerTargetKind.ClassBaseTypesBaseType
                            ? type.BaseType?.ToFullName() == marker.FullName
                            : type.OriginalDefinition?.ToFullName() == marker.FullName;

                        if (matched)
                        {
                            markerShortName = marker.ShortName;
                            typeSymbol       = semanticModel.GetDeclaredSymbol(typeDeclaration) as ITypeSymbol;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        static bool BaseTypeTextMatches(TypeSyntax typeSyntax, string shortName)
        {
            switch (typeSyntax)
            {
                case IdentifierNameSyntax id: return id.Identifier.ValueText == shortName;
                case GenericNameSyntax gen: return gen.Identifier.ValueText == shortName;
                case QualifiedNameSyntax qual: return BaseTypeTextMatches(qual.Right, shortName);
                default: return false;
            }
        }

        static bool HasPartialModifier(TypeDeclarationSyntax typeDeclaration)
        {
            foreach (var m in typeDeclaration.Modifiers)
                if (m.IsKind(SyntaxKind.PartialKeyword))
                    return true;
            return false;
        }

        static bool HasAbstractModifier(ClassDeclarationSyntax classDeclaration)
        {
            foreach (var m in classDeclaration.Modifiers)
                if (m.IsKind(SyntaxKind.AbstractKeyword))
                    return true;
            return false;
        }

        // Skips generator output, which Roslyn's GeneratedCodeAnalysisFlags heuristic misses because Latios
        // hint names end in ".gen.cs" rather than ".g.cs". Also skips the calling analyzer's own module,
        // since RunOnlyOnAssembliesWithReference runs it against Latios's own assemblies, where the
        // framework's abstract base hierarchies match structurally without being user mistakes. Scoping the
        // skip to one module keeps a Core analyzer able to flag real mistakes in Unika, and vice versa.
        static bool IsExcludedFromAnalysis(TypeDeclarationSyntax typeDeclaration, string excludedModulePath)
        {
            var filePath = typeDeclaration.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return false;
            if (filePath.EndsWith(".gen.cs", System.StringComparison.OrdinalIgnoreCase))
                return true;
            var normalizedFilePath = filePath.Replace('\\', '/');
            if (normalizedFilePath.IndexOf(excludedModulePath, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }
    }
}
