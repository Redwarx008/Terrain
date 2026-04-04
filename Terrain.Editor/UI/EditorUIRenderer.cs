#nullable enable

using Hexa.NET.ImGui;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Rendering;
using System;
using Terrain.Editor.UI.Styling;
using Stride.Core;
using Stride.Input;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;
using Stride.Core.Mathematics;
using StrideBuffer = Stride.Graphics.Buffer;
using Terrain.Editor.Platform;
using Terrain.Editor;

namespace Terrain.Editor.UI;

/// <summary>
/// 编辑器UI渲染器 - 管理ImGui的初始化和渲染
/// </summary>
public class EditorUIRenderer : GameSystemBase
{
    public readonly ImGuiContextPtr ImGuiContext;

    private ImGuiIOPtr io;
    private ImGuiPlatformIOPtr platform;
    private float scale = 1.0f;
    private bool isFirstFrame = true;

    // 依赖项
    readonly InputManager? input;
    readonly GraphicsDevice device;
    readonly GraphicsDeviceManager deviceManager;
    readonly GraphicsContext context;
    readonly EffectSystem effectSystem;
    CommandList commandList = null!;

    // 设备资源
    PipelineState? alphaPipeline;
    PipelineState? opaquePipeline;
    VertexDeclaration? vertexLayout;
    VertexBufferBinding vertexBinding;
    IndexBufferBinding indexBinding;
    EffectInstance? shader;
    bool frameBegun;

    // ImGui 管理的纹理 (字体图集等)
    private readonly Dictionary<ImTextureID, Texture> managedTextures = new();

    // 输入映射
    private Dictionary<Keys, ImGuiKey> keyMap = new();

    public Action? OnRender { get; set; }

    public float Scale
    {
        get => scale;
        set
        {
            if (Math.Abs(scale - value) < 0.001f)
                return;

            scale = value;
            FontManager.UpdateScale(scale);
            EditorStyle.Apply(scale);
            RebuildFonts();
        }
    }

    public EditorUIRenderer(Game game) : base(game.Services)
    {
        deviceManager = game.Services.GetService<IGraphicsDeviceManager>() as GraphicsDeviceManager
            ?? throw new InvalidOperationException("GraphicsDeviceManager not found");

        device = deviceManager.GraphicsDevice;
        context = game.Services.GetService<GraphicsContext>()
            ?? throw new InvalidOperationException("GraphicsContext not found");
        effectSystem = game.Services.GetService<EffectSystem>()
            ?? throw new InvalidOperationException("EffectSystem not found");
        input = game.Services.GetService<InputManager>();

        // 创建ImGui上下文
        ImGuiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(ImGuiContext);

        io = ImGui.GetIO();
        platform = ImGui.GetPlatformIO();

        // 设置输入
        SetupInput();

        // 创建设备资源
        CreateDeviceObjects();

        // 设置字体
        SetupFonts();

        // 强制启用更新和绘制
        Enabled = true;
        Visible = true;
        UpdateOrder = 1; // 在InputManager之后更新

        // 添加到游戏系统
        Game.GameSystems.Add(this);
    }

    private void SetupInput()
    {
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // 告诉 ImGui 渲染器支持动态纹理创建
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        platform.RendererTextureMaxWidth = 4096;
        platform.RendererTextureMaxHeight = 4096;

        // 键盘映射
        keyMap[Keys.Tab] = ImGuiKey.Tab;
        keyMap[Keys.Left] = ImGuiKey.LeftArrow;
        keyMap[Keys.Right] = ImGuiKey.RightArrow;
        keyMap[Keys.Up] = ImGuiKey.UpArrow;
        keyMap[Keys.Down] = ImGuiKey.DownArrow;
        keyMap[Keys.PageUp] = ImGuiKey.PageUp;
        keyMap[Keys.PageDown] = ImGuiKey.PageDown;
        keyMap[Keys.Home] = ImGuiKey.Home;
        keyMap[Keys.End] = ImGuiKey.End;
        keyMap[Keys.Delete] = ImGuiKey.Delete;
        keyMap[Keys.Back] = ImGuiKey.Backspace;
        keyMap[Keys.Enter] = ImGuiKey.Enter;
        keyMap[Keys.Escape] = ImGuiKey.Escape;
        keyMap[Keys.Space] = ImGuiKey.Space;
        keyMap[Keys.A] = ImGuiKey.A;
        keyMap[Keys.C] = ImGuiKey.C;
        keyMap[Keys.V] = ImGuiKey.V;
        keyMap[Keys.X] = ImGuiKey.X;
        keyMap[Keys.Y] = ImGuiKey.Y;
        keyMap[Keys.Z] = ImGuiKey.Z;
    }

    private unsafe void SetupFonts()
    {
        ImGui.SetCurrentContext(ImGuiContext);

        // 添加默认字体（使用系统字体，带缩放）
        float scaledFontSize = FontManager.RegularSize * scale;
        string fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

        // 尝试加载 Segoe UI 作为默认字体
        string defaultFontPath = Path.Combine(fontsDirectory, "segoeui.ttf");
        ImFontPtr defaultFont;
        if (File.Exists(defaultFontPath))
        {
            defaultFont = io.Fonts.AddFontFromFileTTF(defaultFontPath, scaledFontSize);
        }
        else
        {
            // 回退到 ImGui 默认字体
            defaultFont = io.Fonts.AddFontDefault();
        }
        FontManager.Initialize(defaultFont);

        // 添加图标字体
        string iconFontPath = Path.Combine(fontsDirectory, "segmdl2.ttf");
        if (!File.Exists(iconFontPath))
        {
            iconFontPath = Path.Combine(fontsDirectory, "SegoeIcons.ttf");
        }
        if (File.Exists(iconFontPath))
        {
            var iconFont = io.Fonts.AddFontFromFileTTF(
                iconFontPath,
                FontManager.IconSize * scale,
                FontManager.GetIconGlyphRanges());
            FontManager.SetIconFont(iconFont);
        }
    }

    private unsafe void RebuildFonts()
    {
        ImGui.SetCurrentContext(ImGuiContext);
        io.Fonts.Clear();

        SetupFonts();

        // 清理旧的托管纹理
        foreach (var texture in managedTextures.Values)
            texture.Dispose();
        managedTextures.Clear();
    }

    private void CreateDeviceObjects()
    {
        commandList = context.CommandList;

        // 加载着色器
        shader = new EffectInstance(effectSystem.LoadEffect("ImGuiShader").WaitForResult());
        shader.UpdateEffect(device);

        // 顶点布局
        vertexLayout = new VertexDeclaration(
            VertexElement.Position<Vector2>(),
            VertexElement.TextureCoordinate<Vector2>(),
            VertexElement.Color(PixelFormat.R8G8B8A8_UNorm)
        );

        // 管线状态
        var pipelineDesc = new PipelineStateDescription()
        {
            BlendState = BlendStates.NonPremultiplied,
            RasterizerState = new RasterizerStateDescription()
            {
                CullMode = CullMode.None,
                DepthBias = 0,
                FillMode = FillMode.Solid,
                MultisampleAntiAliasLine = false,
                ScissorTestEnable = true,
                SlopeScaleDepthBias = 0,
            },
            PrimitiveType = PrimitiveType.TriangleList,
            InputElements = vertexLayout.CreateInputElements(),
            DepthStencilState = DepthStencilStates.Default,
            EffectBytecode = shader.Effect.Bytecode,
            RootSignature = shader.RootSignature,
            Output = new RenderOutputDescription(PixelFormat.R8G8B8A8_UNorm)
        };

        alphaPipeline = PipelineState.New(device, pipelineDesc);

        pipelineDesc.BlendState = BlendStates.Opaque;
        opaquePipeline = PipelineState.New(device, pipelineDesc);

        // 创建缓冲区
        const int initialVertexCount = 128;
        const int initialIndexCount = 128;

        var indexBuffer = StrideBuffer.Index.New(device, initialIndexCount * sizeof(ushort), GraphicsResourceUsage.Dynamic);
        indexBinding = new IndexBufferBinding(indexBuffer, false, 0);

        var vertexBuffer = StrideBuffer.Vertex.New(device, initialVertexCount * vertexLayout.CalculateSize(), GraphicsResourceUsage.Dynamic);
        vertexBinding = new VertexBufferBinding(vertexBuffer, vertexLayout, 0);
    }

    public override void Update(GameTime gameTime)
    {
        ImGui.SetCurrentContext(ImGuiContext);

        var deltaTime = (float)gameTime.Elapsed.TotalSeconds;
        if (isFirstFrame)
        {
            isFirstFrame = false;
            deltaTime = 1f / 60f;
        }

        // 更新显示尺寸
        var surfaceSize = Game.Window.ClientBounds;
        if (Game.Window.IsMinimized || surfaceSize.Width <= 1 || surfaceSize.Height <= 1)
        {
            io.DisplaySize = Vector2.Zero;
            frameBegun = false;
            input?.TextInput.DisableTextInput();
            return;
        }

        Scale = GetUiScale();
        io.DisplaySize = new System.Numerics.Vector2(surfaceSize.Width, surfaceSize.Height);
        io.DeltaTime = deltaTime;

        // 处理输入
        if (input != null && input.HasMouse && !input.IsMousePositionLocked)
        {
            var mousePos = input.AbsoluteMousePosition;
            io.AddMousePosEvent(mousePos.X, mousePos.Y);

            if (io.WantTextInput)
                input.TextInput.EnabledTextInput();
            else
                input.TextInput.DisableTextInput();

            // 处理输入事件
            foreach (var ev in input.Events)
            {
                switch (ev)
                {
                    case TextInputEvent tev:
                        if (tev.Text == "\t") continue;
                        io.AddInputCharactersUTF8(tev.Text);
                        break;
                    case KeyEvent kev:
                        if (keyMap.TryGetValue(kev.Key, out var imGuiKey))
                            io.AddKeyEvent(imGuiKey, input.IsKeyDown(kev.Key));
                        break;
                    case MouseWheelEvent mw:
                        io.AddMouseWheelEvent(0, mw.WheelDelta);
                        break;
                }
            }

            io.AddMouseButtonEvent(0, input.IsMouseButtonDown(MouseButton.Left));
            io.AddMouseButtonEvent(1, input.IsMouseButtonDown(MouseButton.Right));
            io.AddMouseButtonEvent(2, input.IsMouseButtonDown(MouseButton.Middle));

            io.AddKeyEvent(ImGuiKey.ModAlt, input.IsKeyDown(Keys.LeftAlt) || input.IsKeyDown(Keys.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModShift, input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModCtrl, input.IsKeyDown(Keys.LeftCtrl) || input.IsKeyDown(Keys.RightCtrl));
            io.AddKeyEvent(ImGuiKey.ModSuper, input.IsKeyDown(Keys.LeftWin) || input.IsKeyDown(Keys.RightWin));
        }

        // 开始新帧
        ImGui.NewFrame();
        frameBegun = true;

        // 调用自定义渲染回调
        OnRender?.Invoke();
    }

    private float GetUiScale()
    {
        nint hwnd = Game.Window.NativeWindow?.Handle ?? nint.Zero;
        float dpiScale = WindowInterop.GetWindowScaleFactor(hwnd);
        return MathF.Round(dpiScale * 20.0f) / 20.0f;
    }

    public override void EndDraw()
    {
        if (!frameBegun)
            return;

        ImGui.SetCurrentContext(ImGuiContext);
        ImGui.Render();
        var drawData = ImGui.GetDrawData();

        // 处理纹理更新
        ProcessTextureUpdates(drawData);

        RenderDrawData(drawData);
        frameBegun = false;

        // 清理用户纹理缓存
        ImGuiExtension.ClearTextures();
    }

    /// <summary>
    /// 处理 ImGui 纹理更新请求
    /// </summary>
    private unsafe void ProcessTextureUpdates(ImDrawDataPtr drawData)
    {
        if (drawData.Handle->Textures == null) return;

        var textures = drawData.Textures;
        for (int i = 0; i < textures.Size; i++)
        {
            ImTextureDataPtr textureData = textures.Data[i];
            switch (textureData.Status)
            {
                case ImTextureStatus.WantCreate:
                    CreateManagedTexture(textureData);
                    break;
                case ImTextureStatus.WantUpdates:
                    UpdateManagedTexture(textureData);
                    break;
                case ImTextureStatus.WantDestroy:
                    DestroyManagedTexture(textureData);
                    break;
            }
        }
    }

    private unsafe void CreateManagedTexture(ImTextureDataPtr textureData)
    {
        var pixelFormat = textureData.Format == ImTextureFormat.Rgba32
            ? PixelFormat.R8G8B8A8_UNorm
            : PixelFormat.R8_UNorm;

        var newTexture = Texture.New2D(device, textureData.Width, textureData.Height, pixelFormat, TextureFlags.ShaderResource);
        newTexture.SetData(commandList, new DataPointer((nint)textureData.Pixels, textureData.GetSizeInBytes()));

        var managedId = textureData.GetTexID();
        textureData.SetTexID(managedId);
        managedTextures[managedId] = newTexture;
        textureData.SetStatus(ImTextureStatus.Ok);
    }

    private unsafe void UpdateManagedTexture(ImTextureDataPtr textureData)
    {
        var texId = textureData.GetTexID();
        if (managedTextures.TryGetValue(texId, out var existing))
        {
            var pixelFormat = textureData.Format == ImTextureFormat.Rgba32
                ? PixelFormat.R8G8B8A8_UNorm
                : PixelFormat.R8_UNorm;

            if (existing.Width != textureData.Width || existing.Height != textureData.Height)
            {
                existing.Dispose();
                var newTexture = Texture.New2D(device, textureData.Width, textureData.Height, pixelFormat, TextureFlags.ShaderResource);
                newTexture.SetData(commandList, new DataPointer((nint)textureData.Pixels, textureData.GetSizeInBytes()));
                managedTextures[texId] = newTexture;
            }
            else
            {
                existing.SetData(commandList, new DataPointer((nint)textureData.Pixels, textureData.GetSizeInBytes()));
            }
        }
        textureData.SetStatus(ImTextureStatus.Ok);
    }

    private void DestroyManagedTexture(ImTextureDataPtr textureData)
    {
        var texId = textureData.GetTexID();
        if (managedTextures.TryGetValue(texId, out var texture))
        {
            texture.Dispose();
            managedTextures.Remove(texId);
        }
        textureData.SetStatus(ImTextureStatus.Ok);
    }

    /// <summary>
    /// 在窗口最小化或恢复冷却期内主动终止当前 ImGui 帧
    /// </summary>
    public void SuspendFrame()
    {
        ImGui.SetCurrentContext(ImGuiContext);
        io.DisplaySize = Vector2.Zero;
        input?.TextInput.DisableTextInput();

        if (!frameBegun)
            return;

        ImGui.EndFrame();
        frameBegun = false;
    }

    private void CheckBuffers(ImDrawDataPtr drawData)
    {
        uint totalVtxSize = (uint)(drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
        if (totalVtxSize > vertexBinding.Buffer.SizeInBytes)
        {
            var newBuffer = StrideBuffer.Vertex.New(device, (int)(totalVtxSize * 1.5f));
            vertexBinding = new VertexBufferBinding(newBuffer, vertexLayout!, 0);
        }

        uint totalIdxSize = (uint)(drawData.TotalIdxCount * sizeof(ushort));
        if (totalIdxSize > indexBinding.Buffer.SizeInBytes)
        {
            var newBuffer = StrideBuffer.Index.New(device, (int)(totalIdxSize * 1.5f));
            indexBinding = new IndexBufferBinding(newBuffer, false, 0);
        }
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            vertexBinding.Buffer.SetData(commandList,
                new DataPointer(cmdList.VtxBuffer.Data, cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()),
                vtxOffset);

            indexBinding.Buffer.SetData(commandList,
                new DataPointer(cmdList.IdxBuffer.Data, cmdList.IdxBuffer.Size * sizeof(ushort)),
                idxOffset);

            vtxOffset += cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
            idxOffset += cmdList.IdxBuffer.Size * sizeof(ushort);
        }
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        var surfaceSize = Game.Window.ClientBounds;
        if (surfaceSize.Width <= 1 || surfaceSize.Height <= 1)
            return;

        var projMatrix = Matrix.OrthoRH(surfaceSize.Width, -surfaceSize.Height, -1, 1);

        CheckBuffers(drawData);
        UpdateBuffers(drawData);

        commandList.SetVertexBuffer(0, vertexBinding.Buffer, 0, Unsafe.SizeOf<ImDrawVert>());
        commandList.SetIndexBuffer(indexBinding.Buffer, 0, false);

        // 获取第一个托管纹理作为初始绑定
        Texture? currentTexture = null;
        foreach (var t in managedTextures.Values) { currentTexture = t; break; }
        if (currentTexture != null)
            shader!.Parameters.Set(ImGuiShaderKeys.tex, currentTexture);

        bool? currentTextureOpaque = null;

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var cmd = cmdList.CmdBuffer[i];

                commandList.SetScissorRectangle(new Rectangle(
                    (int)cmd.ClipRect.X,
                    (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                    (int)(cmd.ClipRect.W - cmd.ClipRect.Y)
                ));

                // 解析纹理
                var texId = cmd.TexRef.GetTexID();
                if (managedTextures.TryGetValue(texId, out var managedTexture))
                {
                    shader!.Parameters.Set(ImGuiShaderKeys.tex, managedTexture);
                    currentTexture = managedTexture;
                }
                else if (ImGuiExtension.TryGetTexture((ulong)(nint)texId, out var userTexture) && userTexture != null)
                {
                    shader!.Parameters.Set(ImGuiShaderKeys.tex, userTexture);
                    currentTexture = userTexture;
                }

                bool isOpaqueTexture = currentTexture != null && (currentTexture.Flags & TextureFlags.RenderTarget) != 0;
                if (currentTextureOpaque != isOpaqueTexture)
                {
                    commandList.SetPipelineState(isOpaqueTexture ? opaquePipeline : alphaPipeline);
                    currentTextureOpaque = isOpaqueTexture;
                }

                shader!.Parameters.Set(ImGuiShaderKeys.proj, ref projMatrix);
                shader.Apply(context);

                commandList.DrawIndexed((int)cmd.ElemCount, idxOffset, vtxOffset);
                idxOffset += (int)cmd.ElemCount;
            }

            vtxOffset += cmdList.VtxBuffer.Size;
        }
    }

    protected override void Destroy()
    {
        foreach (var texture in managedTextures.Values)
            texture.Dispose();
        managedTextures.Clear();

        vertexBinding.Buffer?.Dispose();
        indexBinding.Buffer?.Dispose();
        alphaPipeline?.Dispose();
        opaquePipeline?.Dispose();
        shader?.Dispose();

        if (!ImGuiContext.IsNull)
        {
            ImGui.DestroyContext(ImGuiContext);
        }

        base.Destroy();
    }
}
