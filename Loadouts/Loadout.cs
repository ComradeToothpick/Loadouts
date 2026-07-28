using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using thebasics.Extensions;
using thebasics.ModSystems.ProximityChat;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Loadouts;

public struct SurvivalPlayerData
{
    public float health;
    public float satiety;
    public float hydration;
}

public struct NutritionData
{
    public float fruit;
    public float vegetable;
    public float grain;
    public float protein;
    public float dairy;
}

public struct Ailments
{
    public float psychadelics;
    public float intoxication;
    public float temporalStability;
}

//EntityBehaviorHealth behavior = byPlayer.Entity.GetBehavior<EntityBehaviorHealth>();
//EntityBehaviorHunger behavior2 = byPlayer.Entity.GetBehavior<EntityBehaviorHunger>();
//EntityBehaviorTemporalStabilityAffected behavior3 = byPlayer.Entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();

public class Loadout
{
    public Dictionary<string, InventoryBasePlayer> Inventories { get; set; }
    public uint mask = 15;
    public string OwnerUID;
    public string loadoutName;
    public EntityPos entityPos;
    public CharacterSelectionPacket packet;
    public string nickName;
    public EnumGameMode gameMode;
    public SurvivalPlayerData survivalPlayerData;
    public NutritionData nutritionData;
    
    private List<ClothStack> clothes = new();
    private int count = 0;
    private Dictionary<string, string> skinParts = new();
    private ICoreAPI api;
    private const uint all = 15;
    private const uint character = 1;
    private const uint nickname = 2;
    private const uint inventory = 4;
    private const uint position = 8;

    public Loadout(ICoreAPI api)
    {
        this.api = api;
    }
    public Loadout(string loadoutName, IServerPlayer byPlayer, ICoreAPI api, uint mods = 0)
    {
        this.api = api;
        this.entityPos = byPlayer.Entity.Pos;
        if (LoadoutContentManager.theBasicsLoaded && (mods & nickname) == nickname)
        {
            nickName = byPlayer.GetNickname();
        }
        
        clothes.Clear();
        count = 0;
        byPlayer.InventoryManager.Inventories["character-" + byPlayer.PlayerUID].Foreach(slot =>
        {
            if (!slot.Empty) clothes.Add(new()
            {
                Code = slot.Itemstack.Collectible.Code.ToShortString(),
                SlotNum = count,
                Class = slot.Itemstack.Class
            });
            count++;
        });
        EntityBehaviorExtraSkinnable behavior = byPlayer.Entity.GetBehavior<EntityBehaviorExtraSkinnable>();
        foreach (AppliedSkinnablePartVariant appliedSkinPart in (IEnumerable<AppliedSkinnablePartVariant>) behavior.AppliedSkinParts)
            skinParts[appliedSkinPart.PartCode] = appliedSkinPart.Code;
        string classCode = byPlayer.Entity.WatchedAttributes.GetAsString("characterClass", "commoner");
        //api.Logger.Event("Class Code: " + classCode);
        packet = new()
        {
            DidSelect = true,
            Clothes = clothes.ToArray(),
            CharacterClass = classCode,
            SkinParts = skinParts,
            VoiceType = behavior.VoiceType,
            VoicePitch = behavior.VoicePitch
        };
        //this.system = api.ModLoader.GetModSystem<CharacterSystem>();
        this.loadoutName = loadoutName;
        this.OwnerUID = byPlayer.PlayerUID;
        Inventories = new();
        int count2 = 0;
        foreach (string invName in LoadoutContentManager.invClassNames)
        {
            string invKey = invName + "-" + byPlayer.PlayerUID;
            
            if (!byPlayer.InventoryManager.Inventories.ContainsKey(invKey))
            {
                continue;
            }
            if (byPlayer.InventoryManager.Inventories[invKey].Count == 0) continue;
            InventoryBasePlayer inv;
            if (byPlayer.InventoryManager.Inventories[invKey] is InventoryPlayerHotbar)
            {
                inv = new InventoryPlayerHotbar(invKey, byPlayer.PlayerUID, api);
                count = 0;
                inv.Foreach(slot =>
                {
                    ItemStack stack = byPlayer.InventoryManager.Inventories[invKey].ElementAt(count++).Itemstack;
                    if (stack != null) slot.Itemstack = stack.Clone();
                });
                Inventories.Add(invKey, inv);
            }
            else if (byPlayer.InventoryManager.Inventories[invKey] is InventoryPlayerBackpacks)
            {
                inv = new InventoryPlayerBackpacksFix(invKey, byPlayer.PlayerUID, api);
                count = 0;
                inv.Foreach(slot =>
                {
                    ItemStack stack = byPlayer.InventoryManager.Inventories[invKey].ElementAt(count++).Itemstack;
                    if (stack != null) slot.Itemstack = stack.Clone();
                });
                Inventories.Add(invKey, inv);
            }
            else if (byPlayer.InventoryManager.Inventories[invKey] is InventoryCharacter)
            {
                inv = new InventoryCharacter(invKey, byPlayer.PlayerUID, api);
                count = 0;
                for (int i = 0; i < ((InventoryCharacter)byPlayer.InventoryManager.Inventories[invKey]).Count; i++)
                {
                    ItemStack stack = ((ItemSlotCharacter)((InventoryCharacter)byPlayer.InventoryManager.Inventories[invKey])[i]).Itemstack;
                    if (stack != null) ((InventoryCharacter)inv)[i].Itemstack = stack;
                }
                Inventories.Add(invKey, inv);
            }
        }
    }

    public void ApplyLoadoutAppearance()
    {
        
    }
    
    public InventoryBasePlayer GetOwnInventory(string invClassName)
    {
        if (Inventories == null || Inventories.Count == 0 || !Inventories.ContainsKey(invClassName)) return null;
        return Inventories[invClassName];
    }
    
    public void GiveInventoryCopy(IServerPlayer player)
    {
        if (Inventories == null || Inventories.Count == 0 || player == null || player.PlayerUID != OwnerUID) return;
        player.InventoryManager.DiscardAll();
        foreach (KeyValuePair<string, InventoryBasePlayer> kvp in Inventories)
        {
            if (player.InventoryManager.Inventories.ContainsKey(kvp.Key))
            {
                player.InventoryManager.Inventories[kvp.Key] = kvp.Value;
            }
            else player.InventoryManager.Inventories.Add(kvp.Key, kvp.Value);
            
            int playerInvCount = player.InventoryManager.Inventories[kvp.Key].Count;
            int newInvCount = Inventories[kvp.Key].Count;
            if (playerInvCount < newInvCount)
            {
                if (kvp.Value is InventoryCharacter)
                {
                    ((InventoryCharacter)player.InventoryManager.Inventories[kvp.Key])
                        .GenEmptySlots(newInvCount - playerInvCount);
                }
                else
                {
                    ((InventoryBasePlayer)player.InventoryManager.Inventories[kvp.Key])
                        .GenEmptySlots(newInvCount - playerInvCount);
                }
            }
            
            for (int i = 0; i < kvp.Value.Count; i++)
            {
                if (kvp.Value is InventoryCharacter)
                {
                    ItemStack? stack = ((ItemSlotCharacter)((InventoryCharacter)kvp.Value)[i]).Itemstack;
                    if (stack != null)
                    {
                        if (((ItemSlotCharacter)((InventoryCharacter)player.InventoryManager.Inventories[kvp.Key])[i])
                            .Itemstack != null)
                        {
                            ((ItemSlotCharacter)((InventoryCharacter)player.InventoryManager.Inventories[kvp.Key])[i]).Itemstack.SetFrom(stack);
                        }
                        else
                        {
                            ((ItemSlotCharacter)((InventoryCharacter)player.InventoryManager.Inventories[kvp.Key])[i]).Itemstack = stack.Clone();
                        }
                    }
                    else
                    {
                        if (player.InventoryManager.Inventories[kvp.Key][i] != null && player.InventoryManager.Inventories[kvp.Key][i].Itemstack != null)
                        {
                            player.InventoryManager.Inventories[kvp.Key][i].TakeOutWhole();
                        }
                    }
                    player.InventoryManager.Inventories[kvp.Key][i].MarkDirty();
                }
                else if (kvp.Value is InventoryPlayerBackpacks)
                {
                    ItemStack? stack = ((InventoryPlayerBackpacks)kvp.Value)[i].Itemstack;
                    if (stack != null)
                    {
                        if (((InventoryPlayerBackpacks)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack !=
                            null)
                        {
                            ((InventoryPlayerBackpacks)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack.SetFrom(stack);
                        }
                        else
                        {
                            ((InventoryPlayerBackpacks)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack = stack.Clone();
                        }
                    }
                    else
                    {
                        if (player.InventoryManager.Inventories[kvp.Key][i] != null && player.InventoryManager.Inventories[kvp.Key][i].Itemstack != null)
                        {
                            player.InventoryManager.Inventories[kvp.Key][i].TakeOutWhole();
                        }
                    }
                    player.InventoryManager.Inventories[kvp.Key][i].MarkDirty();
                }
                else
                {
                    ItemStack? stack = kvp.Value[i].Itemstack;
                    if (stack != null)
                    {
                        if (player.InventoryManager.Inventories[kvp.Key][i].Itemstack != null)
                        {
                            player.InventoryManager.Inventories[kvp.Key][i].Itemstack.SetFrom(stack);
                        }
                        else
                        {
                            player.InventoryManager.Inventories[kvp.Key][i].Itemstack = stack;
                        }
                    }
                    else
                    {
                        if (player.InventoryManager.Inventories[kvp.Key][i] != null && player.InventoryManager.Inventories[kvp.Key][i].Itemstack != null)
                        {
                            player.InventoryManager.Inventories[kvp.Key][i].TakeOutWhole();
                        }
                    }
                    player.InventoryManager.Inventories[kvp.Key][i].MarkDirty();
                }
            }
        }
    }
    
    public void SlotsToTreeAttributes(ItemSlot[] slots, ITreeAttribute tree)
    {
        tree.SetInt("qslots", slots.Length);
        TreeAttribute treeAttribute = new TreeAttribute();
        for (int index = 0; index < slots.Length; ++index)
        {
            if (slots[index].Itemstack != null)
                treeAttribute.SetItemstack(index.ToString() ?? "", slots[index].Itemstack.Clone());
        }
        tree[nameof (slots)] = (IAttribute) treeAttribute;
    }
    
    public void ToTreeAttributes(ITreeAttribute invtree, uint mods)
    {
        invtree.SetInt("modMask", (int)mods);//Keep track of the modMask to prevent attempting to load data that was never saved.
        invtree.SetString("loadoutName", loadoutName);
        invtree.SetString("ownerUID", OwnerUID);
        //Only saving data that is requested to be saved
        if (LoadoutContentManager.theBasicsLoaded && (mods & nickname) == nickname)
        {
            invtree.SetString("nickName", nickName);//nickname
        }
        
        if ((mods & character) == character) invtree.SetBytes("packet", SerializerUtil.Serialize(packet));//character
        
        if ((mods & position) == position) invtree.SetBytes("entityPos", SerializerUtil.Serialize(entityPos));//position
        
        if ((mods & inventory) == inventory) foreach (KeyValuePair<string, InventoryBasePlayer> kvp in Inventories)//inventory
        {
            if (kvp.Value is InventoryPlayerCreative) continue;
            if (kvp.Value is InventoryPlayerBackpacks)
            {
                invtree.SetInt("slotCount" + kvp.Key, ((InventoryPlayerBackpacks)kvp.Value).Count);
                for (int i = 0; i < ((InventoryPlayerBackpacks)kvp.Value).Count; i++)
                {
                    ItemStack? stack = ((InventoryPlayerBackpacks)kvp.Value)[i].Itemstack;
                    if (stack != null)
                    {
                        invtree.SetItemstack(kvp.Key + "-" + i, stack);
                    }
                }
            }
            else
            {
                invtree.SetInt("slotCount" + kvp.Key, kvp.Value.Count);
                for (int i = 0; i < kvp.Value.Count; ++i)
                {
                    ItemStack? stack = kvp.Value[i].Itemstack;
                    if (stack != null)
                    {
                        invtree.SetItemstack(kvp.Key + "-" + i, stack);
                    }
                }
            }
        }
    }
    
    public void FromTreeAttributes(ITreeAttribute invtree, uint mods)
    {
        mask = (uint)invtree.GetInt("modMask");
        mask = (mask & mods);
        loadoutName = invtree.GetString("loadoutName");
        OwnerUID = invtree.GetString("ownerUID");
        if (LoadoutContentManager.theBasicsLoaded && (mask & nickname) == nickname)
        {
            nickName = invtree.GetString("nickName");
        }
        
        if ((mask & character) == character)packet = SerializerUtil.Deserialize<CharacterSelectionPacket>(invtree.GetBytes("packet"));
        
        if ((mask & position) == position) entityPos = SerializerUtil.Deserialize<EntityPos>(invtree.GetBytes("entityPos"));
        
        if ((mask & inventory) == inventory) foreach (KeyValuePair<string, InventoryBasePlayer> kvp in Inventories)
        {
            if (kvp.Value is InventoryPlayerBackpacks)
            {
                int slotCount = invtree.GetInt("slotCount" + kvp.Key);
                //int backpackSlots = invtree.GetInt("bagSlotCount" + kvp.Key);
                //if (backpackSlots == 0) continue;
                
                if (slotCount != null && slotCount > 0)
                {
                    ((InventoryPlayerBackpacksFix)kvp.Value).GenEmptySlots(slotCount);
                    for (int index = 0; index < ((InventoryPlayerBackpacksFix)kvp.Value).Count; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetItemstack(kvp.Key + "-" + index);
                        if (itemstack1 != null)
                        {
                            ((InventoryPlayerBackpacksFix)kvp.Value)[index].Itemstack = itemstack1;
                            //.Event("(FromTreeAttributes) slot itemstack: " + ((InventoryPlayerBackpacksFix)kvp.Value)[index].Itemstack.Id);
                            if (this.api?.World != null)
                            {
                                ((InventoryPlayerBackpacksFix)kvp.Value)[index].Itemstack.ResolveBlockOrItem(this.api.World);
                            }
                        }
                        //else
                        //{
                            //api.Logger.Event("(FromTreeAttributes) slot is null");
                        //}
                    }
                }
                //else
                //{
                    //api.Logger.Event("Slot count is null or 0");
                //}
            }
            else if (kvp.Value is InventoryCharacter)
            {
                int slotCount = invtree.GetInt("slotCount" + kvp.Key);
                if (slotCount != null && slotCount > 0)
                {
                    ((InventoryCharacter)kvp.Value).GenEmptySlots(slotCount);
                    for (int index = 0; index < kvp.Value.Count; ++index)
                    {
                        ItemStack itemstack = invtree.GetItemstack(kvp.Key + "-" + index);
                        if (itemstack != null)
                        {
                            ((InventoryCharacter)kvp.Value)[index].Itemstack = null;
                        }
                        ((InventoryCharacter)kvp.Value)[index].Itemstack = itemstack;
                        if (api.World != null && ((InventoryCharacter)kvp.Value)[index].Itemstack != null)
                        {
                            ((InventoryCharacter)kvp.Value)[index].Itemstack!.ResolveBlockOrItem(api.World);
                        }
                    }
                }
            }
            else
            {
                int slotCount = invtree.GetInt("slotCount" + kvp.Key);
                if (slotCount != null && slotCount > 0)
                {
                    kvp.Value.GenEmptySlots(slotCount);
                    for (int index = 0; index < kvp.Value.Count; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetItemstack(kvp.Key + "-" + index);
                        if (itemstack1 != null)
                        {
                            kvp.Value[index].Itemstack = itemstack1;
                        }
                    
                        if (this.api?.World != null)
                        {
                            itemstack1?.ResolveBlockOrItem(this.api.World);
                        }
                    }
                }
            }
        }
    }
}