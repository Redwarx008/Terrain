using System.Globalization;
using TerrainPreProcessor.Resources;

namespace TerrainPreProcessor.Services;

/// <summary>
/// 本地化服务，用于获取当前语言的资源字符串
/// </summary>
public class LocalizationService
{
    private static LocalizationService? _instance;
    
    public static LocalizationService Instance => _instance ??= new LocalizationService();
    
    private LocalizationService()
    {
        // 自动使用系统语言
        var currentCulture = CultureInfo.CurrentUICulture;
        SetLanguage(currentCulture.Name);
    }
    
    /// <summary>
    /// 获取本地化字符串
    /// </summary>
    public string this[string key] => GetString(key);
    
    /// <summary>
    /// 获取本地化字符串
    /// </summary>
    public string GetString(string key)
    {
        return Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
    }
    
    /// <summary>
    /// 设置语言
    /// </summary>
    public void SetLanguage(string cultureName)
    {
        try
        {
            var culture = new CultureInfo(cultureName);
            Strings.Culture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // 如果指定的文化不存在，使用默认（英文）
            Strings.Culture = CultureInfo.InvariantCulture;
        }
    }
    
    /// <summary>
    /// 获取当前语言代码
    /// </summary>
    public string CurrentLanguage => Strings.Culture?.Name ?? "en";
}
