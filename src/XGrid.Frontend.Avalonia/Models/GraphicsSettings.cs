using System;
using System.Collections.Generic;
using System.Linq;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>Graphics options written into the xemu core configuration.</summary>
/// <remarks>
/// xemu has OpenGL, Vulkan, Direct3D 11 and a null renderer. Direct3D 11 is
/// Windows-only; the shell only offers it when the core manifest lists it.
/// </remarks>
public sealed class GraphicsSettings
{
    /// <summary>Renderer key for the OpenGL backend.</summary>
    public const string RendererOpenGl = "OPENGL";

    /// <summary>Renderer key for the Vulkan backend.</summary>
    public const string RendererVulkan = "VULKAN";

    /// <summary>Renderer key for the Windows Direct3D 11 backend.</summary>
    public const string RendererDirect3D11 = "D3D11";

    /// <summary>Renderer key for the headless null backend.</summary>
    public const string RendererNull = "NULL";

    /// <summary>Texture filtering key for linear sampling.</summary>
    public const string FilterLinear = "linear";

    /// <summary>Texture filtering key for nearest sampling.</summary>
    public const string FilterNearest = "nearest";

    /// <summary>Gets or sets the core renderer (<c>OPENGL</c>, <c>VULKAN</c>, <c>D3D11</c> or <c>NULL</c>).</summary>
    public string Renderer { get; set; } = RendererOpenGl;

    /// <summary>Gets or sets the internal surface scale (1-8).</summary>
    public int SurfaceScale { get; set; } = 1;

    /// <summary>Gets or sets the core texture filtering (<c>linear</c> or <c>nearest</c>).</summary>
    public string Filtering { get; set; } = FilterLinear;

    /// <summary>Gets or sets the core window startup size key.</summary>
    public string StartupSize { get; set; } = "1280x960";

    /// <summary>Gets or sets a value indicating whether the core waits for vertical sync.</summary>
    public bool VSync { get; set; }

    /// <summary>Gets or sets a value indicating whether the core starts fullscreen.</summary>
    public bool FullscreenOnStartup { get; set; }

    /// <summary>Gets or sets a value indicating whether fullscreen is exclusive.</summary>
    public bool FullscreenExclusive { get; set; }

    /// <summary>Gets or sets the aspect-ratio mode (<c>native</c>, <c>auto</c>, <c>4x3</c>, <c>16x9</c>).</summary>
    public string AspectRatio { get; set; } = "auto";

    /// <summary>Gets or sets the fit mode (<c>center</c>, <c>scale</c>, <c>stretch</c>).</summary>
    public string Fit { get; set; } = "scale";

    /// <summary>Gets or sets the reported AV pack.</summary>
    public string AvPack { get; set; } = "hdtv";

    /// <summary>Creates an independent copy of these settings.</summary>
    public GraphicsSettings Clone()
    {
        return new GraphicsSettings
        {
            Renderer = Renderer,
            SurfaceScale = SurfaceScale,
            Filtering = Filtering,
            StartupSize = StartupSize,
            VSync = VSync,
            FullscreenOnStartup = FullscreenOnStartup,
            FullscreenExclusive = FullscreenExclusive,
            AspectRatio = AspectRatio,
            Fit = Fit,
            AvPack = AvPack,
        };
    }

    /// <summary>Repairs values loaded from disk.</summary>
    public void Normalize()
    {
        Renderer = NullSafe.Text(Renderer).ToUpperInvariant() switch
        {
            RendererVulkan => RendererVulkan,
            RendererDirect3D11 => RendererDirect3D11,
            RendererNull => RendererNull,
            _ => RendererOpenGl,
        };
        SurfaceScale = Math.Clamp(SurfaceScale, 1, 8);
        Filtering = string.Equals(Filtering, FilterNearest, StringComparison.OrdinalIgnoreCase)
            ? FilterNearest
            : FilterLinear;
        StartupSize = StartupSizes.Contains(StartupSize, StringComparer.Ordinal) ? StartupSize : "1280x960";
        AspectRatio = NullSafe.Text(AspectRatio).ToLowerInvariant() switch
        {
            "native" => "native",
            "4x3" => "4x3",
            "16x9" => "16x9",
            _ => "auto",
        };
        Fit = NullSafe.Text(Fit).ToLowerInvariant() switch
        {
            "center" => "center",
            "stretch" => "stretch",
            _ => "scale",
        };
        AvPack = AvPacks.Contains(AvPack, StringComparer.Ordinal) ? AvPack : "hdtv";
    }

    /// <summary>Gets the startup-size keys xemu accepts.</summary>
    public static IReadOnlyList<string> StartupSizes { get; } =
    [
        "last_used",
        "640x480",
        "720x480",
        "1280x720",
        "1280x800",
        "1280x960",
        "1920x1080",
        "2560x1440",
        "2560x1600",
        "2560x1920",
        "3840x2160",
    ];

    /// <summary>Gets the AV pack keys xemu accepts.</summary>
    public static IReadOnlyList<string> AvPacks { get; } =
    [
        "hdtv",
        "composite",
        "svideo",
        "scart",
        "vga",
        "rfu",
        "none",
    ];
}
