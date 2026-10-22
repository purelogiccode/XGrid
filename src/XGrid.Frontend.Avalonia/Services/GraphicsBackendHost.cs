using System;
using XGrid.Frontend.Avalonia.Models;
using XGrid.GpuGl;
using XGrid.Host;
using XGrid.Nv2a;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Attaches the host OpenGL backend to a machine the frontend owns, and
/// detaches it again on stop.
/// </summary>
/// <remarks>
/// <para>
/// The frontend displays <see cref="Nv2AMmio.TryCopyDisplayFrame"/>, which
/// delegates to the backend readback whenever a backend is attached, so the
/// frame pipeline is unchanged: the same <c>FramePacket</c> seam carries either
/// renderer's pixels and the two are never mixed. Nothing here embeds a foreign
/// window in the Avalonia visual tree; the GL context is an offscreen
/// hidden-window context owned by the emulation thread.
/// </para>
/// <para>
/// Replacement mode is deliberate: the swap is meant to remove the software
/// rasterizer's share of the frame, and §219b made a declined draw compose
/// correctly (the flip re-uploads the whole presented surface from guest RAM).
/// The per-flip readback is on because the UI consumes pixels, not an FBO.
/// </para>
/// <para>
/// GL is created on the calling thread and must be disposed on that same
/// thread: call both from the emulation thread (the session does).
/// </para>
/// </remarks>
public static class GraphicsBackendHost
{
    /// <summary>
    /// Attaches the renderer the settings ask for. Never throws: a refused or
    /// failed request leaves the machine on the software rasterizer and reports
    /// why through <paramref name="detail"/>.
    /// </summary>
    /// <param name="machine">Machine to attach to (must have a GPU memory adapter).</param>
    /// <param name="settings">Settings snapshot of the boot.</param>
    /// <param name="detail">One-line description of the renderer in use.</param>
    /// <returns>True when the OpenGL backend is attached and rendering.</returns>
    public static bool TryAttach(XboxMachine machine, EmulatorSettings settings, out string detail)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(settings);

        switch (RendererPolicy.Decide(settings.Renderer, OperatingSystem.IsWindows()))
        {
            case RendererChoice.Software:
                detail = RendererPolicy.Describe(RendererPolicy.Software);
                return false;

            case RendererChoice.OpenGlUnavailable:
                detail = RendererPolicy.Describe(RendererPolicy.Software)
                    + " (the OpenGL backend needs a Windows host; the request was ignored)";
                return false;

        }

        IGpuMemory? memory = machine.Nv2A.GpuMemory;
        if (memory is null)
        {
            detail = RendererPolicy.Describe(RendererPolicy.Software)
                + " (this machine has no GPU memory adapter)";
            return false;
        }

        if (!GlBackendFactory.TryCreate(
                memory,
                windowEnabled: false,
                replaceSoftware: true,
                frameReadback: true,
                out IPgraphBackend? backend,
                out string why) ||
            backend is null)
        {
            detail = RendererPolicy.Describe(RendererPolicy.Software) + " (OpenGL probe failed: " + why + ")";
            return false;
        }

        machine.Nv2A.Backend = backend;
        detail = "OpenGL backend (replace=on, readback=on) - " + why;
        return true;
    }

    /// <summary>
    /// Detaches and disposes the attached backend, if any, leaving the machine
    /// on the software path. Best-effort: a stop must never fail on GL teardown.
    /// </summary>
    /// <param name="machine">Machine whose backend should be released (may be null).</param>
    public static void Detach(XboxMachine? machine)
    {
        IPgraphBackend? backend = machine?.Nv2A.Backend;
        if (backend is null || machine is null)
        {
            return;
        }

        machine.Nv2A.Backend = null;
        if (backend is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
