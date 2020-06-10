using Server.Engines.Craft;
using Server.Mobiles;

namespace Server.Items
{
    public interface IItemWithAosAttributes
    {
        // Note: This does not include weapon and armor attributes, but those
        // are pretty specific, so you should be able to just use something like
        // "if (item is BaseWeapon)
        //  {
        //      var weapon = item as BaseWeapon;
        //      var attrs = weapon.WeaponAttributes;
        //      ...
        //  }"
        // ... to pull those.

        AosAttributes   Attributes   { get; }
        AosSkillBonuses SkillBonuses { get; }
    }
    
    public interface IItemWithAosElementalDamage
    {
        AosElementAttributes AosElementDamages { get; }
    }
    
    public interface IItemWithAosElementalResistance
    {
        AosElementAttributes Resistances { get; }
    }
    
    public interface IItemWithNegativeAttributes
    {
        NegativeAttributes NegativeAttributes { get; }
    }

    public interface IItemWithSAAbsorptionAttributes
    {
        SAAbsorptionAttributes AbsorptionAttributes { get; }
    }

    public interface IUsesRemaining
    {
        int UsesRemaining { get; set; }
        bool ShowUsesRemaining { get; set; }
    }

    public interface IAccountRestricted
    {
        string Account { get; set; }
    }

    public interface IOwnerRestricted
    {
        Mobile Owner { get; set; }
        string OwnerName { get; set; }
    }

    public interface IFlipable
    {
        void OnFlip(Mobile m);
    }

    public interface IQuality : ICraftable
    {
        ItemQuality Quality { get; set; }
        bool PlayerConstructed { get; }
    }

    public interface IResource
    {
        CraftResource Resource { get; set; }
    }

    public interface IConditionalVisibility
    {
        bool CanBeSeenBy(PlayerMobile m);
    }

    public interface IImbuableEquipment
    {
        int TimesImbued { get; set; }
        bool IsImbued { get; set; }

        int[] BaseResists { get; }
        void OnAfterImbued(Mobile m, int mod, int value);
    }

    public interface ICombatEquipment : IImbuableEquipment
    {
        ItemPower ItemPower { get; set; }
        ReforgedPrefix ReforgedPrefix { get; set; }
        ReforgedSuffix ReforgedSuffix { get; set; }
        bool PlayerConstructed { get; set; }
    }

    public enum ItemQuality
    {
        Low,
        Normal,
        Exceptional
    }

    public enum DirectionType
    {
        None = 0,
        South = 1,
        East = 2,
        North = 3,
        West = 4
    }
}
