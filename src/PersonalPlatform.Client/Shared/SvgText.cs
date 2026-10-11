using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace PersonalPlatform.Client.Shared;

/// <summary>
/// Renders an SVG &lt;text&gt; element ("text" is a reserved tag in Razor markup). Content is plain text,
/// encoded by the renderer like any other text node.
/// </summary>
public sealed class SvgText : ComponentBase
{
    [Parameter] public string X { get; set; } = "0";

    [Parameter] public string Y { get; set; } = "0";

    [Parameter] public string Anchor { get; set; } = "start";

    [Parameter] public string Text { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "x", X);
        builder.AddAttribute(2, "y", Y);
        builder.AddAttribute(3, "text-anchor", Anchor);
        builder.AddContent(4, Text);
        builder.CloseElement();
    }
}
