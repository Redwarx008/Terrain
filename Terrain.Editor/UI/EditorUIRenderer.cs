#nullable enable

using Hexa.NET.ImGui;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Rendering;
using Stride.CommunityToolkit.ImGui;
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

namespace Terrain.Editor.UI;

/// <summary>
/// 编辑器UI渲染器 - 管理ImGui的初始化和渲染
/// </summary>
public class EditorUIRenderer : GameSystemBase
{
    public readonly ImGuiContextPtr ImGuiContext;

    private ImGuiIOPtr io;
    private float scale = 1.0f;

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
    Texture? fontTexture;
    bool frameBegun;

    private Dictionary<Keys, ImGuiKey> keyMap = new();
    private readonly Dictionary<Texture, nint> textureToId = new();
    private readonly Dictionary<nint, Texture> idToTexture = new();
    private nint nextTextureId = 1;

    public Action? OnRender { get; set; }

    public ImTextureID GetOrCreateTextureId(Texture texture)
    {
        if (!textureToId.TryGetValue(texture, out nint textureId))
        {
            textureId = nextTextureId++;
            textureToId[texture] = textureId;
            idToTexture[textureId] = texture;
        }

        return Unsafe.BitCast<nint, ImTextureID>(textureId);
    }

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
            CreateFontTexture();
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

        // 设置输入
        SetupInput();

        // 创建设备资源
        CreateDeviceObjects();

        // 创建字体纹理
        CreateFontTexture();

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

    private void CreateDeviceObjects()
    {
        commandList = context.CommandList;

        // 加载着色器
        shader = new EffectInstance(effectSystem.LoadEffect("ImGuiShader").WaitForResult());
        shader.UpdateEffect(device);

        // 顶点布局 - 使用Stride的Vector2类型
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

        alphaPipeline = PipelineState.New(device, ref pipelineDesc);

        pipelineDesc.BlendState = BlendStates.Opaque;
        opaquePipeline = PipelineState.New(device, ref pipelineDesc);

        // 创建缓冲区
        const int initialVertexCount = 128;
        const int initialIndexCount = 128;

        var indexBuffer = StrideBuffer.Index.New(device, initialIndexCount * sizeof(ushort), GraphicsResourceUsage.Dynamic);
        indexBinding = new IndexBufferBinding(indexBuffer, false, 0);

        var vertexBuffer = StrideBuffer.Vertex.New(device, initialVertexCount * vertexLayout.CalculateSize(), GraphicsResourceUsage.Dynamic);
        vertexBinding = new VertexBufferBinding(vertexBuffer, vertexLayout, 0);
    }

    private unsafe void CreateFontTexture()
    {
        ImGui.SetCurrentContext(ImGuiContext);
        fontTexture?.Dispose();

        // 只有第一次才添加字体
        // Rebuild the atlas whenever the UI scale changes.
        io.Fonts.Clear();

        // Keep the default font for regular text and labels.
        var defaultFont = io.Fonts.AddFontDefault();
        defaultFont.Scale = scale;
        FontManager.Initialize(defaultFont);

        string fontsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string iconFontPath = Path.Combine(fontsDirectory, "segmdl2.ttf");
        if (!File.Exists(iconFontPath))
        {
            iconFontPath = Path.Combine(fontsDirectory, "SegoeIcons.ttf");
        }
        if (File.Exists(iconFontPath))
        {
            // Load the icon font as a separate face. Merging via ImFontConfig was
            // causing a native startup assertion with this ImGui binding.
            var iconFont = io.Fonts.AddFontFromFileTTF(
                iconFontPath,
                FontManager.IconSize * scale,
                FontManager.GetIconGlyphRanges());
            FontManager.SetIconFont(iconFont);

            // 初始化字体管理器
        }

        byte* pixelData;
        int width, height, bytesPerPixel;
        io.Fonts.GetTexDataAsRGBA32(&pixelData, &width, &height, &bytesPerPixel);

        fontTexture = Texture.New2D(device, width, height, PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource);
        fontTexture.SetData(commandList, new DataPointer(pixelData, width * height * bytesPerPixel));
    }

    public override void Update(GameTime gameTime)
    {
        ImGui.SetCurrentContext(ImGuiContext);

        // 更新显示尺寸
        var surfaceSize = Game.Window.ClientBounds;
        if (Game.Window.IsMinimized || surfaceSize.Width <= 1 || surfaceSize.Height <= 1)
        {
            // 最小化时窗口可能退化到 1x1，直接跳过 ImGui 帧，避免后续渲染链使用非法尺寸。
            io.DisplaySize = Vector2.Zero;
            frameBegun = false;
            input?.TextInput.DisableTextInput();
            return;
        }

        Scale = GetUiScale();
        io.DisplaySize = new System.Numerics.Vector2(surfaceSize.Width, surfaceSize.Height);

        // 确保DeltaTime为正数
        var deltaTime = (float)gameTime.TimePerFrame.TotalSeconds;
        io.DeltaTime = deltaTime > 0 ? deltaTime : 1f / 60f;

        // 处理输入
        if (input != null && input.HasMouse && !input.IsMousePositionLocked)
        {
            var mousePos = input.AbsoluteMousePosition;
            io.MousePos = new System.Numerics.Vector2(mousePos.X, mousePos.Y);

            // 文本输入
            if (io.WantTextInput)
            {
                input.TextInput.EnabledTextInput();
            }
            else
            {
                input.TextInput.DisableTextInput();
            }

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
                        io.MouseWheel += mw.WheelDelta;
                        break;
                }
            }

            // 鼠标按钮
            io.MouseDown[0] = input.IsMouseButtonDown(MouseButton.Left);
            io.MouseDown[1] = input.IsMouseButtonDown(MouseButton.Right);
            io.MouseDown[2] = input.IsMouseButtonDown(MouseButton.Middle);

            // 修饰键
            io.KeyAlt = input.IsKeyDown(Keys.LeftAlt) || input.IsKeyDown(Keys.RightAlt);
            io.KeyShift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            io.KeyCtrl = input.IsKeyDown(Keys.LeftCtrl) || input.IsKeyDown(Keys.RightCtrl);
            io.KeySuper = input.IsKeyDown(Keys.LeftWin) || input.IsKeyDown(Keys.RightWin);
        }

        // 开始新帧
        ImGui.NewFrame();
        frameBegun = true;

        // 调用自定义渲染回调（在NewFrame之后，Render之前）
        OnRender?.Invoke();
    }

    private float GetUiScale()
    {
        nint hwnd = Game.Window.NativeWindow?.Handle ?? nint.Zero;
        float dpiScale = WindowInterop.GetWindowScaleFactor(hwnd);

        // 量化到 0.05，避免连续拖动窗口或跨屏时频繁重建字体纹理。
        return MathF.Round(dpiScale * 20.0f) / 20.0f;
    }

    public override void EndDraw()
    {
        if (!frameBegun)
            return;

        ImGui.SetCurrentContext(ImGuiContext);
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
        frameBegun = false;
    }

    /// <summary>
    /// 在窗口最小化或恢复冷却期内主动终止当前 ImGui 帧，避免出现“本帧已开始但不会进入 EndDraw”的悬空状态。
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

        // 视图投影矩阵
        var surfaceSize = Game.Window.ClientBounds;
        if (surfaceSize.Width <= 1 || surfaceSize.Height <= 1)
            return;

        var projMatrix = Matrix.OrthoRH(surfaceSize.Width, -surfaceSize.Height, -1, 1);

        // 检查并更新缓冲区
        CheckBuffers(drawData);
        UpdateBuffers(drawData);

        // 设置管线
        commandList.SetVertexBuffer(0, vertexBinding.Buffer, 0, Unsafe.SizeOf<ImDrawVert>());
        commandList.SetIndexBuffer(indexBinding.Buffer, 0, false);
        Texture? currentTexture = null;
        bool? currentTextureOpaque = null;

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var cmd = cmdList.CmdBuffer[i];

                // 设置裁剪区域
                commandList.SetScissorRectangle(new Rectangle(
                    (int)cmd.ClipRect.X,
                    (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                    (int)(cmd.ClipRect.W - cmd.ClipRect.Y)
                ));

                // 设置投影矩阵
                Texture? texture = ResolveTexture(cmd.TextureId);
                if (!ReferenceEquals(currentTexture, texture))
                {
                    shader!.Parameters.Set(ImGuiShaderKeys.tex, texture);
                    currentTexture = texture;
                }

                bool isOpaqueTexture = texture != null && (texture.Flags & TextureFlags.RenderTarget) != 0;
                if (currentTextureOpaque != isOpaqueTexture)
                {
                    // Scene render targets should be composited as fully opaque images. If we render them
                    // through the normal ImGui alpha pipeline, any zero/undefined alpha coming out of the
                    // offscreen scene path gets blended with the black editor background and the horizon
                    // turns unnaturally black compared to Terrain.exe.
                    commandList.SetPipelineState(isOpaqueTexture ? opaquePipeline : alphaPipeline);
                    currentTextureOpaque = isOpaqueTexture;
                }

                shader.Parameters.Set(ImGuiShaderKeys.proj, ref projMatrix);
                shader.Apply(context);

                // 绘制
                commandList.DrawIndexed((int)cmd.ElemCount, idxOffset, vtxOffset);
                idxOffset += (int)cmd.ElemCount;
            }

            vtxOffset += cmdList.VtxBuffer.Size;
        }
    }

    private Texture? ResolveTexture(ImTextureID textureId)
    {
        nint handle = Unsafe.BitCast<ImTextureID, nint>(textureId);
        if (handle == 0)
        {
            return fontTexture;
        }

        return idToTexture.TryGetValue(handle, out Texture? texture)
            ? texture
            : fontTexture;
    }

    protected override void Destroy()
    {
        fontTexture?.Dispose();
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
