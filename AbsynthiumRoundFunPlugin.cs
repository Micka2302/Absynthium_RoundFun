using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace Absynthium.RoundFun;

public enum RoundFunType
{
    NoscopAwp = 0,
    Ssg = 1,
    SsgNoscop = 2
}

public sealed class RoundFunConfig : BasePluginConfig
{
    public override int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public int FunRoundChancePercent { get; set; } = 30;

    public bool EnableNoscopAwp { get; set; } = true;

    public bool EnableSsg { get; set; } = true;

    public bool EnableSsgNoscop { get; set; } = true;

    public string ForceCommandPermission { get; set; } = "@css/ban";

    public bool StripWeapons { get; set; } = true;

    public bool AnnounceInChat { get; set; } = true;

    public bool AnnounceCenterHtml { get; set; } = true;

    public int AnnouncementDurationSeconds { get; set; } = 10;
}

[MinimumApiVersion(333)]
public sealed class AbsynthiumRoundFunPlugin : BasePlugin, IPluginConfig<RoundFunConfig>
{
    private const byte LifeStateAlive = 0;
    private const string LangDirectoryName = "lang";
    private const string LangFileName = "en.json";
    private const ulong NoScopeInputMask = (ulong)(PlayerButtons.Attack2 | PlayerButtons.Zoom);
    private const int NoScopeSecondaryLockTicks = 5000;
    private const uint NoScopeDefaultFov = 90;
    private const int MinAnnouncementDurationSeconds = 5;
    private const int MaxAnnouncementDurationSeconds = 12;

    private static readonly JsonSerializerOptions LangJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, string> DefaultMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chat.prefix"] = "{green}[Absynthium RoundFun]{default}",
        ["command.only_ingame"] = "{red}This command is only available in-game.",
        ["command.no_access"] = "{red}You do not have access to this command.",
        ["command.disabled"] = "{red}This round fun type is disabled in the config.",
        ["command.queued"] = "{lightgreen}Round fun queued for next round: {orange}{0}{default}.",
        ["command.queued_by"] = "{orange}{0}{default} forced next round fun: {orange}{1}{default}.",
        ["round.announce.chat"] = "{yellow}ROUND FUN - {orange}{0}",
        ["round.announce.html"] = "<font class='fontSize-l' color='#FFB347'>ROUND FUN</font><br><font class='fontSize-m' color='#FFFFFF'>{0}</font>",
        ["round.announce.center"] = "<font color='#FFB347'>{0}</font> <br> {1} <br> <font color='white'>ROUND FUN</font>",
        ["round.announce.image.noscope_awp"] = "<img src='https://raw.githubusercontent.com/Micka2302/cs2-retakes-allocator-2.0/refs/heads/main/Resources/ASITEv2.png' class=''>",
        ["round.announce.image.ssg"] = "<img src='https://raw.githubusercontent.com/Micka2302/cs2-retakes-allocator-2.0/refs/heads/main/Resources/BSITEv2.png' class=''>",
        ["round.announce.image.ssg_noscope"] = "<img src='https://raw.githubusercontent.com/Micka2302/cs2-retakes-allocator-2.0/refs/heads/main/Resources/ASITEv2.png' class=''>",
        ["round.name.noscope_awp"] = "NOSCOPE AWP",
        ["round.name.ssg"] = "SSG",
        ["round.name.ssg_noscope"] = "SSG NOSCOPE"
    };

    private readonly HashSet<int> _noScopeSlots = new();
    private readonly HashSet<int> _equippedSlots = new();
    private readonly Random _random = new();
    private Dictionary<string, string> _messages = BuildDefaultMessages();
    private RoundFunType? _activeRound;
    private RoundFunType? _centerAnnouncementRound;
    private RoundFunType? _forcedNextRound;
    private float _centerAnnouncementEndTime;
    private string _centerAnnouncementHtml = string.Empty;
    private string _messagesPath = string.Empty;

    public RoundFunConfig Config { get; set; } = new();

    public override string ModuleName => "Absynthium_RoundFun";

    public override string ModuleVersion => "1.0.0";

    public override string ModuleAuthor => "micka";

    public override string ModuleDescription => "Rounds fun aleatoires pour serveur AWP: noscope AWP, SSG et SSG noscope.";

    public void OnConfigParsed(RoundFunConfig config)
    {
        Config = NormalizeConfig(config ?? new RoundFunConfig());
    }

    public override void Load(bool hotReload)
    {
        LoadMessages();

        AddCommand("css_noscopawp", "Force next round fun: noscope AWP", (caller, command) => QueueForcedRound(caller, command, RoundFunType.NoscopAwp));
        AddCommand("css_noscopeawp", "Force next round fun: noscope AWP", (caller, command) => QueueForcedRound(caller, command, RoundFunType.NoscopAwp));
        AddCommand("css_ssg", "Force next round fun: SSG", (caller, command) => QueueForcedRound(caller, command, RoundFunType.Ssg));
        AddCommand("css_noscopssg", "Force next round fun: SSG noscope", (caller, command) => QueueForcedRound(caller, command, RoundFunType.SsgNoscop));
        AddCommand("css_noscopessg", "Force next round fun: SSG noscope", (caller, command) => QueueForcedRound(caller, command, RoundFunType.SsgNoscop));

        RegisterListener<Listeners.OnTick>(OnTick);

        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect, HookMode.Post);
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnTick>(OnTick);

        _activeRound = null;
        _forcedNextRound = null;
        ClearCenterAnnouncement();
        ClearNoScopeTargets();
        ClearEquippedTargets();
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _activeRound = null;
        ClearCenterAnnouncement();
        ResetNoScopeRestrictionsForAllPlayers();
        ClearNoScopeTargets();
        ClearEquippedTargets();

        if (!Config.Enabled)
        {
            _forcedNextRound = null;
            return HookResult.Continue;
        }

        var forcedRound = ConsumeForcedRound();
        var selectedRound = forcedRound ?? PickRandomRound();
        if (selectedRound is null)
        {
            return HookResult.Continue;
        }

        _activeRound = selectedRound.Value;
        AnnounceRound(_activeRound.Value);
        ScheduleApplyRoundToAllPlayers();

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        ResetNoScopeRestrictionsForAllPlayers();
        _activeRound = null;
        ClearCenterAnnouncement();
        ClearNoScopeTargets();
        ClearEquippedTargets();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (_activeRound is null || !IsRoundPlayer(player))
        {
            return HookResult.Continue;
        }

        var slot = player!.Slot;
        AddTimer(0.10f, () => ApplyRoundToSlot(slot));
        AddTimer(0.50f, () => ApplyRoundToSlot(slot));

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } player)
        {
            ResetNoScopeRestrictions(player);
            _noScopeSlots.Remove(player.Slot);
            _equippedSlots.Remove(player.Slot);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } player)
        {
            _noScopeSlots.Remove(player.Slot);
            _equippedSlots.Remove(player.Slot);
        }

        return HookResult.Continue;
    }

    private HookResult OnWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        HandleNoScopeZoom(@event.Userid);
        return HookResult.Continue;
    }

    private HookResult OnWeaponZoomRifle(EventWeaponZoomRifle @event, GameEventInfo info)
    {
        HandleNoScopeZoom(@event.Userid);
        return HookResult.Continue;
    }

    private void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!IsRoundPlayer(player))
        {
            return;
        }

        if (!_noScopeSlots.Contains(player.Slot))
        {
            return;
        }

        var attemptedZoom = (pressed & (PlayerButtons.Attack2 | PlayerButtons.Zoom)) != 0;
        if (!attemptedZoom)
        {
            return;
        }

        BlockNoScopeInput(player);
        BlockScopeWeaponSecondary(player);
        ResetNoScopeCamera(player);
        ForceUnscope(player);
    }

    private void OnTick()
    {
        if (_centerAnnouncementRound is not null)
        {
            ShowCenterAnnouncement();
        }

        if (_noScopeSlots.Count == 0)
        {
            return;
        }

        List<int>? slotsToRemove = null;
        foreach (var slot in _noScopeSlots)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (!IsRoundPlayer(player) || !IsAlive(player!))
            {
                slotsToRemove ??= new List<int>();
                slotsToRemove.Add(slot);
                continue;
            }

            BlockNoScopeInput(player!);
            BlockScopeWeaponSecondary(player!);
            ResetNoScopeCamera(player!);
            ForceUnscope(player!);
        }

        if (slotsToRemove is null)
        {
            return;
        }

        foreach (var slot in slotsToRemove)
        {
            _noScopeSlots.Remove(slot);
        }
    }

    private void QueueForcedRound(CCSPlayerController? caller, CommandInfo command, RoundFunType roundType)
    {
        if (!Config.Enabled)
        {
            Reply(caller, command, T("command.disabled"));
            return;
        }

        if (!CanUseForceCommand(caller))
        {
            Reply(caller, command, T(caller is null ? "command.only_ingame" : "command.no_access"));
            return;
        }

        if (!IsRoundEnabled(roundType))
        {
            Reply(caller, command, T("command.disabled"));
            return;
        }

        _forcedNextRound = roundType;

        var roundName = GetRoundName(roundType);
        Reply(caller, command, T("command.queued", roundName));
        Broadcast(T("command.queued_by", GetCallerName(caller), roundName), exceptSlot: caller?.Slot);
    }

    private RoundFunType? ConsumeForcedRound()
    {
        if (_forcedNextRound is not { } forcedRound)
        {
            return null;
        }

        _forcedNextRound = null;
        return IsRoundEnabled(forcedRound) ? forcedRound : null;
    }

    private RoundFunType? PickRandomRound()
    {
        var enabledRounds = GetEnabledRounds();
        if (enabledRounds.Count == 0)
        {
            return null;
        }

        var chance = Math.Clamp(Config.FunRoundChancePercent, 0, 100);
        if (chance <= 0 || _random.Next(1, 101) > chance)
        {
            return null;
        }

        return enabledRounds[_random.Next(enabledRounds.Count)];
    }

    private List<RoundFunType> GetEnabledRounds()
    {
        var rounds = new List<RoundFunType>(3);
        if (Config.EnableNoscopAwp)
        {
            rounds.Add(RoundFunType.NoscopAwp);
        }

        if (Config.EnableSsg)
        {
            rounds.Add(RoundFunType.Ssg);
        }

        if (Config.EnableSsgNoscop)
        {
            rounds.Add(RoundFunType.SsgNoscop);
        }

        return rounds;
    }

    private bool IsRoundEnabled(RoundFunType roundType)
        => roundType switch
        {
            RoundFunType.NoscopAwp => Config.EnableNoscopAwp,
            RoundFunType.Ssg => Config.EnableSsg,
            RoundFunType.SsgNoscop => Config.EnableSsgNoscop,
            _ => false
        };

    private void ScheduleApplyRoundToAllPlayers()
    {
        AddTimer(0.10f, ApplyRoundToAllPlayers);
        AddTimer(0.50f, ApplyRoundToAllPlayers);
    }

    private void ApplyRoundToAllPlayers()
    {
        if (_activeRound is null)
        {
            return;
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRoundPlayer(player) || !IsAlive(player!))
            {
                continue;
            }

            ApplyRoundToPlayer(player!);
        }
    }

    private void ApplyRoundToSlot(int slot)
    {
        if (_activeRound is null)
        {
            return;
        }

        var player = Utilities.GetPlayerFromSlot(slot);
        if (!IsRoundPlayer(player) || !IsAlive(player!))
        {
            return;
        }

        ApplyRoundToPlayer(player!);
    }

    private void ApplyRoundToPlayer(CCSPlayerController player)
    {
        if (_activeRound is null)
        {
            return;
        }

        var round = _activeRound.Value;
        if (!_equippedSlots.Add(player.Slot))
        {
            return;
        }

        if (Config.StripWeapons)
        {
            player.RemoveWeapons();
        }

        player.GiveNamedItem(GetRoundWeapon(round));
        player.GiveNamedItem(CsItem.Knife);
        player.GiveNamedItem(CsItem.AssaultSuit);

        if (IsNoScopeRound(round))
        {
            _noScopeSlots.Add(player.Slot);
        }
    }

    private static CsItem GetRoundWeapon(RoundFunType roundType)
        => roundType == RoundFunType.NoscopAwp ? CsItem.AWP : CsItem.SSG08;

    private static bool IsNoScopeRound(RoundFunType roundType)
        => roundType is RoundFunType.NoscopAwp or RoundFunType.SsgNoscop;

    private void AnnounceRound(RoundFunType roundType)
    {
        var roundName = GetRoundName(roundType);

        if (Config.AnnounceInChat)
        {
            Broadcast(T("round.announce.chat", roundName));
        }

        if (!Config.AnnounceCenterHtml)
        {
            return;
        }

        var duration = Math.Clamp(Config.AnnouncementDurationSeconds, MinAnnouncementDurationSeconds, MaxAnnouncementDurationSeconds);
        var html = BuildAnnouncementHtml(roundType, roundName);
        StartCenterAnnouncement(roundType, html, duration);
    }

    private void StartCenterAnnouncement(RoundFunType roundType, string html, int duration)
    {
        _centerAnnouncementRound = roundType;
        _centerAnnouncementHtml = html;
        _centerAnnouncementEndTime = Server.CurrentTime + duration;

        AddTimer(duration, () =>
        {
            if (_centerAnnouncementRound == roundType)
            {
                ClearCenterAnnouncement();
            }
        });
    }

    private void ShowCenterAnnouncement()
    {
        if (_centerAnnouncementRound is not { } roundType)
        {
            return;
        }

        if (_activeRound != roundType || Server.CurrentTime >= _centerAnnouncementEndTime)
        {
            ClearCenterAnnouncement();
            return;
        }

        if (string.IsNullOrWhiteSpace(_centerAnnouncementHtml))
        {
            return;
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRealPlayer(player) ||
                player!.Team is not CsTeam.Terrorist and not CsTeam.CounterTerrorist)
            {
                continue;
            }

            try
            {
                player.PrintToCenterHtml(_centerAnnouncementHtml);
            }
            catch
            {
                // Some clients may not be ready to receive HUD events during connect/team transitions.
            }
        }
    }

    private string BuildAnnouncementHtml(RoundFunType roundType, string roundName)
        => T("round.announce.center", roundName, T(GetRoundImageKey(roundType)));

    private static string GetRoundImageKey(RoundFunType roundType)
        => roundType switch
        {
            RoundFunType.NoscopAwp => "round.announce.image.noscope_awp",
            RoundFunType.Ssg => "round.announce.image.ssg",
            RoundFunType.SsgNoscop => "round.announce.image.ssg_noscope",
            _ => "round.announce.image.noscope_awp"
        };

    private void ClearCenterAnnouncement()
    {
        _centerAnnouncementRound = null;
        _centerAnnouncementHtml = string.Empty;
        _centerAnnouncementEndTime = 0.0f;
    }

    private string GetRoundName(RoundFunType roundType)
        => roundType switch
        {
            RoundFunType.NoscopAwp => T("round.name.noscope_awp"),
            RoundFunType.Ssg => T("round.name.ssg"),
            RoundFunType.SsgNoscop => T("round.name.ssg_noscope"),
            _ => roundType.ToString()
        };

    private void HandleNoScopeZoom(CCSPlayerController? player)
    {
        if (!IsRoundPlayer(player) || !_noScopeSlots.Contains(player!.Slot))
        {
            return;
        }

        BlockScopeWeaponSecondary(player);
        ResetNoScopeCamera(player);
        ForceUnscope(player);
    }

    private static void ForceUnscope(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
        {
            return;
        }

        pawn.IsScoped = false;
        pawn.ResumeZoom = false;

        if (pawn.WeaponServices?.ActiveWeapon.Value is CCSWeaponBaseGun gun && gun.IsValid)
        {
            gun.ZoomLevel = 0;
        }
    }

    private static void BlockScopeWeaponSecondary(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
        {
            return;
        }

        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon is null || !activeWeapon.IsValid || !IsScopeWeapon(activeWeapon.DesignerName))
        {
            return;
        }

        var lockUntilTick = Server.TickCount + NoScopeSecondaryLockTicks;
        if (activeWeapon.NextSecondaryAttackTick < lockUntilTick)
        {
            activeWeapon.NextSecondaryAttackTick = lockUntilTick;
        }

        activeWeapon.NextSecondaryAttackTickRatio = 0.0f;
        if (activeWeapon is CCSWeaponBaseGun gun)
        {
            gun.ZoomLevel = 0;
        }
    }

    private static void ResetNoScopeRestrictions(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
        {
            return;
        }

        pawn.IsScoped = false;
        pawn.ResumeZoom = false;

        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon is not null && activeWeapon.IsValid && IsScopeWeapon(activeWeapon.DesignerName))
        {
            if (activeWeapon.NextSecondaryAttackTick > Server.TickCount)
            {
                activeWeapon.NextSecondaryAttackTick = Server.TickCount;
            }

            activeWeapon.NextSecondaryAttackTickRatio = 0.0f;
            if (activeWeapon is CCSWeaponBaseGun gun)
            {
                gun.ZoomLevel = 0;
            }
        }

        ResetNoScopeCamera(player);
    }

    private static void ResetNoScopeRestrictionsForAllPlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (IsRealPlayer(player))
            {
                ResetNoScopeRestrictions(player!);
            }
        }
    }

    private static bool IsScopeWeapon(string? designerName)
    {
        if (string.IsNullOrWhiteSpace(designerName))
        {
            return false;
        }

        return designerName.Contains("weapon_awp", StringComparison.OrdinalIgnoreCase) ||
               designerName.Contains("weapon_ssg08", StringComparison.OrdinalIgnoreCase) ||
               designerName.Contains("weapon_scar20", StringComparison.OrdinalIgnoreCase) ||
               designerName.Contains("weapon_g3sg1", StringComparison.OrdinalIgnoreCase);
    }

    private static void ResetNoScopeCamera(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
        {
            return;
        }

        var cameraServices = pawn.CameraServices;
        if (cameraServices is null || cameraServices.Handle == IntPtr.Zero)
        {
            return;
        }

        var camera = cameraServices.As<CCSPlayerBase_CameraServices>();
        if (camera.Handle == IntPtr.Zero)
        {
            return;
        }

        player.DesiredFOV = NoScopeDefaultFov;
        camera.FOV = NoScopeDefaultFov;
        camera.FOVStart = NoScopeDefaultFov;
        camera.FOVRate = 0.0f;
        camera.FOVTime = 0.0f;
    }

    private static void BlockNoScopeInput(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
        {
            return;
        }

        var movement = pawn.MovementServices;
        if (movement is null || movement.Handle == IntPtr.Zero)
        {
            return;
        }

        movement.QueuedButtonChangeMask &= ~NoScopeInputMask;
        movement.QueuedButtonDownMask &= ~NoScopeInputMask;
        movement.ToggleButtonDownMask &= ~NoScopeInputMask;
        movement.ButtonDoublePressed &= ~NoScopeInputMask;

        var buttonStates = movement.Buttons.ButtonStates;
        for (var i = 0; i < buttonStates.Length; i++)
        {
            buttonStates[i] &= ~NoScopeInputMask;
        }

        var pressedCmdNumbers = movement.ButtonPressedCmdNumber;
        ClearButtonPressedCommand(pressedCmdNumbers, PlayerButtons.Attack2);
        ClearButtonPressedCommand(pressedCmdNumbers, PlayerButtons.Zoom);

        player.InButtonsWhichAreToggles &= ~NoScopeInputMask;
    }

    private static void ClearButtonPressedCommand(Span<uint> commandNumbers, PlayerButtons button)
    {
        var raw = (ulong)button;
        if (raw == 0 || (raw & (raw - 1UL)) != 0)
        {
            return;
        }

        var index = BitOperations.TrailingZeroCount(raw);
        if (index < 0 || index >= commandNumbers.Length)
        {
            return;
        }

        commandNumbers[index] = 0;
    }

    private void ClearNoScopeTargets()
    {
        _noScopeSlots.Clear();
    }

    private void ClearEquippedTargets()
    {
        _equippedSlots.Clear();
    }

    private bool CanUseForceCommand(CCSPlayerController? caller)
    {
        if (caller is null)
        {
            return true;
        }

        if (!IsRealPlayer(caller))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Config.ForceCommandPermission))
        {
            return true;
        }

        try
        {
            return AdminManager.PlayerHasPermissions(caller, Config.ForceCommandPermission) ||
                   AdminManager.PlayerHasPermissions(caller, "@css/root");
        }
        catch
        {
            return true;
        }
    }

    private static bool IsRoundPlayer(CCSPlayerController? player)
        => IsPlayablePlayer(player, allowBots: false) &&
           player!.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;

    private static bool IsPlayablePlayer(CCSPlayerController? player, bool allowBots)
    {
        try
        {
            if (player is not { IsValid: true } ||
                player.Connected != PlayerConnectedState.PlayerConnected ||
                player.IsHLTV)
            {
                return false;
            }

            return allowBots || !player.IsBot;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRealPlayer(CCSPlayerController? player)
    {
        try
        {
            return player is { IsValid: true } &&
                   player.Connected == PlayerConnectedState.PlayerConnected &&
                   !player.IsBot &&
                   !player.IsHLTV;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAlive(CCSPlayerController player)
    {
        try
        {
            if (!player.PawnIsAlive)
            {
                return false;
            }

            if (player.PlayerPawn.Value is not CCSPlayerPawn pawn || !pawn.IsValid)
            {
                return false;
            }

            if (pawn.LifeState != LifeStateAlive)
            {
                return false;
            }

            return pawn.Health > 0;
        }
        catch
        {
            return false;
        }
    }

    private void Broadcast(string message, int? exceptSlot = null)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsRealPlayer(player) || player!.Slot == exceptSlot)
            {
                continue;
            }

            Tell(player, message);
        }
    }

    private void Reply(CCSPlayerController? caller, CommandInfo command, string message)
    {
        if (IsRealPlayer(caller))
        {
            Tell(caller!, message);
            return;
        }

        command.ReplyToCommand($"{ResolveChatPrefix()} {message}");
    }

    private void Tell(CCSPlayerController player, string message)
    {
        player.PrintToChat($" {ResolveChatPrefix()} {ApplyChatColorTokens(message)}");
    }

    private string GetCallerName(CCSPlayerController? caller)
        => IsRealPlayer(caller) ? caller!.PlayerName : "Console";

    private string ResolveChatPrefix()
    {
        var rawPrefix = T("chat.prefix");
        if (string.IsNullOrWhiteSpace(rawPrefix) || string.Equals(rawPrefix, "chat.prefix", StringComparison.OrdinalIgnoreCase))
        {
            rawPrefix = "{green}[Absynthium RoundFun]{default}";
        }

        return ApplyChatColorTokens(rawPrefix);
    }

    private string T(string key, params object[] args)
    {
        var template = _messages.TryGetValue(key, out var value) ? value : key;
        template = ApplyChatColorTokens(template);
        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch
        {
            return template;
        }
    }

    private static string ApplyChatColorTokens(string text)
    {
        return text
            .Replace("{default}", $"{ChatColors.Default}", StringComparison.OrdinalIgnoreCase)
            .Replace("[default]", $"{ChatColors.Default}", StringComparison.OrdinalIgnoreCase)
            .Replace("{white}", $"{ChatColors.White}", StringComparison.OrdinalIgnoreCase)
            .Replace("[white]", $"{ChatColors.White}", StringComparison.OrdinalIgnoreCase)
            .Replace("{green}", $"{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
            .Replace("[green]", $"{ChatColors.Green}", StringComparison.OrdinalIgnoreCase)
            .Replace("{lightgreen}", $"{ChatColors.Lime}", StringComparison.OrdinalIgnoreCase)
            .Replace("[lightgreen]", $"{ChatColors.Lime}", StringComparison.OrdinalIgnoreCase)
            .Replace("{red}", $"{ChatColors.Red}", StringComparison.OrdinalIgnoreCase)
            .Replace("[red]", $"{ChatColors.Red}", StringComparison.OrdinalIgnoreCase)
            .Replace("{blue}", $"{ChatColors.Blue}", StringComparison.OrdinalIgnoreCase)
            .Replace("[blue]", $"{ChatColors.Blue}", StringComparison.OrdinalIgnoreCase)
            .Replace("{yellow}", $"{ChatColors.Yellow}", StringComparison.OrdinalIgnoreCase)
            .Replace("[yellow]", $"{ChatColors.Yellow}", StringComparison.OrdinalIgnoreCase)
            .Replace("{orange}", $"{ChatColors.Orange}", StringComparison.OrdinalIgnoreCase)
            .Replace("[orange]", $"{ChatColors.Orange}", StringComparison.OrdinalIgnoreCase)
            .Replace("{grey}", $"{ChatColors.Grey}", StringComparison.OrdinalIgnoreCase)
            .Replace("[grey]", $"{ChatColors.Grey}", StringComparison.OrdinalIgnoreCase)
            .Replace("{lightblue}", $"{ChatColors.LightBlue}", StringComparison.OrdinalIgnoreCase)
            .Replace("[lightblue]", $"{ChatColors.LightBlue}", StringComparison.OrdinalIgnoreCase)
            .Replace("{purple}", $"{ChatColors.Purple}", StringComparison.OrdinalIgnoreCase)
            .Replace("[purple]", $"{ChatColors.Purple}", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildDefaultMessages()
        => new(DefaultMessages, StringComparer.OrdinalIgnoreCase);

    private void LoadMessages()
    {
        try
        {
            _messagesPath = ResolveMessagesPath();
            EnsureMessagesFileExists(_messagesPath);

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_messagesPath), LangJsonOptions) ??
                      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in raw)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    parsed[entry.Key.Trim()] = entry.Value ?? string.Empty;
                }
            }

            var merged = BuildDefaultMessages();
            foreach (var entry in parsed)
            {
                merged[entry.Key] = entry.Value;
            }

            if (DefaultMessages.Keys.Any(defaultKey => !parsed.ContainsKey(defaultKey)))
            {
                WriteMessagesFile(_messagesPath, merged);
            }

            _messages = merged;
        }
        catch
        {
            _messages = BuildDefaultMessages();
        }
    }

    private string ResolveMessagesPath()
    {
        var moduleDir = Path.GetDirectoryName(ModulePath);
        return !string.IsNullOrWhiteSpace(moduleDir)
            ? Path.Combine(moduleDir, LangDirectoryName, LangFileName)
            : Path.Combine(AppContext.BaseDirectory, LangDirectoryName, LangFileName);
    }

    private static void EnsureMessagesFileExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            WriteMessagesFile(path, DefaultMessages);
        }
    }

    private static void WriteMessagesFile(string path, IReadOnlyDictionary<string, string> messages)
    {
        var serialized = JsonSerializer.Serialize(messages, LangJsonOptions);
        File.WriteAllText(path, serialized);
    }

    private static RoundFunConfig NormalizeConfig(RoundFunConfig config)
    {
        config.FunRoundChancePercent = Math.Clamp(config.FunRoundChancePercent, 0, 100);
        config.AnnouncementDurationSeconds = Math.Clamp(config.AnnouncementDurationSeconds, MinAnnouncementDurationSeconds, MaxAnnouncementDurationSeconds);
        config.ForceCommandPermission = config.ForceCommandPermission?.Trim() ?? string.Empty;
        return config;
    }
}
