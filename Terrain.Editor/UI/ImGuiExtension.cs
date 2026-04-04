using Hexa.NET.ImGui;
using Stride.Graphics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Terrain.Editor.UI;

/// <summary>
/// ImGui 扩展方法，提供与 Stride 纹理的集成
/// </summary>
public static class ImGuiExtension
{
    // 纹理注册表
    private static readonly List<Texture> _textureRegistry = [];

    /// <summary>
    /// 获取纹理的 ImTextureRef 键，并将其添加到注册表
    /// </summary>
    internal static ImTextureRef GetTextureKey(Texture texture)
    {
        _textureRegistry.Add(texture);
        ulong id = (ulong)_textureRegistry.Count;
        return new ImTextureRef { TexID = (ImTextureID)(nint)id };
    }

    /// <summary>
    /// 尝试从键获取纹理
    /// </summary>
    internal static bool TryGetTexture(ulong key, out Texture? texture)
    {
        int index = (int)key - 1;
        if (index >= 0 && index < _textureRegistry.Count)
        {
            texture = _textureRegistry[index];
            return true;
        }
        texture = null;
        return false;
    }

    /// <summary>
    /// 清除纹理注册表
    /// </summary>
    internal static void ClearTextures()
    {
        _textureRegistry.Clear();
    }

    /// <summary>
    /// 显示纹理图像
    /// </summary>
    public static void Image(Texture texture)
    {
        Hexa.NET.ImGui.ImGui.Image(GetTextureKey(texture), new Vector2(texture.Width, texture.Height));
    }

    /// <summary>
    /// 显示指定大小的纹理图像
    /// </summary>
    public static void Image(Texture texture, int width, int height)
    {
        Hexa.NET.ImGui.ImGui.Image(GetTextureKey(texture), new Vector2(width, height));
    }

    /// <summary>
    /// 显示指定大小的纹理图像
    /// </summary>
    public static void Image(Texture texture, Vector2 size)
    {
        Hexa.NET.ImGui.ImGui.Image(GetTextureKey(texture), size);
    }

    /// <summary>
    /// 显示可点击的纹理图像按钮
    /// </summary>
    public static bool ImageButton(string strId, Texture texture)
    {
        return Hexa.NET.ImGui.ImGui.ImageButton(strId, GetTextureKey(texture), new Vector2(texture.Width, texture.Height));
    }

    /// <summary>
    /// 显示指定大小的可点击纹理图像按钮
    /// </summary>
    public static bool ImageButton(string strId, Texture texture, int width, int height)
    {
        return Hexa.NET.ImGui.ImGui.ImageButton(strId, GetTextureKey(texture), new Vector2(width, height));
    }

    /// <summary>
    /// 显示指定大小的可点击纹理图像按钮
    /// </summary>
    public static bool ImageButton(string strId, Texture texture, Vector2 size)
    {
        return Hexa.NET.ImGui.ImGui.ImageButton(strId, GetTextureKey(texture), size);
    }
}
