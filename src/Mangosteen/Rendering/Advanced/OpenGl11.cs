using System.Runtime.InteropServices;

namespace Mangosteen.Rendering.Advanced;

internal static partial class OpenGl11
{
    public const uint ColorBufferBit = 0x00004000;
    public const uint DepthBufferBit = 0x00000100;
    public const uint DepthTest = 0x0B71;
    public const uint ScissorTest = 0x0C11;
    public const uint Texture2D = 0x0DE1;
    public const uint Quads = 0x0007;
    public const uint Projection = 0x1701;
    public const uint ModelView = 0x1700;
    public const uint Rgba = 0x1908;
    public const uint Rgba8 = 0x8058;
    public const uint Rgba16 = 0x805B;
    public const uint UnsignedByte = 0x1401;
    public const uint UnsignedShort = 0x1403;
    public const uint TextureMinFilter = 0x2801;
    public const uint TextureMagFilter = 0x2800;
    public const uint TextureWrapS = 0x2802;
    public const uint TextureWrapT = 0x2803;
    public const int Linear = 0x2601;
    public const int Nearest = 0x2600;
    public const int ClampToEdge = 0x812F;
    public const uint Blend = 0x0BE2;
    public const uint SrcAlpha = 0x0302;
    public const uint OneMinusSrcAlpha = 0x0303;

    [LibraryImport("opengl32.dll", EntryPoint = "glGetError")]
    public static partial uint GetError();

    [LibraryImport("opengl32.dll", EntryPoint = "glViewport")]
    public static partial void Viewport(int x, int y, int width, int height);

    [LibraryImport("opengl32.dll", EntryPoint = "glClearColor")]
    public static partial void ClearColor(float red, float green, float blue, float alpha);

    [LibraryImport("opengl32.dll", EntryPoint = "glClear")]
    public static partial void Clear(uint mask);

    [LibraryImport("opengl32.dll", EntryPoint = "glDepthMask")]
    public static partial void DepthMask(byte enabled);

    [LibraryImport("opengl32.dll", EntryPoint = "glClearDepth")]
    public static partial void ClearDepth(double depth);

    [LibraryImport("opengl32.dll", EntryPoint = "glEnable")]
    public static partial void Enable(uint capability);

    [LibraryImport("opengl32.dll", EntryPoint = "glDisable")]
    public static partial void Disable(uint capability);

    [LibraryImport("opengl32.dll", EntryPoint = "glBlendFunc")]
    public static partial void BlendFunc(uint source, uint destination);

    [LibraryImport("opengl32.dll", EntryPoint = "glMatrixMode")]
    public static partial void MatrixMode(uint mode);

    [LibraryImport("opengl32.dll", EntryPoint = "glLoadIdentity")]
    public static partial void LoadIdentity();

    [LibraryImport("opengl32.dll", EntryPoint = "glOrtho")]
    public static partial void Ortho(double left, double right, double bottom, double top, double nearValue, double farValue);

    [LibraryImport("opengl32.dll", EntryPoint = "glGenTextures")]
    public static partial void GenTextures(int count, out uint textures);

    [LibraryImport("opengl32.dll", EntryPoint = "glDeleteTextures")]
    public static partial void DeleteTextures(int count, ref uint textures);

    [LibraryImport("opengl32.dll", EntryPoint = "glBindTexture")]
    public static partial void BindTexture(uint target, uint texture);

    [LibraryImport("opengl32.dll", EntryPoint = "glTexParameteri")]
    public static partial void TexParameteri(uint target, uint parameter, int value);

    [LibraryImport("opengl32.dll", EntryPoint = "glTexImage2D")]
    public static partial void TexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, nint pixels);

    [LibraryImport("opengl32.dll", EntryPoint = "glBegin")]
    public static partial void Begin(uint mode);

    [LibraryImport("opengl32.dll", EntryPoint = "glEnd")]
    public static partial void End();

    [LibraryImport("opengl32.dll", EntryPoint = "glTexCoord2f")]
    public static partial void TexCoord2(float s, float t);

    [LibraryImport("opengl32.dll", EntryPoint = "glVertex2f")]
    public static partial void Vertex2(float x, float y);

    [LibraryImport("opengl32.dll", EntryPoint = "glColor4f")]
    public static partial void Color4(float red, float green, float blue, float alpha);
}
