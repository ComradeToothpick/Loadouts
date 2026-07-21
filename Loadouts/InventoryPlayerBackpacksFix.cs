using System;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Loadouts;

public class InventoryPlayerBackpacksFix : InventoryPlayerBackpacks
{
    public InventoryPlayerBackpacksFix(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
    }

    public InventoryPlayerBackpacksFix(string inventoryId, ICoreAPI api) : base(inventoryId, api)
    {
    }
    
    public InventoryPlayerBackpacksFix(string inventoryId, ICoreAPI api, InventoryPlayerBackpacks inv) : base(inventoryId, api)
    {
        this.bagSlots = this.GenEmptySlots(4);
        this.baseWeight = 1f;
        this.bagInv = inv.bagInv;
    }

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= this.Count)
                return (ItemSlot) null;
            return slotId < this.bagSlots.Length ? this.bagSlots[slotId] : this.bagInv[slotId - this.bagSlots.Length];
        }
        set
        {
            if (slotId < 0 || slotId >= this.Count)
                throw new ArgumentOutOfRangeException(nameof (slotId));
            if (value == null)
                throw new ArgumentNullException(nameof (value));
            if (slotId < this.bagSlots.Length)
                this.bagSlots[slotId] = value;
            else this.bagInv[slotId - this.bagSlots.Length] = value;
        }
    }
}