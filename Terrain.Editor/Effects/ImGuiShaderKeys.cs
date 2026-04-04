// Manual shader keys for ImGuiShader until auto-generated
using Stride.Rendering;
using Stride.Graphics;
using Stride.Core.Mathematics;

namespace Terrain.Editor
{
    public static class ImGuiShaderKeys
    {
        public static readonly ValueParameterKey<Matrix> proj = ParameterKeys.NewValue<Matrix>();
        public static readonly ObjectParameterKey<Texture> tex = ParameterKeys.NewObject<Texture>();
        public static readonly ObjectParameterKey<SamplerState> TexSampler = ParameterKeys.NewObject<SamplerState>();
    }
}
