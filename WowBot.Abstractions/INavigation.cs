using WowBot.Abstractions.Entities;

namespace WowBot.Abstractions;

/// <summary>
/// Навигация и движение персонажа.
/// Объединяет: поворот к цели, CTM движение, проверки позиции.
/// Реализации: Navigation + ClickToMove (фасад)
/// </summary>
public interface INavigation
{
    // Движение
    void MoveTo(float x, float y, float z, float precision = 1.0f);
    void Stop();

    // Поворот
    bool FaceUnit(IWowUnit player, IWowUnit target);
    bool FaceInstant(IWowUnit player, IWowUnit target);

    // Проверки
    bool IsPlayerStanding(IWowUnit player);
    bool IsPlayerMoving(IWowUnit player);
}
