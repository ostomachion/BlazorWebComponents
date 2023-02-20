﻿using System;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ostomachion.BlazorWebComponents.Generators;

[Generator]
public partial class WebComponentGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Do a simple filter for classes.
        var webComponentClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null)!;

        // Combine the selected classes with the `Compilation`.
        var compilationAndClasses = context.CompilationProvider.Combine(webComponentClasses.Collect());

        // Generate the source using the compilation and classes.
        context.RegisterSourceOutput(compilationAndClasses,
            static (context, source) => GenerateSource(context, source.Right!));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static WebComponentSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;
        var attributeSyntax = GetAttribute("Ostomachion.BlazorWebComponents.WebComponentAttribute", context, classDeclarationSyntax.AttributeLists);
        var slotSyntaxes = GetSlotProperties(context);
        return attributeSyntax is null ? null : new WebComponentSyntax(classDeclarationSyntax, attributeSyntax, slotSyntaxes);
    }

    private static IEnumerable<SlotSyntax> GetSlotProperties(GeneratorSyntaxContext context)
    {
        var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;
        var properties = classDeclarationSyntax.Members.OfType<PropertyDeclarationSyntax>();
        foreach (var property in properties)
        {
            var attribute = GetAttribute("Ostomachion.BlazorWebComponents.SlotAttribute", context, property.AttributeLists);
            if (attribute is not null)
            {
                string typeString;
                var symbol = context.SemanticModel.GetTypeInfo(property.Type).Type;
                typeString = symbol!.ToDisplayString();

                yield return new SlotSyntax(property, attribute, typeString);
            }
        }
    }

    private static AttributeSyntax? GetAttribute(string fullName, GeneratorSyntaxContext context, SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeListSyntax in attributeLists)
        {
            foreach (var attributeSyntax in attributeListSyntax.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol is not IMethodSymbol attributeSymbol)
                {
                    continue;
                }

                if (attributeSymbol.ContainingType.ToDisplayString() == fullName)
                {
                    return attributeSyntax;
                }
            }
        }

        return null;
    }

    private static void GenerateSource(SourceProductionContext context, ImmutableArray<WebComponentSyntax> webComponentSyntaxes)
    {
        foreach (var item in webComponentSyntaxes)
        {
            var className = item.ClassDeclarationSyntax.Identifier.Text;

            var namespaceName = GetNamespace(item.ClassDeclarationSyntax);

            // TODO: Get actual value, not the expresssion.
            var tagNameExpression = item.AttributeSyntax.ArgumentList!.Arguments.First()
                .Expression.NormalizeWhitespace().ToFullString();

            var filePath = item.ClassDeclarationSyntax.SyntaxTree.FilePath;
            var htmlPath = Path.GetFileName(filePath).EndsWith(".razor.cs") ?
                Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + ".html") :
                filePath + ".html";
            var cssPath = htmlPath + ".css";

            var slotTemplateHtml = new StringBuilder(); // TODO: ???

            var templateHtml = File.Exists(htmlPath) ? File.ReadAllText(htmlPath) : "<slot></slot>";

            var templateCss = File.Exists(cssPath) ? File.ReadAllText(cssPath) : null;

            var builder = new StringBuilder()
                .AppendLine($$"""
                    #nullable enable

                    using Microsoft.AspNetCore.Components;
                    using Microsoft.AspNetCore.Components.Rendering;
                    using Ostomachion.BlazorWebComponents;

                    namespace {{namespaceName}};

                    partial class {{className}} : IWebComponent
                    {
                        [Parameter]
                        public ShadowRootMode ShadowRootMode { get; set; }

                        public static string TagName => {{tagNameExpression}};

                        public static string TemplateHtml => {{SymbolDisplay.FormatLiteral(templateHtml, true)}};

                        public static string? TemplateCss => {{(templateCss is null ? "null" : SymbolDisplay.FormatLiteral(templateCss, true))}};
                    """);

            foreach (var slotSyntax in item.SlotSyntaxes)
            {
                var type = slotSyntax.TypeString;
                var name = slotSyntax.PropertyDeclarationSyntax.Identifier.ValueText;
                var isTemplated = slotSyntax.AttributeSyntax.ArgumentList?.Arguments
                    .Any(x => x.NormalizeWhitespace().GetText().ContentEquals(SourceText.From("IsTemplated = true"))) ?? false;
                if (!isTemplated)
                {
                    continue;
                }

                _ = builder.AppendLine()
                    .AppendLine($$"""
                            
                            [Parameter]
                            public RenderFragment<{{type}}> {{name}}Template { get; set; } = null!;
                        """);
            }

            if (item.SlotSyntaxes.Any())
            {
                _ = builder.Append("""

                        protected override void BuildRenderTreeSlots(RenderTreeBuilder builder)
                        {
                    """);

                var sequence = 0;
                foreach (var slotSyntax in item.SlotSyntaxes)
                {
                    _ = builder.AppendLine();

                    var type = slotSyntax.TypeString;
                    var name = slotSyntax.PropertyDeclarationSyntax.Identifier.ValueText;
                    var isTemplated = slotSyntax.AttributeSyntax.ArgumentList!.Arguments
                        .Any(x => x.NormalizeWhitespace().GetText().ContentEquals(SourceText.From("IsTemplated = true")));

                    var slotName = slotSyntax.AttributeSyntax.ArgumentList!.Arguments.First().GetText();

                    var rootElementName = slotSyntax.AttributeSyntax.ArgumentList!.Arguments
                        .FirstOrDefault(x => x.NormalizeWhitespace().GetText().ToString().StartsWith("RootElement = "))
                        ?.NormalizeWhitespace().GetText().ToString().Substring("RootElement = ".Length);

                    rootElementName ??= isTemplated || type == "Microsoft.AspNetCore.Components.RenderFragment" ? "\"div\"" : "\"span\"";

                    _ = builder.AppendLine($$"""
                                builder.OpenElement({{sequence++}}, {{rootElementName}});
                                builder.AddAttribute({{sequence++}}, "slot", {{slotName}});
                        """);

                    if (isTemplated)
                    {
                        _ = builder.AppendLine($$"""
                                    builder.AddContent({{sequence++}}, this.{{name}}Template, this.{{name}});
                            """);
                    }
                    else
                    {
                        _ = builder.AppendLine($$"""
                                    builder.AddContent({{sequence++}}, this.{{name}});
                            """);
                    }

                    _ = builder.AppendLine($$"""
                                builder.CloseElement();
                        """);
                }

                _ = builder.AppendLine("    }");
            }

            _ = builder.Append("}");

            context.AddSource($"{className}.g.cs", builder.ToString());
        }
    }

    private static string GetNamespace(BaseTypeDeclarationSyntax syntax)
    {
        var value = string.Empty;

        SyntaxNode? potentialNamespaceParent = syntax.Parent;

        while (potentialNamespaceParent is not null
            and not NamespaceDeclarationSyntax
            and not FileScopedNamespaceDeclarationSyntax)
        {
            potentialNamespaceParent = potentialNamespaceParent.Parent;
        }

        if (potentialNamespaceParent is BaseNamespaceDeclarationSyntax namespaceParent)
        {
            value = namespaceParent.Name.ToString();

            while (true)
            {
                if (namespaceParent.Parent is not NamespaceDeclarationSyntax parent)
                {
                    break;
                }

                value = $"{namespaceParent.Name}.{value}";
                namespaceParent = parent;
            }
        }

        return value;
    }
}
