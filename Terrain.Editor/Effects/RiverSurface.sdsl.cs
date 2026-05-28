using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Rendering;
using Stride.Shaders;

namespace Terrain.Editor
{
    public static class RiverSurfaceKeys
    {
        public static readonly ValueParameterKey<float> FlowNormalSpeed = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> FlowNormalUvScale = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> BankFade = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<float> Depth = ParameterKeys.NewValue<float>();
        public static readonly ValueParameterKey<Color4> WaterColorShallow = ParameterKeys.NewValue<Color4>();
        public static readonly ValueParameterKey<Color4> WaterColorDeep = ParameterKeys.NewValue<Color4>();
        public static readonly ValueParameterKey<float> GlobalTime = ParameterKeys.NewValue<float>();
    }
}
