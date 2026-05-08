// Состояние одной MITM сессии (один реальный клиент).
// Содержит две K — одна для разговора с клиентом, другая для разговора с настоящим сервером.

namespace WowBot.MitmProxy;

internal sealed class Session
{
    public required string Account { get; init; }
    public required string Password { get; init; }   // нужен чтобы пройти SRP6 с реальным сервером

    public byte[]? K_Client { get; set; }   // session key между нами и реальным WoW клиентом
    public byte[]? K_Server { get; set; }   // session key между нами и реальным wowcircle сервером

    public List<RealmInfo> Realms { get; set; } = new();
    public RealmInfo? PickedRealm { get; set; }
}

internal sealed record RealmInfo(byte Id, string Name, string Address, byte NumChars);
