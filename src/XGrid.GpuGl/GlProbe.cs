using System;
using Silk.NET.OpenGL;

namespace XGrid.GpuGl;

/// <summary>
/// One-time behavioral self-test at backend init (the gl/probe.c pattern —
/// structure and ±2 tolerance facts taken from
/// References2/xemu-Playtest-v2/hw/xbox/nv2a/pgraph/gl/probe.c; GPL/LGPL
/// code is NOT copied — this is an independent minimal implementation):
/// compile a tiny program, create an FBO and require completeness, draw a
/// known-texel quad and read the pixel back within ±2. Any GL error or a
/// wrong pixel fails the probe with a why string; the caller then falls
/// back to software for the session.
/// </summary>
internal static class GlProbe
{
    private const string VertexSource =
        "#version 330 core\n" +
        "void main() {\n" +
        "  vec2 p = vec2((gl_VertexID == 1) ? 3.0 : -1.0,\n" +
        "                (gl_VertexID == 2) ? 3.0 : -1.0);\n" +
        "  gl_Position = vec4(p, 0.0, 1.0);\n" +
        "}\n";

    private const string FragmentSource =
        "#version 330 core\n" +
        "uniform sampler2D uTex;\n" +
        "out vec4 fragColor;\n" +
        "void main() {\n" +
        "  fragColor = texture(uTex, vec2(0.5));\n" +
        "}\n";

    public static bool TryRun(GL gl, out string why)
    {
        why = "ok";
        while (gl.GetError() != GLEnum.NoError)
        {
        }

        byte[] texel = { 64, 128, 192, 255 };
        uint program = 0;
        uint vertexShader = 0;
        uint fragmentShader = 0;
        uint fbo = 0;
        uint texture = 0;
        uint target = 0;
        uint vao = 0;
        try
        {
            program = gl.CreateProgram();
            vertexShader = Compile(gl, ShaderType.VertexShader, VertexSource, "vertex", ref why);
            if (vertexShader == 0)
            {
                return false;
            }

            fragmentShader = Compile(gl, ShaderType.FragmentShader, FragmentSource, "fragment", ref why);
            if (fragmentShader == 0)
            {
                return false;
            }

            gl.AttachShader(program, vertexShader);
            gl.AttachShader(program, fragmentShader);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
            {
                why = "probe link: " + gl.GetProgramInfoLog(program).Replace('\n', ' ');
                return false;
            }

            if (CheckError(gl, "probe link", ref why))
            {
                return false;
            }

            // Two textures: sampling the texture attached to the currently
            // bound FBO is a feedback loop (undefined; renders black).
            gl.GenTextures(1, out texture);
            gl.GenTextures(1, out target);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            unsafe
            {
                fixed (byte* pTexel = texel)
                {
                    // 1x1 source: the whole sampled texture is the known texel.
                    gl.TexImage2D(
                        TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                        1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pTexel);
                }
            }

            // 2x2 render target with storage (sampling it would be a
            // feedback loop, so it is only ever attached, never sampled).
            byte[] blank = new byte[16];
            gl.BindTexture(TextureTarget.Texture2D, target);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            unsafe
            {
                fixed (byte* pBlank = blank)
                {
                    gl.TexImage2D(
                        TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                        2, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pBlank);
                }
            }

            gl.GenFramebuffers(1, out fbo);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, target, 0);
            if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            {
                why = "probe framebuffers incomplete";
                return false;
            }

            gl.GenVertexArrays(1, out vao);
            gl.BindVertexArray(vao);
            gl.Viewport(0, 0, 2, 2);
            gl.Disable(EnableCap.ScissorTest);
            gl.Disable(EnableCap.Blend);
            gl.Disable(EnableCap.DepthTest);
            gl.Disable(EnableCap.StencilTest);
            gl.Disable(EnableCap.CullFace);
            gl.ColorMask(true, true, true, true);
            gl.UseProgram(program);
            gl.Uniform1(gl.GetUniformLocation(program, "uTex"), 0);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, texture);
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Clear(ClearBufferMask.ColorBufferBit);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            if (CheckError(gl, "probe draw", ref why))
            {
                return false;
            }

            byte[] pixel = new byte[4];
            unsafe
            {
                fixed (byte* pPixel = pixel)
                {
                    gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pPixel);
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(pixel[i] - texel[i]) > 2)
                {
                    why = FormattableString.Invariant(
                        $"probe draw gave {pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]} (expected {texel[0]},{texel[1]},{texel[2]},{texel[3]} within 2)");
                    return false;
                }
            }

            return !CheckError(gl, "probe readback", ref why);
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.BindVertexArray(0);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.UseProgram(0);
            if (fbo != 0)
            {
                gl.DeleteFramebuffers(1, ref fbo);
            }

            if (vao != 0)
            {
                gl.DeleteVertexArrays(1, ref vao);
            }

            if (texture != 0)
            {
                gl.DeleteTextures(1, ref texture);
            }

            if (target != 0)
            {
                gl.DeleteTextures(1, ref target);
            }

            if (program != 0)
            {
                gl.DeleteProgram(program);
            }

            if (vertexShader != 0)
            {
                gl.DeleteShader(vertexShader);
            }

            if (fragmentShader != 0)
            {
                gl.DeleteShader(fragmentShader);
            }

            while (gl.GetError() != GLEnum.NoError)
            {
            }
        }
    }

    private static uint Compile(GL gl, ShaderType type, string source, string label, ref string why)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            why = $"probe {label} compile: {gl.GetShaderInfoLog(shader).Replace('\n', ' ')}";
            gl.DeleteShader(shader);
            return 0;
        }

        return shader;
    }

    private static bool CheckError(GL gl, string step, ref string why)
    {
        GLEnum error = gl.GetError();
        if (error == GLEnum.NoError)
        {
            return false;
        }

        why = $"{step}: GL error 0x{(uint)error:X}";
        return true;
    }
}
