namespace WowBot.Core.Game.Rotations;

/// <summary>
/// Общие Lua хелперы для всех C# ротаций.
/// Единый набор — чтобы не было бага "функция не определена" как с IsReady в RetPaladin.
/// </summary>
public static class LuaHelpers
{
    /// <summary>Полный набор хелперов — включать в начало каждой ротации</summary>
    public const string All = @"
local _SN={} local function SN(id) if not _SN[id] then _SN[id]=GetSpellInfo(id) end return _SN[id] end
local function Cast(id) local n=SN(id) if n then CastSpellByName(n) end end
local function IsReady(name) local s,d=GetSpellCooldown(name) return s~=nil and s==0 end
local function IR(id) local n=SN(id) if not n then return false end return IsReady(n) end
local function IU(id) local n=SN(id) if not n then return false end return IsUsableSpell(n) end
local function HB(id) local sn=SN(id) if not sn then return false end for i=1,40 do local n,_,_,_,_,_,_,_,_,_,sId=UnitBuff('player',i) if not n then return false end if sId==id then return true end end return false end
local function HasBuff(name) for i=1,40 do local n=UnitBuff('player',i) if not n then return false end if n==name then return true end end return false end
local function HD(u,id) local sn=SN(id) if not sn then return false end for i=1,40 do local n=UnitDebuff(u,i) if not n then return false end if n==sn then return true end end return false end
local function HasDebuff(unit,name) for i=1,40 do local n=UnitDebuff(unit,i) if not n then return false end if n==name then return true end end return false end
local function BS(id) for i=1,40 do local n,_,_,c,_,_,_,_,_,_,sId=UnitBuff('player',i) if not n then return 0 end if sId==id then return c or 0 end end return 0 end
local function NR(u,id,cid) local sn=SN(id) if not sn then return true end for i=1,40 do local n,_,_,_,_,_,exp=UnitDebuff(u,i) if not n then return true end if n==sn then local left=exp-GetTime() local _,_,_,ct=GetSpellInfo(cid or id) return left<=(ct or 1500)/1000 end end return true end
local function PHP() local h,hm=UnitHealth('player'),UnitHealthMax('player') if hm==0 then return 1 end return h/hm end
local function THP() local h,hm=UnitHealth('target'),UnitHealthMax('target') if hm==0 then return 1 end return h/hm end
local function MP() local m,mm=UnitMana('player'),UnitManaMax('player') if mm==0 then return 1 end return m/mm end
local function CP() return GetComboPoints('player','target') or 0 end
local function CDLeft(name) local s,d=GetSpellCooldown(name) if not s or s==0 then return 0 end return s+d-GetTime() end
local function CastOn(u,id) local n=SN(id) if n and u then RunMacroText('/cast [@'..u..'] '..n) end end
local function HasBuffById(id) for i=1,40 do local n,_,_,_,_,_,_,_,_,_,sId=UnitBuff('player',i) if not n then return false end if sId==id then return true end end return false end
";

    /// <summary>Pre-checks для DPS ротаций</summary>
    public const string PreChecksDPS = @"
if IsMounted() then return end
if UnitIsDeadOrGhost('player') then return end
if not WB_S then WB_S={} end
if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
if not UnitAffectingCombat('target') then return end
if not UnitExists('target') or UnitIsDeadOrGhost('target') or not UnitCanAttack('player','target') then return end
if not WB_SA or GetTime()-WB_SA>1 then WB_SA=GetTime() RunMacroText('/startattack') end
";

    /// <summary>Pre-checks для Healer ротаций (без проверки таргета)</summary>
    public const string PreChecksHealer = @"
if IsMounted() then return end
if UnitIsDeadOrGhost('player') then return end
if not WB_S then WB_S={} end
if UnitCastingInfo('player') or UnitChannelInfo('player') then return end
";

    /// <summary>Полные хелперы из AllRotations (TryTaunt, TryDefCD, IsTankUnit, TryRes, HealerFindTarget)</summary>
    public static string FullHelpers => AllRotations.SharedHelpers;

    /// <summary>HealerFindTarget из AllRotations</summary>
    public static string HealerFindTarget => AllRotations.SharedHealerFindTarget;

    /// <summary>Обёртка pcall с базовыми хелперами</summary>
    public static string Wrap(string funcName, string body) =>
        $"local function {funcName}() {All}{body} end local ok,err=pcall({funcName}) if not ok then WB_ERR=err end ";

    /// <summary>Обёртка pcall с полными хелперами (TryTaunt, TryDefCD и т.д.)</summary>
    public static string WrapFull(string funcName, string body) =>
        $"local function {funcName}() {FullHelpers}{body} end local ok,err=pcall({funcName}) if not ok then WB_ERR=err end ";

    public static string WrapDPS(string funcName, string body) =>
        WrapFull(funcName, PreChecksDPS + body);

    public static string WrapHealer(string funcName, string body) =>
        WrapFull(funcName, PreChecksHealer + body);
}
