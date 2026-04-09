using WowBot.Abstractions.Entities;

namespace WowBot.Abstractions;

/// <summary>
/// Боевая ротация для одного спека.
/// В будущем: каждый спек — отдельный класс, реализующий этот интерфейс.
/// Сейчас: AllRotations.cs генерирует Lua, но интерфейс готовит почву для C# ротаций.
/// </summary>
public interface ICombatRotation
{
    /// <summary>Название ротации (напр. "Retribution Paladin")</summary>
    string Name { get; }

    /// <summary>Класс WoW (напр. "PALADIN")</summary>
    string WowClass { get; }

    /// <summary>Подходит ли эта ротация для текущего класса/спека</summary>
    bool IsMatch(string playerClass, string? specName);

    /// <summary>
    /// Получить Lua-скрипт для выполнения (текущий подход).
    /// Возвращает полный скрипт ротации.
    /// </summary>
    string GetFullScript();

    /// <summary>Получить instant-скрипт (кастуется без GCD)</summary>
    string GetInstantScript();
}
