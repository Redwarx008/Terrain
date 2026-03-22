using Stride.Engine;
using Stride.Games;
using Stride.Profiling;

using var game = new TerrainGame();
game.Run();

/// <summary>
/// 自定义游戏类，用于配置图形设置。
/// </summary>
public class TerrainGame : Game
{
    protected override void BeginRun()
    {
        base.BeginRun();
        
        // 关闭 VSync 以获得更高的帧率
        var graphicsDeviceManager = Services.GetService<IGraphicsDeviceManager>() as GraphicsDeviceManager;
        if (graphicsDeviceManager != null)
        {
            graphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
            graphicsDeviceManager.ApplyChanges();
        }
        
        // 显示帧率和性能信息
        ProfilingSystem.EnableProfiling(false, GameProfilingKeys.GameDrawFPS);
    }
}
