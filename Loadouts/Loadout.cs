using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using thebasics.Extensions;
using thebasics.ModSystems.ProximityChat;
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
    public Loadout(string loadoutName, IServerPlayer byPlayer, ICoreAPI api)
    {
        this.api = api;
        if (api.ModLoader.GetModSystem<RPProximityChatSystem>() != null)
        {
            nickName = byPlayer.GetNickname();//this only works if the basics is installed, so just check that it's there first
        }
        
        api.Logger.Event("Nickname: " + nickName);
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
        api.Logger.Event("Class Code: " + classCode);
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
            //string newInvKey = loadoutName + "-" + invKey;
            
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
                    //slot.Itemstack.Collectible.Attributes = api.Assets.;
                    player.InventoryManager.Inventories[kvp.Key][i].MarkDirty();
                }
                else if (kvp.Value is InventoryPlayerBackpacks)
                {
                    ItemStack? stack = ((InventoryPlayerBackpacks)kvp.Value)[i].Itemstack;
                    if (stack != null)
                    {
                        if (((InventoryPlayerBackpacksFix)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack !=
                            null)
                        {
                            ((InventoryPlayerBackpacksFix)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack.SetFrom(stack);
                        }
                        else
                        {
                            ((InventoryPlayerBackpacksFix)player.InventoryManager.Inventories[kvp.Key])[i].Itemstack = stack.Clone();
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
    /*
    public void InventoryCharacterFromTreeAttributes(ITreeAttribute tree)
    {
        this.slots = this.SlotsFromTreeAttributes(tree);
        if (this.slots.Length == 10)
        {
            ItemSlot[] slots = this.slots;
            this.slots = this.GenEmptySlots(12);
            for (int index = 0; index < slots.Length; ++index)
                this.slots[index] = slots[index];
        }
        if (this.slots.Length != 12)
            return;
        ItemSlot[] slots1 = this.slots;
        this.slots = this.GenEmptySlots(15);
        for (int index = 0; index < slots1.Length; ++index)
            this.slots[index] = slots1[index];
    }
    
    public void InventoryCharacterToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
        this.ResolveBlocksOrItems();
    }
    
    public virtual ItemSlot[] SlotsFromTreeAttributes(
    ITreeAttribute tree,
    ItemSlot[] slots = null,
    List<ItemSlot> modifiedSlots = null)
  {
    if (tree == null)
      return slots;
    if (slots == null)
      slots = this.GenEmptySlots(tree.GetInt("qslots"));
    for (int index = 0; index < slots.Length; ++index)
    {
      ItemStack itemstack1 = tree.GetTreeAttribute(nameof (slots))?.GetItemstack(index.ToString() ?? "");
      slots[index].Itemstack = itemstack1;
      if (this.Api?.World != null)
      {
        itemstack1?.ResolveBlockOrItem(this.Api.World);
        if (modifiedSlots != null)
        {
          ItemStack itemstack2 = slots[index].Itemstack;
          if (((itemstack1 == null ? 0 : (!itemstack1.Equals(this.Api.World, itemstack2, Array.Empty<string>()) ? 1 : 0)) | (itemstack2 == null ? (false ? 1 : 0) : (!itemstack2.Equals(this.Api.World, itemstack1, Array.Empty<string>()) ? 1 : 0))) != 0)
            modifiedSlots.Add(slots[index]);
        }
      }
    }
    return slots;
  }
    */
    
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
                invtree.SetInt("slotCount" + kvp.Key, ((InventoryPlayerBackpacksFix)kvp.Value).Count);
                for (int i = 0; i < ((InventoryPlayerBackpacksFix)kvp.Value).Count; i++)
                {
                    ItemStack? stack = ((InventoryPlayerBackpacksFix)kvp.Value)[i].Itemstack;
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
                            api.Logger.Event("(FromTreeAttributes) slot itemstack: " + ((InventoryPlayerBackpacksFix)kvp.Value)[index].Itemstack.Id);
                            if (this.api?.World != null)
                            {
                                ((InventoryPlayerBackpacksFix)kvp.Value)[index].Itemstack.ResolveBlockOrItem(this.api.World);
                            }
                        }
                        else
                        {
                            api.Logger.Event("(FromTreeAttributes) slot is null");
                        }
                    }
                    /*
                    for (int index = 0; index < backpackSlots; ++index)
                    {
                        ItemStack itemstack1 = invtree.GetItemstack(kvp.Key + "-bag-" + index);
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
                    */
                }
                else
                {
                    api.Logger.Event("Slot count is null or 0");
                }
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