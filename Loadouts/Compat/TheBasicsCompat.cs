using System.Runtime.CompilerServices;
using thebasics.Configs;
using thebasics.Extensions;
using thebasics.ModSystems.CharacterSheets;
using thebasics.ModSystems.ProximityChat;
using thebasics.Utilities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Loadouts.Compat;

public class TheBasicsCompat
{
    public static void SetNickname(ICoreServerAPI sapi, IServerPlayer byPlayer, Loadout loadout)
    {
        if (LoadoutContentManager.theBasicsLoaded) SetNicknameInternal(sapi, byPlayer, loadout);
    }
    [MethodImpl( MethodImplOptions.NoInlining)]
    private static void SetNicknameInternal(ICoreServerAPI sapi, IServerPlayer byPlayer, Loadout loadout)
    {
        ModConfig Config = sapi.ModLoader.GetModSystem<RPProximityChatSystem>().Config;
        sapi.Logger.Event("Nickname: " + loadout.nickName);
        byPlayer.SetNickname(loadout.nickName, Config); 
        SwapOutNameTag(byPlayer, sapi);
    }
    
    internal static void SwapOutNameTag(IServerPlayer player, ICoreServerAPI sapi)
    {
        if (!LoadoutContentManager.theBasicsLoaded) return;
        SwapOutNameTagInternal(player, sapi);
    }
    
    [MethodImpl( MethodImplOptions.NoInlining)]
    private static void SwapOutNameTagInternal(IServerPlayer player, ICoreServerAPI sapi)
    {
        if (!LoadoutContentManager.theBasicsLoaded) return;
        ModConfig Config = sapi.ModLoader.GetModSystem<RPProximityChatSystem>().Config;
        var behavior = player.Entity.GetBehavior<EntityBehaviorNameTag>();

        if (behavior == null)
        {
            return;
        }

        // Apply visibility/range settings regardless of whether we're overriding the display name.
        behavior.ShowOnlyWhenTargeted = Config.HideNametagUnlessTargeting;
        behavior.RenderRange = Config.NametagRenderRange;

        // Determine the visible nametag string.
        string displayName;
        if (Config.ShowNicknameInNametag)
        {
            var nickname = player.GetNickname();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                displayName = Config.ShowPlayerNameInNametag ? player.PlayerName : "";
            }
            else
            {
                displayName = Config.ShowPlayerNameInNametag ? $"{nickname} ({player.PlayerName})" : nickname;
            }
        }
        else
        {
            displayName = Config.ShowPlayerNameInNametag ? player.PlayerName : "";
        }

        behavior.SetName(displayName);
    }

    public static string GetNickname(ICoreServerAPI sapi, IServerPlayer byPlayer)
    {
        if (LoadoutContentManager.theBasicsLoaded) return GetNicknameInternal(sapi, byPlayer);
        return byPlayer.PlayerName;
    }
    
    [MethodImpl( MethodImplOptions.NoInlining)]
    public static string GetNicknameInternal(ICoreServerAPI sapi, IServerPlayer byPlayer)
    {
        ModConfig Config = sapi.ModLoader.GetModSystem<RPProximityChatSystem>().Config;
        return (byPlayer).GetNickname(Config);
    }
}