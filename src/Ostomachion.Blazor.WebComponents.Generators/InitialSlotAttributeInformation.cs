using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ostomachion.Blazor.WebComponents.Generators;

internal record class InitialSlotAttributeInformation
{
    public IMethodSymbol AttributeConstructorSymbol { get; set; } = null!;
    public AttributeArgumentSyntax? SlotNameArgument { get; set; }
    public AttributeArgumentSyntax? RootElementArgument { get; set; }
    public AttributeArgumentSyntax? IsTemplatedArgument { get; set; }
    public AttributeArgumentSyntax? DefaultTextArgument { get; set; }

    private InitialSlotAttributeInformation() { }

    public static InitialSlotAttributeInformation? Parse(AttributeSyntax syntax, GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var attributeConstructorSymbol = context.SemanticModel.GetSymbolInfo(syntax.Name, cancellationToken).Symbol;
        if (attributeConstructorSymbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType.ToString() != "Ostomachion.Blazor.WebComponents.SlotAttribute")
        {
            return null;
        }

        var slotNameArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is null);
        var rootElementArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is NameEqualsSyntax n && n.Name.Identifier.ToString() == "RootElement");
        var isTemplatedArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is NameEqualsSyntax n && n.Name.Identifier.ToString() == "IsTemplated");
        var defaultTextArgument = syntax.ArgumentList?.Arguments.FirstOrDefault(x => x.NameEquals is NameEqualsSyntax n && n.Name.Identifier.ToString() == "DefaultText");

        return new InitialSlotAttributeInformation
        {
            AttributeConstructorSymbol = methodSymbol,
            SlotNameArgument = slotNameArgument,
            RootElementArgument = rootElementArgument,
            IsTemplatedArgument = isTemplatedArgument,
            DefaultTextArgument = defaultTextArgument,
        };
    }
}