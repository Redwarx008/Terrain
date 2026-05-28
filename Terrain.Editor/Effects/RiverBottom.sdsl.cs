using Stride.Core.Mathematics;
using Stride.Rendering;
using Stride.Shaders;

namespace Terrain.Editor.Effects
{
    public static class RiverBottomKeys
    {
        public static readonly ValueParameterKey<float> Depth = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> DepthFakeFactor = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> BankFade = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<Color4> BottomColor = ParameterKeys.NewValue<Color4>();
    }
}
