using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Loadouts;

public class Loadout
{
    public Dictionary<string, InventoryBasePlayer> Inventories { get; set; }
    public string OwnerUID;
    public string loadoutName;
    public CharacterSelectionPacket packet;
    private List<ClothStack> clothes = new();
    private int count = 0;
    private Dictionary<string, string> skinParts = new();
    public string nickName;
    private ICoreAPI api;

    public Loadout(ICoreAPI api)
    {
        this.api = api;
    }
    public Loadout(string loadoutName, string characterClass, IServerPlayer byPlayer, ICoreAPI api)
    {
        this.api = api;
        nickName = SerializerUtil.Deserialize<string>(byPlayer.GetModdata("BASIC_NICKNAME"), byPlayer.PlayerName);
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
        packet = new()
        {
            DidSelect = false,
            Clothes = clothes.ToArray(),
            CharacterClass = characterClass,
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
            string newInvKey = loadoutName + "-" + invKey;
            
            if (!byPlayer.InventoryManager.Inventories.ContainsKey(invKey))
            {
                continue;
            }
            if (byPlayer.InventoryManager.Inventories[invKey].Count == 0) continue;
            InventoryBasePlayer inv;
            if (byPlayer.InventoryManager.Inventories[invKey] is InventoryPlayerHotbar)
            {
                inv = new InventoryPlayerHotbar(newInvKey, byPlayer.PlayerUID, api);
            }
            else if (byPlayer.InventoryManager.Inventories[invKey] is InventoryPlayerBackpacks)
            {
                inv = new InventoryPlayerBackpacks(newInvKey, byPlayer.PlayerUID, api);
            }
            else if (byPlayer.InventoryManager.Inventories[invKey] is InventoryCharacter)
            {
                inv = new InventoryCharacter(newInvKey, byPlayer.PlayerUID, api);
            }
            else
            {
                continue;
            }
            inv.LateInitialize(newInvKey, api);
            count = 0;
            inv.Foreach(slot =>
            {
                ItemStack stack = byPlayer.InventoryManager.Inventories[invKey].ElementAt(count++).Itemstack;
                if (stack != null) slot.Itemstack = stack.Clone();
            });
            Inventories.Add(invName, inv);
        }
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

            int i = 0;
            int playerInvCount = player.InventoryManager.Inventories[kvp.Key].Count;
            int newInvCount = Inventories[kvp.Key].Count;
            if (playerInvCount < newInvCount)
            {
                ((InventoryBasePlayer)player.InventoryManager.Inventories[kvp.Key])
                    .GenEmptySlots(newInvCount - playerInvCount);
            }
            player.InventoryManager.Inventories[kvp.Key].Foreach(slot =>
            {
                ItemStack? stack = kvp.Value[i++].Itemstack;
                if (stack != null)
                {
                    slot.Itemstack = stack;
                    api.Logger.Event("slot itemstack: " + slot.Itemstack.Id);
                }
                //slot.Itemstack.Collectible.Attributes = api.Assets.;
                slot.MarkDirty();
            });
        }
    }
    //public Dictionary<string, InventoryBasePlayer> Inventories { get; set; }
    //public string OwnerUID;
    //public string loadoutName;
    //public CharacterSelectionPacket packet;
    //private List<ClothStack> clothes = new List<ClothStack>();
    //private int count = 0;
    //private Dictionary<string, string> skinParts = new Dictionary<string, string>();
    //public string nickName;
    
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
    
    
    public void ToTreeAttributes(ITreeAttribute invtree)
    {
        invtree.SetBytes("loadoutName", SerializerUtil.Serialize(loadoutName));
        invtree.SetBytes("ownerUID", SerializerUtil.Serialize(OwnerUID));
        invtree.SetBytes("nickName", SerializerUtil.Serialize(nickName));
        invtree.SetBytes("packet", SerializerUtil.Serialize(packet));
        foreach (KeyValuePair<string, InventoryBasePlayer> kvp in Inventories)
        {
            if (kvp.Value is InventoryPlayerCreative) continue;
            if (kvp.Value is InventoryPlayerBackpacks)
            {
                invtree.SetInt("slotCount" + kvp.Key, kvp.Value.Count);
                invtree.SetInt("bagSlotCount" + kvp.Key, ((InventoryPlayerBackpacks)kvp.Value).bagInv.Count);
                if (((InventoryPlayerBackpacks)kvp.Value).bagInv.Count == 0) continue;
                for (int i = 0; i < kvp.Value.Count; ++i)
                {
                    ItemStack? stack = kvp.Value[i].Itemstack;
                    if (stack != null)
                    {
                        invtree.SetItemstack(kvp.Key + "-" + i, stack);
                    }
                }

                for (int i = 0; i < ((InventoryPlayerBackpacks)kvp.Value).bagInv.Count; ++i)
                {
                    ItemStack? stack = ((InventoryPlayerBackpacks)kvp.Value).bagInv[i].Itemstack;
                    if (stack != null)
                    {
                        invtree.SetItemstack(kvp.Key + "-bag-" + i, stack);
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
    
    public void FromTreeAttributes(ITreeAttribute invtree)
    {
        loadoutName = SerializerUtil.Deserialize<string>(invtree.GetBytes("loadoutName"));
        OwnerUID = SerializerUtil.Deserialize<string>(invtree.GetBytes("ownerUID"));
        nickName = SerializerUtil.Deserialize<string>(invtree.GetBytes("nickName"));
        packet = SerializerUtil.Deserialize<CharacterSelectionPacket>(invtree.GetBytes("packet"));
        foreach (KeyValuePair<string, InventoryBasePlayer> kvp in Inventories)
        {
            if (kvp.Value is InventoryPlayerBackpacks)
            {
                int slotCount = invtree.GetInt("slotCount" + kvp.Key);
                int backpackSlots = invtree.GetInt("bagSlotCount" + kvp.Key);
                if (backpackSlots == 0) continue;
                
                if (slotCount != null && slotCount > 0)
                {
                    ItemSlot[] slots = kvp.Value.GenEmptySlots(slotCount);
                    for (int index = 0; index < slots.Length; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetTreeAttribute(nameof (slots))?.GetItemstack(kvp.Key + "-" + index);
                        if (itemstack1 != null)
                        {
                            slots[index].Itemstack = itemstack1;
                            api.Logger.Event("(FromTreeAttributes) slot itemstack: " + slots[index].Itemstack.Id);
                        }
                        else
                        {
                            api.Logger.Event("(FromTreeAttributes) slot is null");
                        }
                    
                        if (this.api?.World != null)
                        {
                            itemstack1?.ResolveBlockOrItem(this.api.World);
                        }
                    }

                    for (int index = 0; index < backpackSlots; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetTreeAttribute(nameof (slots))?.GetItemstack(kvp.Key + "-bag-" + index);
                        if (itemstack1 != null)
                        {
                            slots[index].Itemstack = itemstack1;
                            api.Logger.Event("(FromTreeAttributes) slot itemstack: " + slots[index].Itemstack.Id);
                        }
                        else
                        {
                            api.Logger.Event("(FromTreeAttributes) slot is null");
                        }
                    
                        if (this.api?.World != null)
                        {
                            itemstack1?.ResolveBlockOrItem(this.api.World);
                        }
                    }
                }
                else
                {
                    api.Logger.Event("Slot count is null or 0");
                }
            }
            else
            {
                int slotCount = invtree.GetInt("slotCount" + kvp.Key);
                if (slotCount != null && slotCount > 0)
                {
                    ItemSlot[] slots = kvp.Value.GenEmptySlots(slotCount);
                    for (int index = 0; index < slots.Length; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetTreeAttribute(nameof (slots))?.GetItemstack(kvp.Key + "-" + index);
                        if (itemstack1 != null)
                        {
                            slots[index].Itemstack = itemstack1;
                            api.Logger.Event("(FromTreeAttributes) slot itemstack: " + slots[index].Itemstack.Id);
                        }
                        else
                        {
                            api.Logger.Event("(FromTreeAttributes) slot is null");
                        }
                    
                        if (this.api?.World != null)
                        {
                            itemstack1?.ResolveBlockOrItem(this.api.World);
                        }
                    }
                }
                else
                {
                    api.Logger.Event("Slot count is null or 0");
                }
            }
        }
    }
}