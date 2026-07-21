using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Microsoft.VisualBasic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;
using thebasics.Configs;
using thebasics.Extensions;
using thebasics.ModSystems.ProximityChat;
using thebasics.Utilities;

namespace Loadouts;

public class LoadoutContentManager : ModSystem
{
    public Dictionary<string, Loadout> prevLoadouts = new Dictionary<string, Loadout>();
    public const int MaxLoadoutSavedPerPlayer = 10;
    private ICoreServerAPI _sapi = null!;
    private CharacterSystem system;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        //api.Event.PlayerJoin += OnPlayerJoin;
        system = api.ModLoader.GetModSystem<CharacterSystem>();
        var rootCommand = api.ChatCommands.Create("loadout")
            .WithDescription("A tool for admins to quickly change equipment, character, and nickname.")
            .RequiresPrivilege(Privilege.gamemode)
            .RequiresPlayer();
        var subCommandSave = rootCommand.BeginSubCommand("save").WithArgs(api.ChatCommands.Parsers.Unparsed("loadoutName"))
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                string input = args.RawArgs.PopAll();
                if (input != string.Empty)
                {
                    string? loadoutName = input;
                    string? className = byPlayer.Entity.WatchedAttributes.GetAsString("characterClass");
                    if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                    if (string.IsNullOrEmpty(className)) return TextCommandResult.Error("Failed to collect class");
                    Loadout loadout = CreateLoadout((IServerPlayer)byPlayer, loadoutName, api);
                    SaveLoadoutContent(loadout, byPlayer, loadoutName);
                    return TextCommandResult.Success($"Loadout {loadoutName} has been saved");
                }
                else
                {
                    return TextCommandResult.Error("No loadout name provided");
                }
            });
            
        var subCommandLoad = rootCommand.BeginSubCommand("load").WithArgs(api.ChatCommands.Parsers.Unparsed("loadoutName"))
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                string input = args.RawArgs.PopAll();
                if (input != string.Empty)
                {
                    string? loadoutName = input;
                    if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                    GetLoadout((IServerPlayer)byPlayer, loadoutName);
                    return TextCommandResult.Success($"Loadout {loadoutName} has been loaded");
                }
                else
                {
                    return TextCommandResult.Error("No loadout name provided");
                }
            });
            
        var subCommandPrev = rootCommand.BeginSubCommand("prev")
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                Loadout oldLoadout = CreateLoadout((IServerPlayer)byPlayer, byPlayer.PlayerUID, byPlayer.Entity.Api);
                Loadout loadout = LoadPrevLoadout((IServerPlayer)byPlayer);
                prevLoadouts[byPlayer.PlayerUID] = oldLoadout;
                ApplyLoadout(loadout, (IServerPlayer)byPlayer);
                return TextCommandResult.Success("Previous loadout loaded");
            });
            
        var subCommandDelete = rootCommand.BeginSubCommand("delete").WithArgs(api.ChatCommands.Parsers.Unparsed("loadoutName"))
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                string input = args.RawArgs.PopAll();
                if (input != string.Empty)
                {
                    string? loadoutName = input;
                    if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                    return DeleteLoadout((IServerPlayer)byPlayer, loadoutName);
                }
                else
                {
                    return TextCommandResult.Error("No loadout name provided");
                }
            });
        var subCommandList = rootCommand.BeginSubCommand("list")
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                ListLoadouts((IServerPlayer)byPlayer);
                return TextCommandResult.Success();
            });
    }

    private void ListLoadouts(IServerPlayer byPlayer)
    {
        string[] files = GetLoadoutDataFiles(byPlayer);
        if (files.Length == 0)
        {
            _sapi.SendMessage(byPlayer, 0, "No loadouts found", EnumChatType.OwnMessage);
            return;
        }
        string path = GetLoadoutDataPath(byPlayer);
        int count = 1;
        foreach (string file in files)
        {
            string fileTruncated = file.Replace("-" + byPlayer.PlayerUID + ".dat", "");
            fileTruncated = fileTruncated.Replace(path + "\\", "");
            _sapi.SendMessage(byPlayer, 0, $"{count++}. {fileTruncated}", EnumChatType.OwnMessage);
        }
    }

    private TextCommandResult DeleteLoadout(IServerPlayer byPlayer, string loadoutName)
    {
        string[] files = GetLoadoutDataFiles(byPlayer);
        if (files.Length == 0)
        {
            return TextCommandResult.Error("No Loadouts found");
        }
        string path = GetLoadoutDataPath(byPlayer);
        foreach (string file in files)
        {
            string fileTruncated = file.Replace("-" + byPlayer.PlayerUID + ".dat", "");
            fileTruncated = fileTruncated.Replace(path + "\\", "");
            if (fileTruncated == loadoutName)
            {
                File.Delete(file);
                return TextCommandResult.Success($"Loadout {loadoutName} deleted successfully");
            }
        }
        return TextCommandResult.Error("Failed to delete loadout named: " + loadoutName);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
    }

    public static string[] invClassNames =
    [
        GlobalConstants.backpackInvClassName,
        GlobalConstants.characterInvClassName,
        GlobalConstants.hotBarInvClassName
    ];

    private Loadout CreateLoadout(IServerPlayer byPlayer, string loadoutName, ICoreAPI api)
    {
        string serverLoadoutName = loadoutName + "-" + byPlayer.PlayerUID;
        Loadout loadout = new Loadout(serverLoadoutName, byPlayer, api);
        
        foreach (InventoryBasePlayer inv in byPlayer.InventoryManager.InventoriesOrdered)
        {
            if (inv is InventoryPlayerCreative) continue;
            int invCount = inv.Count;
            if (invCount == 0) continue;
            string invID = inv.InventoryID;
            /*
            if (inv is InventoryPlayerBackpacks)
            {
                loadout.Inventories[invID] = new InventoryPlayerBackpacksFix(inv.InventoryID, api, (InventoryPlayerBackpacks)inv);
                int loadoutCount = loadout.Inventories[invID].Count;

                if (loadoutCount < invCount) loadout.Inventories[invID].GenEmptySlots(invCount - loadoutCount);
                //InventoryPlayerBackpacksFix playerInvFix = inv as InventoryPlayerBackpacksFix;
                //InventoryPlayerBackpacksFix invFix = (InventoryPlayerBackpacksFix)loadout.Inventories[invID];
                for (int i = 0; i < (invCount); ++i)//Bug in InventoryPlayerBackpack this[int slotId] setter. missing else at the start of the last line
                {
                    loadout.Inventories[invID][i] = inv[i];
                }
            }
            else
            {
                loadout.Inventories[invID] = (InventoryBasePlayer)inv;
                int loadoutCount = loadout.Inventories[invID].Count;

                if (loadoutCount < invCount) loadout.Inventories[invID].GenEmptySlots(invCount - loadoutCount);
                for (int i = 0; i < inv.Count; ++i)
                {
                    loadout.Inventories[invID][i] = inv[i];
                }
            }*/
        }
        return loadout;
    }

    public void ApplyLoadout(Loadout loadout, IServerPlayer byPlayer)
    {
        prevLoadouts[byPlayer.PlayerUID] = CreateLoadout(byPlayer, byPlayer.PlayerUID, byPlayer.Entity.Api);
        //system.setCharacterClass(byPlayer.Entity, loadout.packet.CharacterClass, false);
        //byPlayer.SetModData("createCharacter", true);
        //Type type = typeof(CharacterSystem);
        //object obj = Activator.CreateInstance(type);
        //MethodInfo method = type.GetMethod("onCharacterSelection", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
        //method.Invoke(obj, new object[] { byPlayer, loadout.packet });
        
        CharacterUpdate(byPlayer, loadout.packet);
        loadout.GiveInventoryCopy(byPlayer);
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() != null)
        {
            byPlayer.SetNickname(loadout.nickName);
            SwapOutNameTag(byPlayer);
        }
    }
    
    private void CharacterUpdate(IServerPlayer fromPlayer, CharacterSelectionPacket p)
    {
        //fromPlayer.SetModData<bool>("createCharacter", true);
        system.setCharacterClass(fromPlayer.Entity, p.CharacterClass, false);
        EntityBehaviorExtraSkinnable behavior = fromPlayer.Entity.GetBehavior<EntityBehaviorExtraSkinnable>();
        behavior.ApplyVoice(p.VoiceType, p.VoicePitch, false);
        foreach (KeyValuePair<string, string> skinPart in p.SkinParts)
            behavior.selectSkinPart(skinPart.Key, skinPart.Value, false);
        DateTime utcNow = DateTime.UtcNow;
        fromPlayer.ServerData.LastCharacterSelectionDate = $"{utcNow.ToShortDateString()} {utcNow.ToShortTimeString()}";
        
        fromPlayer.Entity.WatchedAttributes.MarkPathDirty("skinConfig");
        fromPlayer.BroadcastPlayerData(true);
    }

    public Loadout LoadPrevLoadout(IServerPlayer byPlayer)
    {
        EntityBehaviorExtraSkinnable behavior = byPlayer.Entity.GetBehavior<EntityBehaviorExtraSkinnable>();
        if (!prevLoadouts.ContainsKey(byPlayer.PlayerUID))
        {
            _sapi.SendMessage(byPlayer, 0, "Previous loadout not found", EnumChatType.CommandError);
            return null;
        }
        return prevLoadouts[byPlayer.PlayerUID];
    }

    public string GetLoadoutDataPath(IPlayer player)
    {
        ICoreAPI api = player.Entity.Api;
        string uidFixed = Regex.Replace(player.PlayerUID, "[^0-9a-zA-Z]", "");
        string localPath = Path.Combine("ModData", api.World.SavegameIdentifier ?? "null", Mod.Info.ModID, uidFixed);
        return api.GetOrCreateDataPath(localPath);
    }

    public string[] GetLoadoutDataFiles(IPlayer player)
    {
        string path = GetLoadoutDataPath(player);
        return Directory
            .GetFiles(path)
            .OrderByDescending(f => new FileInfo(f).CreationTime)
            .ToArray();
    }

    public void GetLoadout(IPlayer player, string loadoutName)
    {
        string path = GetLoadoutDataPath(player);
        string[] files = GetLoadoutDataFiles(player);
        string filename = $"{loadoutName}-{player.PlayerUID}.dat";
        foreach (string file in files)
        {
            if ((file == path + "\\" + filename)) 
            {
                var tree = new TreeAttribute();
                tree.FromBytes(File.ReadAllBytes(file));

                var loadout = new Loadout(player.Entity.Api);
                loadout.Inventories = new Dictionary<string, InventoryBasePlayer>();
                
                string invKey1 = invClassNames[0] + "-" + player.PlayerUID;
                InventoryPlayerBackpacksFix inv1 = new InventoryPlayerBackpacksFix(invKey1, player.PlayerUID, player.Entity.Api);
                
                string invKey2 = invClassNames[1] + "-" + player.PlayerUID;
                InventoryCharacter inv2 = new InventoryCharacter(invKey2, player.PlayerUID, player.Entity.Api);
                
                string invKey3 = invClassNames[2] + "-" + player.PlayerUID;
                InventoryPlayerHotbar inv3 = new InventoryPlayerHotbar(invKey3, player.PlayerUID, player.Entity.Api);
                
                //inv1.LateInitialize(invKey1, player.Entity.Api);
                //inv2.LateInitialize(invKey2, player.Entity.Api);
                //inv3.LateInitialize(invKey3, player.Entity.Api);
                
                loadout.Inventories.Add(invKey1, inv1);
                loadout.Inventories.Add(invKey2, inv2);
                loadout.Inventories.Add(invKey3, inv3);
                
                loadout.FromTreeAttributes(tree);
                ApplyLoadout(loadout, (IServerPlayer)player);
                return;
            }
        }
    }

    public void SaveLoadoutContent(Loadout loadout, IPlayer player, string loadoutName)
    {
        string path = GetLoadoutDataPath(player);
        string[] files = GetLoadoutDataFiles(player);

        for (int i = files.Length - 1; i > MaxLoadoutSavedPerPlayer - 2; i--)
        {
            File.Delete(files[i]);
        }

        var tree = new TreeAttribute();
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() != null)
        {
            loadout.nickName = ((IServerPlayer)player).GetNickname();//Make sure to collect this just in case
        }
        loadout.ToTreeAttributes(tree);

        string name = $"{loadoutName}-{player.PlayerUID}.dat";
        File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
    }
    
    private void SwapOutNameTag(IServerPlayer player)
    {
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() == null) return;
        ModConfig Config = _sapi.ModLoader.GetModSystem<RPProximityChatSystem>().Config;
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

    private void SetNickname(string nickname, IServerPlayer byPlayer)
    {
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() == null)
        {
            _sapi.Logger.Event("RPProximityChatSystem not found");
            return;
        }
        byPlayer.SetNickname(nickname);
        SwapOutNameTag(byPlayer);
    }
}