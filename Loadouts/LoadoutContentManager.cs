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
    private const uint all = 15;
    private const uint character = 1;
    private const uint nickname = 2;
    private const uint inventory = 4;
    private const uint position = 8;
    

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
                string[] arguments = args.RawArgs.PopAll().Split(' ');
                string input =  arguments[0];
                arguments = arguments.Skip(1).ToArray();
                uint mods = CollectMods(arguments);
                
                if (input != string.Empty && mods != 0)
                {
                    string? loadoutName = input;
                    string[] files = GetLoadoutDataFiles(byPlayer);
                    string path = GetLoadoutDataPath(byPlayer);
                    foreach (string file in files)
                    {
                        string fileTruncated = file.Replace("-" + byPlayer.PlayerUID + ".dat", "");
                        fileTruncated = fileTruncated.Replace(path + "\\", "");
                        if (fileTruncated == loadoutName)
                        {
                            return TextCommandResult.Error($"Loadout {loadoutName} already exists, please use /loadout update {loadoutName}");
                        }
                    }
                    string? className = byPlayer.Entity.WatchedAttributes.GetAsString("characterClass");
                    if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                    if (string.IsNullOrEmpty(className)) return TextCommandResult.Error("Failed to collect class");
                    Loadout loadout = CreateLoadout((IServerPlayer)byPlayer, loadoutName, api);
                    SaveLoadoutContent(loadout, byPlayer, loadoutName, mods);
                    return TextCommandResult.Success($"Loadout {loadoutName} has been saved");
                }
                else
                {
                    if (input == string.Empty)
                    {
                        return TextCommandResult.Error("No loadout name provided");
                    }
                    else if (mods == 0)
                    {
                        return TextCommandResult.Error("No data selected, please use \"all\", \"position\", \"character\", \"inventory\", or \"nickname\" to select data to include in the loadout");
                    }
                    else
                    {
                        return TextCommandResult.Error("Unknown error detected");
                    }
                }
            });
        
        var subCommandUpdate = rootCommand.BeginSubCommand("update").WithArgs(api.ChatCommands.Parsers.Unparsed("loadoutName"))
            .HandleWith((args) =>
            {
                var byPlayer = args.Caller.Player;
                
                string[] arguments = args.RawArgs.PopAll().Split(' ');
                string input =  arguments[0];
                arguments = arguments.Skip(1).ToArray();
                uint mods = CollectMods(arguments);
                
                if (input != string.Empty)
                {
                    string? loadoutName = input;
                    string[] files = GetLoadoutDataFiles(byPlayer);
                    string path = GetLoadoutDataPath(byPlayer);
                    bool found = false;
                    int i = 0;
                    foreach (string file in files)
                    {
                        string fileTruncated = file.Replace("-" + byPlayer.PlayerUID + ".dat", "");
                        fileTruncated = fileTruncated.Replace(path + "\\", "");
                        if (fileTruncated == loadoutName)
                        {
                            found = true;
                            break;
                        }

                        i++;
                    }

                    if (found)
                    {
                        if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                        Loadout loadout = CreateLoadout((IServerPlayer)byPlayer, loadoutName, api);
                        UpdateLoadoutContent(loadout, byPlayer, loadoutName, mods);
                        return TextCommandResult.Success($"Loadout {loadoutName} has been updated");
                    }
                    return TextCommandResult.Error($"Loadout {loadoutName} not found");
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
                
                string[] arguments = args.RawArgs.PopAll().Split(' ');
                string input =  arguments[0];
                arguments = arguments.Skip(1).ToArray();
                uint mods = CollectMods(arguments);
                if (mods == 0) mods = 15; //Assume user wants to load everything by default
                if (input != string.Empty)
                {
                    string? loadoutName = input;
                    if (string.IsNullOrEmpty(loadoutName)) return TextCommandResult.Error("No loadout name provided");
                    GetLoadout((IServerPlayer)byPlayer, loadoutName, mods);
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
                ApplyLoadout(loadout, (IServerPlayer)byPlayer, 15);//Load everything from prev
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

    private uint CollectMods(string[] arguments)
    {
        uint mods = 0;
        if (arguments.Contains("all"))//not super optimal, but it doesn't really need to be
        {
            mods = mods | all;
            
            if (arguments.Contains("-character") || arguments.Contains("-char"))
            {
                mods = mods & ~character;
            }
            if (arguments.Contains("-nickname") || arguments.Contains("-nick") || arguments.Contains("-name"))
            {
                mods = mods & ~nickname;
            }
            if (arguments.Contains("-inventory") || arguments.Contains("-inv") || arguments.Contains("-bags"))
            {
                mods = mods & ~inventory;
            }
            if (arguments.Contains("-position") || arguments.Contains("-pos") || arguments.Contains("-location") || arguments.Contains("-loc"))
            {
                mods = mods & ~position;
            }
        }
        else
        {
            if (arguments.Contains("character") || arguments.Contains("char"))
            {
                mods = mods | character;
            }
            if (arguments.Contains("nickname") || arguments.Contains("nick") || arguments.Contains("name"))
            {
                mods = mods | nickname;
            }
            if (arguments.Contains("inventory") || arguments.Contains("inv") || arguments.Contains("bags"))
            {
                mods = mods | inventory;
            }
            if (arguments.Contains("position") || arguments.Contains("pos") || arguments.Contains("location") || arguments.Contains("loc"))
            {
                mods = mods | position;
            }
        }

        return mods;
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
        return loadout;
    }

    public void ApplyLoadout(Loadout loadout, IServerPlayer byPlayer, uint mods)
    {
        prevLoadouts[byPlayer.PlayerUID] = CreateLoadout(byPlayer, byPlayer.PlayerUID, byPlayer.Entity.Api);
        
        if (loadout.entityPos != null && (mods & position) == position)
        {
            //byPlayer.Entity.Pos.SetFrom(loadout.entityPos);
            byPlayer.Entity.TeleportTo(loadout.entityPos);
        }
        if ((mods & character) == character) CharacterUpdate(byPlayer, loadout.packet);
        if ((mods & inventory) == inventory) loadout.GiveInventoryCopy(byPlayer);
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() != null && (mods & nickname) == nickname)
        {
            byPlayer.SetNickname(loadout.nickName);
            SwapOutNameTag(byPlayer);
        }
    }
    
    private void CharacterUpdate(IServerPlayer fromPlayer, CharacterSelectionPacket p)
    {
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

    public void GetLoadout(IPlayer player, string loadoutName, uint mods)
    {
        Loadout loadout = LoadLoadout(player, loadoutName, mods);
        if (loadout != null)
        {
            ApplyLoadout(loadout, (IServerPlayer)player, mods);
            _sapi.SendMessage(player, 0, "successfully loaded loadout named: " + loadoutName , EnumChatType.CommandSuccess);
        }
        else
        {
            _sapi.SendMessage(player, 0, "Failed to find loadout named: " + loadoutName , EnumChatType.CommandError);
        }
    }
    
    public Loadout LoadLoadout(IPlayer player, string loadoutName, uint mods)
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

                loadout.entityPos = player.Entity.Pos;//set to avoid any null exceptions
                
                loadout.FromTreeAttributes(tree, mods);
                
                return loadout;
            }
        }
        return null;
    }

    public void SaveLoadoutContent(Loadout loadout, IPlayer player, string loadoutName, uint mods)
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
        loadout.ToTreeAttributes(tree, mods);

        string name = $"{loadoutName}-{player.PlayerUID}.dat";
        File.WriteAllBytes($"{path}/{name}", tree.ToBytes());
    }
    
    public void UpdateLoadoutContent(Loadout loadout, IPlayer player, string loadoutName, uint mods)
    {
        string path = GetLoadoutDataPath(player);
        
        var tree = new TreeAttribute();
        if (_sapi.ModLoader.GetModSystem<RPProximityChatSystem>() != null)
        {
            loadout.nickName = ((IServerPlayer)player).GetNickname();//Make sure to collect this
        }
        Loadout originalLoadout = LoadLoadout(player, loadoutName, 15);
        //Consolidate the original and the new based on the mods, checking that modMask for the update does not include a trait AND that the original does to instead use that
        
        if ((mods & position) != position && (originalLoadout.mask & position) == position) loadout.entityPos = originalLoadout.entityPos;
        if ((mods & character) != character && (originalLoadout.mask & character) == character) loadout.packet = originalLoadout.packet;
        if ((mods & inventory) != inventory && (originalLoadout.mask & inventory) == inventory) loadout.Inventories = originalLoadout.Inventories;
        if ((mods & nickname) != nickname && (originalLoadout.mask & nickname) == nickname) loadout.nickName = originalLoadout.nickName;
        //Define a new mask that contains everything remaining from the original and everything new from the update
        //TODO: fix limitation, can't remove components from a loadout currently
        uint newModMask = mods | originalLoadout.mask;
        loadout.ToTreeAttributes(tree, newModMask);

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