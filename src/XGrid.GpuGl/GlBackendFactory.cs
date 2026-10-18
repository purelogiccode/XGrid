using System;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using XGrid.Nv2a;

namespace XGrid.GpuGl;

/// <summary>
/// Builds the slice-1 GL backend: hidden-window WGL context, probe
/// self-test (gl/probe.c pattern), then the backend instance. Any failure
/// returns false with a why string; the CLI then falls back to software for
/// the session (Backend stays null).
/// </summary>
public static class GlBackendFactory
{
    /// <summary>Creates the GL backend, running the probe self-test at init.</summary>
    public static bool TryCreate(IGpuMemory memory, out IPgraphBackend? backend, out string why)
    {
        return TryCreate(memory, windowEnabled: false, out backend, out why);
    }

    /// <summary>Creates the GL backend with the opt-in live window (<c>--window</c>).</summary>
    public static bool TryCreate(IGpuMemory memory, bool windowEnabled, out IPgraphBackend? backend, out string why)
    {
        ArgumentNullException.ThrowIfNull(memory);
        backend = null;
        if (!WglContext.TryCreate(out WglContext? context, out why) || context is null)
        {
            return false;
        }

        IDisposable? glContext = null;
        try
        {
            INativeContext created = new LamdaNativeContext(context.GetProcAddress);
            glContext = created;
            var gl = new GL(created);
            if (!GlProbe.TryRun(gl, out why))
            {
                glContext.Dispose();
                context.Dispose();
                return false;
            }

            backend = new GlPgraphBackend(context, created, gl, memory, windowEnabled);
            unsafe
            {
                byte* version = gl.GetString(StringName.Version);
                string? versionText = version is null
                    ? null
                    : System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)version);
                why = $"probe ok; GL_VERSION={versionText ?? "unknown"}";
            }

            return true;
        }
        catch (Exception exception)
        {
            why = exception.GetType().Name + ": " + exception.Message;
            glContext?.Dispose();
            context.Dispose();
            return false;
        }
    }
}
