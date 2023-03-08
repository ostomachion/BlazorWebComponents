using Microsoft.AspNetCore.Components;

namespace Ostomachion.BlazorWebComponents.Demo.Demos.ComposedComposedPath;

[CustomElement("closed-shadow")]
public partial class ClosedShadow
{
    public override ShadowRootMode ShadowRootMode => ShadowRootMode.Closed;

    [Parameter]
    [EditorRequired]
    public string Text { get; set; } = default!;
}
