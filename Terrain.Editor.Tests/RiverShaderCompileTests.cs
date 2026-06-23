using System.Reflection;
using Stride.Core.IO;
using Stride.Graphics;
using Stride.Shaders;
using Stride.Shaders.Compiler;

namespace Terrain.Editor.Tests;

internal static class RiverShaderCompileTests
{
    public static void RunAll()
    {
        TestHarness.Run("river bottom shader compiles through stride effect compiler", () => CompileShader("RiverBottom"));
        TestHarness.Run("river surface shader compiles through stride effect compiler", () => CompileShader("RiverSurface"));
        TestHarness.Run("river scene seed shader compiles through stride effect compiler", () => CompileShader("RiverSceneSeed"));
        TestHarness.Run("ocean surface shader compiles through stride effect compiler", () => CompileShader("OceanSurface"));
    }

    private static void CompileShader(string shaderName)
    {
        string repositoryRoot = FindRepositoryRoot();
        string strideSourceRoot = FindStrideSourceRoot();

        using var fileProvider = new FileSystemProvider("/", null);
        using var compiler = new EffectCompiler(fileProvider)
        {
            UseFileSystem = true,
        };

        foreach (string sourceDirectory in EnumerateSourceDirectories(repositoryRoot, strideSourceRoot))
        {
            compiler.SourceDirectories.Add(sourceDirectory);
        }

        var compilerParameters = new CompilerParameters();
        compilerParameters.EffectParameters.Platform = GraphicsPlatform.Direct3D11;
        compilerParameters.EffectParameters.Profile = GraphicsProfile.Level_11_0;

        CompilerResults compilerResults = compiler.Compile(new ShaderClassSource(shaderName), compilerParameters);
        TestHarness.Assert(!compilerResults.HasErrors, $"{shaderName} should compile without front-end errors.{Environment.NewLine}{compilerResults.ToText()}");

        EffectBytecodeCompilerResult bytecodeResult = compilerResults.Bytecode.WaitForResult();
        TestHarness.Assert(!bytecodeResult.CompilationLog.HasErrors, $"{shaderName} should compile without backend errors.{Environment.NewLine}{bytecodeResult.CompilationLog.ToText()}");
        TestHarness.Assert(bytecodeResult.Bytecode != null, $"{shaderName} should produce bytecode");
    }

    private static IEnumerable<string> EnumerateSourceDirectories(string repositoryRoot, string strideSourceRoot)
    {
        yield return Path.Combine(repositoryRoot, "Terrain", "Effects", "River");
        yield return Path.Combine(repositoryRoot, "Terrain", "Effects", "Ocean");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Graphics", "Shaders");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Shaders");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Core");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Images");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Lights");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Shadows");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Materials", "Shaders");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Materials", "ComputeColors", "Shaders");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Skinning");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Shading");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Transformation");
        yield return Path.Combine(strideSourceRoot, "sources", "engine", "Stride.Rendering", "Rendering", "Utils");
    }

    private static string FindRepositoryRoot()
    {
        string? current = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "Terrain", "Effects", "River")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test assembly location.");
    }

    private static string FindStrideSourceRoot()
    {
        string[] candidates =
        [
            Environment.GetEnvironmentVariable("STRIDE_ENGINE_SOURCE") ?? string.Empty,
            @"E:\WorkSpace\stride",
        ];

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (Directory.Exists(Path.Combine(candidate, "sources", "engine", "Stride.Graphics", "Shaders")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not locate Stride engine source root. Set STRIDE_ENGINE_SOURCE or restore the default E:\\WorkSpace\\stride checkout.");
    }
}
