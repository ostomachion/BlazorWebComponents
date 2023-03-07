﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ostomachion.BlazorWebComponents.Generators;

internal record class InitialAttributeInformation
{
    public IMethodSymbol AttributeConstructorSymbol { get; set; } = null!;
    public AttributeArgumentSyntax? SlotNameArgument { get; set; }
    public AttributeArgumentSyntax? RootElementArgument { get; set; }

    private InitialAttributeInformation() { }

    public static InitialAttributeInformation? Parse(AttributeSyntax syntax, GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var attributeConstructorSymbol = context.SemanticModel.GetSymbolInfo(syntax.Name, cancellationToken).Symbol;
        if (attributeConstructorSymbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType.ToString() != "Ostomachion.BlazorWebComponents.SlotAttribute")
        {
            return null;
        }

        var slotNameArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is null);
        var rootElementArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is NameEqualsSyntax n && n.Name.Identifier.ToString() == "RootElement");

        return new InitialAttributeInformation
        {
            AttributeConstructorSymbol = methodSymbol,
            SlotNameArgument = slotNameArgument,
            RootElementArgument = rootElementArgument,
        };
    }
}