using WowBot.Abstractions.Entities;
using WowBot.HeadlessPoc;

namespace WowBot.Adapter.Headless.Entities;

/// <summary>Игрок (включая локального бота). Сейчас поверх IWowUnit — расширения добавятся в Phase C.</summary>
internal sealed class HeadlessWowPlayer : HeadlessWowUnit, IWowPlayer
{
    public HeadlessWowPlayer(WorldEntity entity, Func<WorldEntity?> localPlayer)
        : base(entity, localPlayer)
    {
    }
}
