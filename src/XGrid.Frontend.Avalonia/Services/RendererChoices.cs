using System.Collections.Generic;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Turns a core's reported capabilities into the renderer choices the settings
/// page offers. A build without Vulkan must not offer it: selecting it only
/// makes the core log a fallback while the shell keeps claiming Vulkan.
/// </summary>
public static class RendererChoices
{
    /// <summary>Returns the renderer keys to offer, best first.</summary>
    /// <param name="capabilities">Core capabilities, or null when unknown.</param>
    /// <returns>The keys, always including OpenGL and the null renderer.</returns>
    public static IReadOnlyList<string> Keys(CoreCapabilities? capabilities)
    {
        if (capabilities is { SupportsVulkan: false })
        {
            return [CoreCapabilities.RendererOpenGl, CoreCapabilities.RendererNull];
        }

        // No manifest: the shell cannot prove Vulkan is missing, so it keeps
        // offering what an older configuration may have selected.
        return [CoreCapabilities.RendererOpenGl, CoreCapabilities.RendererVulkan, CoreCapabilities.RendererNull];
    }

    /// <summary>Returns the display label for a renderer key.</summary>
    /// <param name="key">Renderer key.</param>
    /// <returns>The label shown in the renderer list.</returns>
    public static string Label(string key)
    {
        return key switch
        {
            CoreCapabilities.RendererOpenGl => "OpenGL",
            CoreCapabilities.RendererVulkan => "Vulkan",
            CoreCapabilities.RendererNull => "Null (headless, no picture)",
            _ => key,
        };
    }

    /// <summary>Returns the hint under the renderer row.</summary>
    /// <param name="capabilities">Core capabilities, or null when unknown.</param>
    /// <returns>The hint text.</returns>
    public static string Hint(CoreCapabilities? capabilities)
    {
        return capabilities switch
        {
            { SupportsVulkan: true } => "OpenGL and Vulkan are both built into this emulator.",
            { SupportsVulkan: false } => "This emulator build has no Vulkan renderer, so Vulkan is not offered.",
            _ => "This emulator does not report which renderers it has, so Vulkan is offered but may fall back to OpenGL.",
        };
    }
}
