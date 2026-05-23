#nullable enable

namespace Terrain.Editor.Models;

/// <summary>
/// 河流地图像素类型，对应 CK3 索引颜色规范。
/// Land=白, River=蓝渐变(宽度编码在蓝通道), Source=绿, Confluence=红, Bifurcation=黄, Ocean=品红
/// </summary>
public enum RiverPixelType : byte
{
    Land = 0,
    River = 1,
    Source = 2,
    Confluence = 3,
    Bifurcation = 4,
    Ocean = 5,
}
