using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ostomachion.Blazor.WebComponents.Generators;

[Generator(LanguageNames.CSharp)]
public partial class CustomElementGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get all classes that inherit CustomElementBase.
        // Partial classes (e.g. from Razor) will be included once from each source file.
        var customElementSources = context.SyntaxProvider
            .CreateSyntaxProvider(CustomElementPredicate, CustomElementInitialTransform)
            .Where(x => x is not null);

        // Gather a list of unique classes that inherit CustomElementBase.
        var distinctNames = customElementSources
            .Select((x, _) => new NameInfo(x!.Name, x.Namespace, x.LocalName))
            .Collect()
            .SelectMany((x, _) => NameInfo.Group(x));

        // For each unique CustomElementBase class, output a partial class with common members.
        context.RegisterSourceOutput(distinctNames, CustomElementSourceOutput.CreateCommonFile);

        var slotSources = customElementSources
            .Where(s => s!.RelevantType == RelevantType.WebComponent && s.Slots!.Any())
            .Select((x, _) => new SlotSourceInformation(x!))
            .Collect()
            .SelectMany((x, _) => SlotSourceInformation.Group(x));

        // For each WebComponentBase declaration that defines slots, output a partial class with the slot properties.
        context.RegisterSourceOutput(slotSources, CustomElementSourceOutput.CreateSlotSource);

        // For each CSS file co-located with a WebComponentBase, override the StylesheetUrl property on the component.
        var styledComponentPaths = context.AdditionalTextsProvider
            .Where(x => x.Path.EndsWith(".razor.css") || x.Path.EndsWith(".cs.css"))
            .Select((x, c) => (x.Path, Text: x.GetText(c)!));

        var hasRazorFiles = customElementSources
            .Where(x => Path.GetExtension(x!.OriginalFilePath) == ".razor")
            .Collect()
            .Select((x, _) => x.Any());

        var webComponentStylesheetInformation = customElementSources
            .Where(x => x!.RelevantType == RelevantType.WebComponent)
            .Select((x, _) => x! with { Slots = null })
            .Combine(styledComponentPaths.Collect())
            .Combine(hasRazorFiles)
            .Select((x, _) => ComponentCssInformation.Parse(x.Left.Left, x.Left.Right, x.Right))
            .Collect()
            .SelectMany((x, _) => ComponentCssInformation.Group(x));

        context.RegisterSourceOutput(webComponentStylesheetInformation, CustomElementSourceOutput.CreateStylesheetSource);
    }

    private bool CustomElementPredicate(SyntaxNode n, CancellationToken _) => n is ClassDeclarationSyntax;

    private CustomElementClassInformation? CustomElementInitialTransform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var syntax = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(syntax, cancellationToken)!;

        var relevantType = GetRelevantType(symbol);
        if (relevantType == RelevantType.None)
        {
            return null;
        }

        var attribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToString() == "Ostomachion.Blazor.WebComponents.CustomElementAttribute");
        var arguments = attribute?.NamedArguments.Where(x => x.Key == "Extends");
        var localName = (arguments?.Any() ?? false) ? (string?)arguments.First().Value.Value : null;

        var slots = relevantType == RelevantType.CustomElement ? null : syntax.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(p => InitialPropertyInformation.Parse(p, context, cancellationToken))
            .OfType<InitialPropertyInformation>()
            .Select(x => SlotInformation.Parse(x, context, cancellationToken));
        
        return new CustomElementClassInformation
        {
            OriginalFilePath = GetOriginalFilePath(syntax.SyntaxTree, cancellationToken),
            RelevantType = relevantType,
            Name = symbol.Name,
            Namespace = symbol.ContainingNamespace.ToString(),
            LocalName = localName,
            Slots = slots?.ToArray(),
        };
    }

    private static RelevantType GetRelevantType(INamedTypeSymbol type)
    {
        INamedTypeSymbol? baseType = type.BaseType?.OriginalDefinition;
        while (baseType is not null)
        {
            if (baseType.ToString() == "Ostomachion.Blazor.WebComponents.WebComponentBase")
            {
                return RelevantType.WebComponent;
            }

            if (baseType.ToString() == "Ostomachion.Blazor.WebComponents.CustomElementBase")
            {
                return RelevantType.CustomElement;
            }

            baseType = baseType.BaseType?.OriginalDefinition;
        }

        return RelevantType.None;
    }

    private static string GetOriginalFilePath(SyntaxTree syntaxTree, CancellationToken cancellationToken)
    {
        var path = syntaxTree.FilePath;
        if (!path.EndsWith(".razor.g.cs"))
        {
            return path;
        }

        var sourceText = syntaxTree.GetText(cancellationToken);
        if (!sourceText.Lines.Any())
        {
            return path;
        }

        var firstLine = sourceText.Lines[0].Text!.ToString();
        const string prefix = "#pragma checksum \"";
        if (!firstLine.StartsWith(prefix))
        {
            return path;
        }

        return firstLine.Substring(prefix.Length, firstLine.IndexOf('"', prefix.Length) - prefix.Length);
    }
}
