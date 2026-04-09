namespace WowBot.Abstractions;

/// <summary>
/// Выполнение Lua/ASM в основном потоке WoW через EndScene хук.
/// Реализация: WowBot.Core.Game.EndSceneHook
/// </summary>
public interface IGameHook : IDisposable
{
    bool IsHooked { get; }

    /// <summary>Выполнить Lua код в WoW</summary>
    bool ExecuteLua(string luaCode, int timeoutMs = 1000);

    /// <summary>Выполнить Lua и прочитать результат из WB_R</summary>
    string? ExecuteLuaWithResult(string luaCode, uint playerBase = 0, int timeoutMs = 2000);

    /// <summary>Клик по земле (для ground-targeted AoE)</summary>
    bool CastTerrainClick(float x, float y, float z, int timeoutMs = 500);

    /// <summary>Вызвать CGPlayer_C__ClickToMove через хук</summary>
    bool CallClickToMove(float x, float y, float z, uint playerBase,
        int clickType = 4, float precision = 0.5f, int timeoutMs = 500, ulong targetGuid = 0);
}
