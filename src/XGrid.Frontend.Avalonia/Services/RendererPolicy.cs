using System;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Which NV2A renderer a session should run.</summary>
public enum RendererChoice
{
    /// <summary>The portable software rasterizer (the default and the gate currency).</summary>
    Software,

    /// <summary>The host OpenGL backend.</summary>
    OpenGl,

    /// <summary>OpenGL was requested but this platform cannot provide it.</summary>
    OpenGlUnavailable,
}

/// <summary>
/// Decides which NV2A renderer a session uses. The OpenGL backend presents
/// through a WGL context, so the choice is a platform question as well as a
/// user preference: the software rasterizer stays the default, and a request
/// the platform cannot honor degrades to it instead of failing the boot.
/// </summary>
/// <remarks>
/// This is the pure policy half of the renderer switch; <see cref="GraphicsBackendHost"/>
/// performs the side effects (context creation, backend attach). Keeping the
/// decision free of GL calls is what makes it testable off Windows.
/// </remarks>
public static class RendererPolicy
{
    /// <summary>Persisted key of the software rasterizer.</summary>
    public const string Software = "software";

    /// <summary>Persisted key of the host OpenGL backend.</summary>
    public const string OpenGl = "gl";

    /// <summary>Returns true when the persisted value selects the OpenGL backend.</summary>
    /// <param name="renderer">Persisted renderer key.</param>
    public static bool IsOpenGl(string? renderer)
    {
        return string.Equals(renderer, OpenGl, StringComparison.Ordinal);
    }

    /// <summary>Repairs a persisted renderer key to one of the two known values.</summary>
    /// <param name="renderer">Persisted renderer key (may be null or unknown).</param>
    public static string Normalize(string? renderer)
    {
        return IsOpenGl(renderer) ? OpenGl : Software;
    }

    /// <summary>Returns the user-facing label of a renderer key.</summary>
    /// <param name="renderer">Persisted renderer key.</param>
    public static string Describe(string? renderer)
    {
        return IsOpenGl(renderer) ? "OpenGL backend" : "Software rasterizer";
    }

    /// <summary>
    /// Resolves the renderer for one boot: a request for OpenGL on a platform
    /// that cannot create a WGL context becomes
    /// <see cref="RendererChoice.OpenGlUnavailable"/> so the caller can say why.
    /// </summary>
    /// <param name="renderer">Persisted renderer key.</param>
    /// <param name="platformSupportsOpenGl">True on a Windows host.</param>
    public static RendererChoice Decide(string? renderer, bool platformSupportsOpenGl)
    {
        if (!IsOpenGl(renderer))
        {
            return RendererChoice.Software;
        }

        return platformSupportsOpenGl ? RendererChoice.OpenGl : RendererChoice.OpenGlUnavailable;
    }
}
