#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 检视器面板 - 显示和编辑选中对象的属性
/// </summary>
public class InspectorPanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 当前选中的对象
    /// </summary>
    public object? SelectedObject { get; set; }

    /// <summary>
    /// 属性组列表
    /// </summary>
    public List<PropertyGroup> PropertyGroups { get; } = new();

    #endregion

    #region 构造函数

    public InspectorPanel()
    {
        Title = "Inspector";
        Icon = Icons.Edit;
        ShowTitleBar = true;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        if (SelectedObject == null)
        {
            RenderEmptyState();
            return;
        }

        // 开始滚动区域
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));
        ImGui.BeginChild($"##inspector_content_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None);

        // 渲染对象头部信息
        RenderObjectHeader();

        ImGui.Separator();

        // 渲染属性组
        foreach (var group in PropertyGroups)
        {
            RenderPropertyGroup(group);
        }

        ImGui.EndChild();
    }

    private void RenderEmptyState()
    {
        var drawList = ImGui.GetWindowDrawList();

        string message = "Select an object to view its properties";
        var textSize = ImGui.CalcTextSize(message);
        var textPos = new Vector2(
            ContentRect.X + (ContentRect.Width - textSize.X) * 0.5f,
            ContentRect.Y + (ContentRect.Height - textSize.Y) * 0.5f
        );

        drawList.AddText(textPos, ColorPalette.TextSecondary.ToUint(), message);
    }

    private void RenderObjectHeader()
    {
        // 对象名称
        ImGui.PushFont(FontManager.Bold);
        ImGui.Text("Terrain Object");
        ImGui.PopFont();

        // 对象类型
        ImGui.TextDisabled("Terrain Component");

        ImGui.Spacing();

        // 启用开关
        bool isActive = true;
        if (ImGui.Checkbox("Active", ref isActive))
        {
            // TODO: 切换激活状态
        }
    }

    private void RenderPropertyGroup(PropertyGroup group)
    {
        // 折叠头
        ImGui.PushStyleColor(ImGuiCol.Header, ColorPalette.PanelBackground.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColorPalette.Hover.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColorPalette.Pressed.ToVector4());

        bool isOpen = ImGui.CollapsingHeader(group.Name, ImGuiTreeNodeFlags.DefaultOpen);

        ImGui.PopStyleColor(3);

        if (isOpen)
        {
            ImGui.Indent(8);

            foreach (var property in group.Properties)
            {
                RenderProperty(property);
            }

            ImGui.Unindent(8);
        }

        ImGui.Spacing();
    }

    private void RenderProperty(Property property)
    {
        // 属性标签
        float labelWidth = 120;
        ImGui.Text(property.Name);

        // 工具提示
        if (!string.IsNullOrEmpty(property.Description) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(property.Description);
        }

        ImGui.SameLine(labelWidth);
        ImGui.SetNextItemWidth(-1);

        // 根据类型渲染不同的编辑器
        switch (property.Type)
        {
            case PropertyType.Float:
                RenderFloatProperty(property);
                break;

            case PropertyType.Integer:
                RenderIntProperty(property);
                break;

            case PropertyType.Boolean:
                RenderBoolProperty(property);
                break;

            case PropertyType.String:
                RenderStringProperty(property);
                break;

            case PropertyType.Enum:
                RenderEnumProperty(property);
                break;

            case PropertyType.Color:
                RenderColorProperty(property);
                break;

            case PropertyType.Vector2:
                RenderVector2Property(property);
                break;

            case PropertyType.Vector3:
                RenderVector3Property(property);
                break;

            case PropertyType.Texture:
                RenderTextureProperty(property);
                break;

            case PropertyType.Slider:
                RenderSliderProperty(property);
                break;

            default:
                ImGui.TextDisabled("Unsupported type");
                break;
        }
    }

    private void RenderFloatProperty(Property property)
    {
        float value = property.GetPropertyValue<float>();
        if (ImGui.DragFloat($"##{property.Id}", ref value, property.Step, property.MinValue, property.MaxValue, property.Format))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderIntProperty(Property property)
    {
        int value = property.GetPropertyValue<int>();
        if (ImGui.DragInt($"##{property.Id}", ref value, property.Step, (int)property.MinValue, (int)property.MaxValue))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderBoolProperty(Property property)
    {
        bool value = property.GetPropertyValue<bool>();
        if (ImGui.Checkbox($"##{property.Id}", ref value))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderStringProperty(Property property)
    {
        string value = property.GetPropertyValue<string>() ?? "";
        if (ImGui.InputText($"##{property.Id}", ref value, 256))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderEnumProperty(Property property)
    {
        int value = property.GetPropertyValue<int>();
        string[] options = property.Options ?? Array.Empty<string>();

        if (options.Length > 0 && ImGui.Combo($"##{property.Id}", ref value, options, options.Length))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderColorProperty(Property property)
    {
        Vector4 value = property.GetPropertyValue<Vector4>();
        if (ImGui.ColorEdit4($"##{property.Id}", ref value, ImGuiColorEditFlags.NoAlpha))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderVector2Property(Property property)
    {
        Vector2 value = property.GetPropertyValue<Vector2>();
        if (ImGui.DragFloat2($"##{property.Id}", ref value, property.Step))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderVector3Property(Property property)
    {
        System.Numerics.Vector3 value = property.GetPropertyValue<System.Numerics.Vector3>();
        if (ImGui.DragFloat3($"##{property.Id}", ref value, property.Step))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    private void RenderTextureProperty(Property property)
    {
        // 纹理预览框
        Vector2 previewSize = new Vector2(64, 64);
        ImGui.Button($"##{property.Id}", previewSize);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to select texture");
        }

        ImGui.SameLine();

        // 纹理信息
        ImGui.BeginGroup();
        ImGui.Text("Texture");
        ImGui.TextDisabled("None (Default)");

        if (ImGui.Button("Select"))
        {
            // TODO: 打开纹理选择对话框
        }

        ImGui.SameLine();

        if (ImGui.Button("Remove"))
        {
            // TODO: 移除纹理
        }

        ImGui.EndGroup();
    }

    private void RenderSliderProperty(Property property)
    {
        float value = property.GetPropertyValue<float>();
        if (ImGui.SliderFloat($"##{property.Id}", ref value, property.MinValue, property.MaxValue, property.Format))
        {
            property.SetValue?.Invoke(value);
            property.OnChanged?.Invoke(value);
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 添加属性组
    /// </summary>
    public PropertyGroup AddGroup(string name)
    {
        var group = new PropertyGroup { Name = name };
        PropertyGroups.Add(group);
        return group;
    }

    /// <summary>
    /// 清除所有属性
    /// </summary>
    public void Clear()
    {
        PropertyGroups.Clear();
    }

    #endregion
}

/// <summary>
/// 属性组
/// </summary>
public class PropertyGroup
{
    public string Name { get; set; } = "";
    public List<Property> Properties { get; } = new();

    public Property AddProperty<T>(string name, Func<T> getter, Action<T> setter)
    {
        var property = new Property
        {
            Name = name,
            Type = GetPropertyType(typeof(T)),
            GetValue = () => getter(),
            SetValue = (value) => setter((T)value),
            OnChanged = (value) => { }
        };
        Properties.Add(property);
        return property;
    }

    private static PropertyType GetPropertyType(Type type)
    {
        if (type == typeof(float)) return PropertyType.Float;
        if (type == typeof(int)) return PropertyType.Integer;
        if (type == typeof(bool)) return PropertyType.Boolean;
        if (type == typeof(string)) return PropertyType.String;
        if (type == typeof(Vector2)) return PropertyType.Vector2;
        if (type == typeof(System.Numerics.Vector3)) return PropertyType.Vector3;
        if (type == typeof(Vector4)) return PropertyType.Color;
        if (type.IsEnum) return PropertyType.Enum;
        return PropertyType.String;
    }
}

/// <summary>
/// 属性定义
/// </summary>
public class Property
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public PropertyType Type { get; set; }

    public float MinValue { get; set; } = float.MinValue;
    public float MaxValue { get; set; } = float.MaxValue;
    public float Step { get; set; } = 1.0f;
    public string Format { get; set; } = "%.2f";

    public string[]? Options { get; set; }

    public Func<object>? GetValue { get; set; }
    public Action<object>? SetValue { get; set; }
    public Action<object>? OnChanged { get; set; }

    public T GetPropertyValue<T>()
    {
        return (T)(GetValue?.Invoke() ?? default(T)!);
    }
}

/// <summary>
/// 属性类型
/// </summary>
public enum PropertyType
{
    Float,
    Integer,
    Boolean,
    String,
    Enum,
    Color,
    Vector2,
    Vector3,
    Vector4,
    Texture,
    Slider,
    Button
}
