using Server.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Mobiles
{
    public class GenericSellInfo : IShopSellInfo
    {
        public static int SplurgeRatioThreshold = Config.Get("Vendors.SplurgeRatioThreshold", 5);
        public static int MaxSpendingOnEachItemPerDay = Config.Get("Vendors.MaxSpendingOnEachItemPerDay", 1200);

        protected struct Sale
        {
            public DateTime  TransactionTime;
            public Type      ItemType;
            public int       ItemQuantity;
            public int       GoldPaid;
        }

        private readonly Dictionary<Type, int> m_Table = new Dictionary<Type, int>();
        private Type[]     m_Types;
        private List<Sale> m_SalesLog; // Note: Not serialized. But that's probably OK.

        public Type[] Types
        {
            get
            {
                if (m_Types == null)
                {
                    m_Types = new Type[m_Table.Keys.Count];
                    m_Table.Keys.CopyTo(m_Types, 0);
                }

                return m_Types;
            }
        }

        /// Unsorted.
        protected List<Sale> SalesLog
        {
            get
            {
                if (m_SalesLog == null)
                    m_SalesLog = new List<Sale>();

                return m_SalesLog;
            }
        }

        public void Add(Type type, int price)
        {
            m_Table[type] = price;
            m_Types = null;
        }

        public int GetBaseSellPriceFor(Type type)
        {
            int price = 0;
            m_Table.TryGetValue(type, out price);
            return price;
        }

        public int GetBaseSellPriceFor(Item item)
        {
            return GetBaseSellPriceFor(item.GetType());
        }

        public int GetSellPriceFor(Item item)
        {
            return GetSellPriceFor(item, null);
        }

        // Simple mathematical function used to simplify some
        // pricing calculations.
        // The double-version is more appropriate for fractional
        // gold-pieces and multipliers, while the integer one
        // allows an integer-returning version for clipping
        // integer-valued attributes and such.
        public static double Saturate(double theThing, double min, double max)
        {
            if ( theThing < min )
                return min;
            else
            if ( theThing > max )
                return max;
            else
                return theThing;
        }

        // Ditto
        public static int Saturate(int theThing, int min, int max)
        {
            if ( theThing < min )
                return min;
            else
            if ( theThing > max )
                return max;
            else
                return theThing;
        }

        public struct SellPriceMetadata
        {
            public ItemQuality    Quality;
            public bool           IsImbued;
            public bool           IsAltered;
            public CraftResource  Material;
            public bool           MaterialIsNormal;
            public bool           IsPlayerConstructed;
            public bool           IsEasilyManufactured;
            public bool           IsSpellChanneling;
        }

        public static SellPriceMetadata GetSellPriceMetadata(Item item)
        {
            // ============================================
            // -------------- Initialization --------------
            // Default values are stated further down
            // next to their calculation.
            // These lines mostly exist to stop the C#
            // compiler from complaining about using an
            // unitialized structure.
            //
            SellPriceMetadata meta;
            meta.Quality = ItemQuality.Normal;
            meta.IsImbued = false;
            meta.IsAltered = false;
            meta.Material = CraftResource.None;
            meta.MaterialIsNormal = true;
            meta.IsPlayerConstructed = false;
            meta.IsEasilyManufactured = true;
            meta.IsSpellChanneling = false;

            // ============================================
            // --- Basic quality and sourcing metadata. ---
            meta.IsPlayerConstructed = false;
            if ( item is ICombatEquipment )
            {
                var tacticool = item as ICombatEquipment;
                meta.IsPlayerConstructed |= tacticool.PlayerConstructed;
            }

            meta.Quality = ItemQuality.Normal;
            if (item is IQuality )
            {
                // Dank meme'n: https://knowyourmeme.com/memes/quality
                var QUALITY = item as IQuality;
                meta.Quality = QUALITY.Quality;
                meta.IsPlayerConstructed |= QUALITY.PlayerConstructed;
            }

            meta.IsImbued = false;
            if (item is IImbuableEquipment)
            {
                var imbuable = item as IImbuableEquipment;
                meta.IsImbued = imbuable.IsImbued;
            }

            meta.IsAltered = false;
            if (item is BaseWeapon)
            {
                var weapon = item as BaseWeapon;
                meta.IsAltered = weapon.Altered;
            }
            else
            if (item is BaseArmor)
            {
                var armor = item as BaseArmor;
                meta.IsAltered = armor.Altered;
            }

            // ============================================
            // -------- Resource/Material metadata --------
            meta.Material = CraftResource.None;
            meta.MaterialIsNormal = true; // Default if we don't know about the resource used.
            if (item is IResource)
            {
                var resourceLaden = item as IResource;
                meta.Material = resourceLaden.Resource;
                meta.MaterialIsNormal = false; // Default if we DO know about the resource.
            }

            switch(meta.Material)
            {
                case CraftResource.None:           meta.MaterialIsNormal = true; break;
                case CraftResource.Iron:           meta.MaterialIsNormal = true; break;
                case CraftResource.RegularLeather: meta.MaterialIsNormal = true; break;

                // As I understand it, all scales are normal materials
                // (for making dragon armor).
                // Reference: https://www.uoguide.com/Dragon_Scales
                case CraftResource.RedScales:    meta.MaterialIsNormal = true; break;
                case CraftResource.YellowScales: meta.MaterialIsNormal = true; break;
                case CraftResource.BlackScales:  meta.MaterialIsNormal = true; break;
                case CraftResource.GreenScales:  meta.MaterialIsNormal = true; break;
                case CraftResource.WhiteScales:  meta.MaterialIsNormal = true; break;
                case CraftResource.BlueScales:   meta.MaterialIsNormal = true; break;

                case CraftResource.RegularWood:  meta.MaterialIsNormal = true; break;

                // Dunno.
                default: meta.MaterialIsNormal = true; break;
            }

            // ===========================================
            meta.IsEasilyManufactured = true
                && (meta.IsPlayerConstructed)
                && !meta.IsImbued && !meta.IsAltered
                && meta.MaterialIsNormal;

            // ===========================================
            // -------------- AosAttributes --------------
            if (item is IItemWithAosAttributes)
            {
                var thingy = item as IItemWithAosAttributes;
                var attrs  = thingy.Attributes;

                if ( attrs.SpellChanneling != 0 )
                    meta.IsSpellChanneling = true;
            }

            // ============================================
            // ------------------- Done -------------------
            return meta;
        }

        public PricingCoefficients GetPricingCoefficients(Item item)
        {
            return GetPricingCoefficients(item, GetSellPriceMetadata(item));
        }

        public PricingCoefficients GetPricingCoefficients(Item item, SellPriceMetadata meta)
        {
            // ===========================================
            // -------- Resource/Material pricing --------
            double materialMultiplier = 1.00;
            switch(meta.Material)
            {
                case CraftResource.None: break;

                case CraftResource.Iron:       break;
                case CraftResource.DullCopper: materialMultiplier = 1.05; break;
                case CraftResource.ShadowIron: materialMultiplier = 1.10; break;
                case CraftResource.Copper:     materialMultiplier = 1.15; break;
                case CraftResource.Bronze:     materialMultiplier = 1.20; break;
                case CraftResource.Gold:       materialMultiplier = 1.30; break;
                case CraftResource.Agapite:    materialMultiplier = 1.40; break;
                case CraftResource.Verite:     materialMultiplier = 1.60; break;
                case CraftResource.Valorite:   materialMultiplier = 2.00; break;

                case CraftResource.RegularLeather: break;
                case CraftResource.SpinedLeather:  materialMultiplier = 1.20; break;
                case CraftResource.HornedLeather:  materialMultiplier = 1.30; break;
                case CraftResource.BarbedLeather:  materialMultiplier = 1.40; break;

                // As I understand it, all scales are normal materials
                // (for making dragon armor).
                // Reference: https://www.uoguide.com/Dragon_Scales
                case CraftResource.RedScales:    break;
                case CraftResource.YellowScales: break;
                case CraftResource.BlackScales:  break;
                case CraftResource.GreenScales:  break;
                case CraftResource.WhiteScales:  break;
                case CraftResource.BlueScales:   break;

                // Reference: https://uo.com/wiki/ultima-online-wiki/items/material-bonuses/
                case CraftResource.RegularWood: break;
                case CraftResource.OakWood:     materialMultiplier = 1.25; break;
                case CraftResource.AshWood:     materialMultiplier = 1.15; break;
                case CraftResource.YewWood:     materialMultiplier = 1.15; break;
                case CraftResource.Heartwood:   materialMultiplier = 1.30; break; // These can be good, but it depends on attributes. Calculate separately.
                case CraftResource.Bloodwood:   materialMultiplier = 1.40; break;
                case CraftResource.Frostwood:   materialMultiplier = 1.50; break; // Guaranteed spell channelling? YES.

                // Dunno.
                default: break;
            }

            // ==========================================
            // -------------- Item Quality --------------
            double qualityMultiplier = 1.00; // 1.00 == 100% == no change
            if (meta.Quality == ItemQuality.Low)
                qualityMultiplier = 0.60;
            else if (meta.Quality == ItemQuality.Exceptional)
                qualityMultiplier = 1.25;

            // ===========================================
            // -------------- Miscellaneous --------------

            double miscMultiplier = 1.00;
            int    miscFlatValue = 0;

            // Regarding "Blessed" armor, clothes, jewelry, weapons and such
            // (e.g. things that aren't implicitly blessed, so as to exclude
            // spellbooks and such) :
            // Character death is probably a much bigger loss-risk for
            // items than durability expiration, not to mention that it
            // saves like 700gp/death (assuming the item is desirable
            // enough to insure). By that logic, it's like durability
            // bonus on steroids, and might deserve something like
            // a 5x or 10x price multiplier. Which is a lot in my book,
            // but worth it. Heck, it'd probably take a lot more than
            // that to make me sell a blessed item to an NPC...
            // unless it's kindof a trash item.
            if (item.LootType == LootType.Blessed
            && (item.IsArtifact || (!(item is Spellbook) && !(item is Runebook)))
            && (item is BaseWeapon     || item is BaseArmor
             || item is BaseJewel      || item is BaseClothing
             || item is BaseInstrument || item is BaseTalisman
             || item is ICombatEquipment)
            )
                miscMultiplier *= 10.00;

            // ===========================================
            // -------------- AosAttributes --------------
            double aosAttrMultiplier = 1.00;
            int aosAttrFlatValue = 0; // gp

            double negativityMultiplier = 1.00; // Needs to be declared early for deduping purposes.
            bool alreadyBrittle = false;

            if (item is IItemWithAosAttributes)
            {
                var thingy = item as IItemWithAosAttributes;
                var attr   = thingy.Attributes;
                aosAttrFlatValue += 200 * attr.RegenHits;
                aosAttrFlatValue += 200 * attr.RegenStam;
                aosAttrFlatValue += 200 * attr.RegenMana;
                aosAttrFlatValue += 150 * attr.DefendChance; // This might actually be mathematically superior to skill points.
                aosAttrFlatValue += 150 * attr.AttackChance; // Ditto.
                aosAttrFlatValue += 125 * attr.BonusStr; // Might have slightly more utility due to carrying weight.
                aosAttrFlatValue += 100 * attr.BonusDex;
                aosAttrFlatValue += 100 * attr.BonusInt;
                aosAttrFlatValue += 100 * attr.BonusHits;
                aosAttrFlatValue += 100 * attr.BonusStam;
                aosAttrFlatValue += 100 * attr.BonusMana;
                if ( !meta.IsEasilyManufactured ) // WeaponDamage is present on Exceptional items, but we're already boosting for that.
                    aosAttrFlatValue += 150 * attr.WeaponDamage; // Maxes at +100%, I think.
                aosAttrFlatValue += 150 * attr.WeaponSpeed; // Swing speed increase.
                aosAttrFlatValue += 600 * attr.SpellDamage; // Maxes at +12%, I think.

                if ( attr.CastRecovery < 0 ) // Not sure if this happens.
                    aosAttrMultiplier *= Math.Pow(0.80, -attr.CastRecovery);
                else
                    aosAttrFlatValue += 300 * attr.CastRecovery; // Boosted because useful + small point range

                if ( attr.CastSpeed < 0 ) // E.g. for spell channeling and/or mage weapons.
                    aosAttrMultiplier *= Math.Pow(0.80, -attr.CastSpeed);
                else
                    aosAttrFlatValue += 600 * attr.CastSpeed; // Boosted because CRUCIAL

                aosAttrFlatValue += 300 * attr.LowerManaCost; // It's +Mana and +Regen in one, and thus OP. *Valuable*
                aosAttrFlatValue += 100 * attr.LowerRegCost; // Kinda cool for grinding magery, but otherwise not huge.
                aosAttrFlatValue += 100 * attr.LowerAmmoCost; // Ditto, but for arrows+bolts.

                aosAttrFlatValue +=  50 * attr.ReflectPhysical; // Seems undervalued, BUT...
                if ( attr.ReflectPhysical != 0 ) // This one will also affect multiplier,
                    aosAttrMultiplier *= 1.20; // because of utility generated by ANY amount of reflection causing interruptions.

                aosAttrFlatValue +=  50 * attr.EnhancePotions;

                // LUCK
                if ( attr.Luck < 0 )
                    aosAttrMultiplier *= 0.80;
                else
                    aosAttrFlatValue = aosAttrFlatValue
                        + ( 25 * Math.Min(attr.Luck-00,    40)) // First 40 points aren't worth much. GM armor can do this.
                        + (100 * Saturate(attr.Luck-40, 0, 40)) // Points 41-80 are special. Those are hard to get. 100gp per.
                        + (400 * Math.Max(attr.Luck-80, 0));    // ... and something over 80 would be crazy good (and rare). 400gp per.

                if ( attr.SpellChanneling != 0 )
                {
                    // SpellChanneling typically comes with a Faster Casting -1
                    // penalty. Notably, a SpellChanneling item with 0 Faster Casting
                    // actually has two attributes: SpellChanneling (contributes -1 FC) and
                    // Faster Casting +1 (contributes +1 FC). So when we are looking
                    // for CastSpeed > 0, we are selecting SpellChanneling items that
                    // do not penalize (and, perhaps rarely or impossibly, help it).
                    // A spell channeling item with no penalty is a rare and highly
                    // synergistic combination, so I'm giving it a disproportionately
                    // strong price multiplying factor.
                    if ( attr.CastSpeed > 0 )
                        aosAttrMultiplier *= 4.00; // Yeah... let's see what happens.
                    else
                        aosAttrMultiplier *= 2.00; // Sucky spell channeling is still quite useful.
                }

                if ( attr.NightSight != 0 )
                    aosAttrMultiplier *= 1.20;

                // "Increased Karma Loss is an item property inrocuded with
                // Publish 36 which grants higher Karma loss for casting Necromancy spells."
                // https://www.uoguide.com/Increased_Karma_Loss
                if ( attr.IncreasedKarmaLoss != 0 )
                    aosAttrMultiplier *= 1.20;

                // Seems redundant with NegativeAttributes below,
                // hence the meta.BrittleCount member.
                if ( attr.Brittle != 0 )
                {
                    alreadyBrittle = true;
                    negativityMultiplier *= 0.80;
                }

                // "While wielding a two-handed weapon with the Balanced property,
                // one can perform any action that requires a free hand, such as
                // drinking a Potion or throwing a Shuriken."
                // https://www.uoguide.com/Balanced
                // This sounds very useful, especially in PvP, so I'm giving it
                // a medio-good multiplier.
                if ( attr.BalancedWeapon != 0 )
                    aosAttrMultiplier *= 1.50;
            }

            // Skill bonuses.
            if (item is IItemWithAosAttributes)
            {
                var thingy = item as IItemWithAosAttributes;
                var attr   = thingy.SkillBonuses;

                // Scripts/Misc/AOS.cs seems to indicate that
                // there can only be five (5) of these at once
                // as the AosSkillBonuses.GetProperties method
                // hardcodes 5 as the halting value for its
                // for-loop. I'm going to try and future proof
                // a bit, and use 31, which should be valid for
                // all signed (32-bit) integers, which is how
                // this thing's flag bit-array is stored (AFAIK).
                for (int i = 0; i < 31; ++i)
                {
                    SkillName skill;
                    double bonus;

                    if (!attr.GetValues(i, out skill, out bonus))
                        continue;

                    if ( bonus == 0 )
                        continue;

                    if ( bonus < 0 )
                    {
                        aosAttrMultiplier *= 0.80;
                        continue;
                    }

                    // Positive skill bonus confirmed:
                    //
                    // Hmmm, value should depend heavily on skill.
                    // I'm not sure what ones are sploity or worthless
                    // beyond what I'm putting here, so this could
                    // probably use improvement.
                    int GpPerSkillPoint()
                    {
                        switch(skill)
                        {
                            case SkillName.AnimalTaming: return 1000; // Oh god yes.
                            case SkillName.Magery:       return  500; // Aristocratic skill; costs money|items + time to gain.
                            case SkillName.Inscribe:     return  500; // Ditto
                            case SkillName.Blacksmith:   return  500; // Ditto
                            case SkillName.Alchemy:      return  500; // Ditto (lotsa regs IIRC...)
                            case SkillName.Imbuing:      return  500; // Educated guess here.
                            case SkillName.Poisoning:    return  500; // IIRC, this is expensive AND painful. Go fig.
                            case SkillName.Tailoring:    return  400; // Costs resources, but easier resources.
                            case SkillName.Lockpicking:  return  400; // Ditto
                            default:                     return  200;
                            case SkillName.Tracking:     return  150; // Might be useful in PvP. Sometimes. 30 is probably enough.
                            case SkillName.Fishing:      return  120; // Better of low-usefulness skills; can pull treasure at sea.
                            case SkillName.SpiritSpeak:  return   80; // Gets a small break incase it helps necromancy or something.
                            case SkillName.Begging:      return   80; // hah...
                            case SkillName.DetectHidden: return   80; // Get Tracking instead. Bring explo pots.
                            case SkillName.Herding:      return   80; // Dunno.
                            case SkillName.ItemID:       return   50; // pffft.
                            case SkillName.Forensics:    return   50; // Probably also next-to-useless.
                            case SkillName.RemoveTrap:   return   50; // Or, you know, bandages|greater heal.
                            case SkillName.Camping:      return   50; // Instant log out would be useful if it were... instant.
                        }
                    }

                    aosAttrFlatValue += (int)Math.Round(
                        bonus * GpPerSkillPoint()); // 1-15?
                }
            }

            // ============================================
            // ------------ NegativeAttributes ------------
            if (item is IItemWithNegativeAttributes)
            {
                var badbadItem = item as IItemWithNegativeAttributes;
                var attrs = badbadItem.NegativeAttributes;
                var brittleNoDoubleDip = (!alreadyBrittle && (attrs.Brittle != 0));
                if ( brittleNoDoubleDip   ) negativityMultiplier *= 0.80;
                if ( attrs.Prized    != 0 ) negativityMultiplier *= 0.80;
                if ( attrs.Massive   != 0 ) negativityMultiplier *= 0.80;
                if ( attrs.Unwieldly != 0 ) negativityMultiplier *= 0.80;
                if ( attrs.Antique   != 0 ) negativityMultiplier *= 0.80;
                if ( attrs.NoRepair  != 0 ) negativityMultiplier *= 0.80;
            }

            // ===========================================
            // ----------- ElementalAttributes -----------
            double elementalDamageMultiplier = 1.00;
            if (item is IItemWithAosElementalDamage)
            {
                var saltyItem = item as IItemWithAosElementalDamage;
                var attrs = saltyItem.AosElementDamages;
                /*
                // No bonuses for elemental properties, as requested.
                if ( attrs.Physical ) elementalDamageMultiplier *= 1.0;
                if ( attrs.Fire     ) elementalDamageMultiplier *= 1.0;
                if ( attrs.Cold     ) elementalDamageMultiplier *= 1.0;
                if ( attrs.Poison   ) elementalDamageMultiplier *= 1.0;
                if ( attrs.Energy   ) elementalDamageMultiplier *= 1.0;
                */

                // Chaos damage randomly selects an element (incl. physical) on
                // each hit. That's kinda cool, but it might actually be LESS
                // useful than the other ones, unless maybe you find an enemy
                // whose predominant weakness constantly changes. I say this
                // because the whole point of pure-element weapons is to prepare
                // for challenging fights by min-maxing your elements to
                // exploit the enemy's weakest resistance, but this advantage
                // is forfeit if your weapon can't make up its mind.
                //if ( attrs.Chaos    ) elementalDamageMultiplier *= 1.0;

                // So, uh, "Direct" damage is a thing. It ignores resistances.
                // That actually sounds quite OP, so it should fetch a decent
                // price boost. Like, against a heavily armored target, a
                // 100% direct damage weapon (I don't think it ever existed)
                // would do 3-4x damage. There might not actually be any items
                // with this attribute AT ALL, currently.
                // Reference: https://www.uoguide.com/Direct_Damage
                if ( attrs.Direct != 0 ) elementalDamageMultiplier *= 2.00;
            }

            // ===========================================
            // ------------ Weapon Attributes ------------
            int weaponAttrFlatValue = 0;
            double weaponAttrMultiplier = 1.00;
            bool hasReactiveParalyze = false;
            if ( item is BaseWeapon )
            {
                var weapon = item as BaseWeapon;
                var attr = weapon.WeaponAttributes;

                weaponAttrFlatValue +=  10 * attr.LowerStatReq;

                // Self repair is weird.
                // https://www.uoguide.com/Self_Repair
                // But also looks really useful once understood.
                weaponAttrFlatValue += 200 * attr.SelfRepair;

                weaponAttrFlatValue += 150 * attr.HitLeechHits;    // 2-100%
                weaponAttrFlatValue += 150 * attr.HitLeechStam;    // 2-50%
                weaponAttrFlatValue += 150 * attr.HitLeechMana;    // 2-100%
                weaponAttrFlatValue += 100 * attr.HitLowerAttack;  // 2-50%
                weaponAttrFlatValue += 100 * attr.HitLowerDefend;  // 2-50%
                weaponAttrFlatValue += 150 * attr.HitMagicArrow;   // 2-50%
                weaponAttrFlatValue += 150 * attr.HitHarm;         // 2-50%
                weaponAttrFlatValue += 150 * attr.HitFireball;     // 2-50%
                weaponAttrFlatValue += 150 * attr.HitLightning;    // 2-50%
                weaponAttrFlatValue += 150 * attr.HitDispel;       // 2-50%

                // The AoE ones will be considered less useful:
                // Yeah, AoE is cool, but these are usually pretty weak,
                // and tend to just cause things to aggro on you unnecessarily.
                weaponAttrFlatValue += 100 * attr.HitColdArea;     // 2-50%
                weaponAttrFlatValue += 100 * attr.HitFireArea;     // 2-50%
                weaponAttrFlatValue += 100 * attr.HitPoisonArea;   // 2-50%
                weaponAttrFlatValue += 100 * attr.HitEnergyArea;   // 2-50%
                weaponAttrFlatValue += 100 * attr.HitPhysicalArea; // 2-50%

                weaponAttrFlatValue += 150 * attr.ResistPhysicalBonus;
                weaponAttrFlatValue += 150 * attr.ResistFireBonus;
                weaponAttrFlatValue += 150 * attr.ResistColdBonus;
                weaponAttrFlatValue += 150 * attr.ResistPoisonBonus;
                weaponAttrFlatValue += 150 * attr.ResistEnergyBonus;
                if ( attr.UseBestSkill != 0 )
                    weaponAttrMultiplier *= 1.20;

                // "Mage Weapon is an Item Property that converts a sorcerer's
                // Magery skill into the weapon's Required Skill, subject to any
                // listed penalty, which typically range from -29 to -21, or
                // anywhere down to -0 for artifacts." Paraphrased from:
                // https://www.uoguide.com/Mage_Weapon
                //
                // It seems that a value of 0 for this attribute correlates with
                // a -30 skill modifier, and then each additional point reduces
                // the skill penalty by one. However, a value of 0 also indicates
                // that the attribute is not present, so items with a Mage Weapon
                // attribute that decreases Magery by 30 skill points should not
                // be possible on ServUO shards.
                //
                // Examples:
                // attr.MageWeapon ==  0  ->  -30 Magery (Also indicates that this item does not have Mage Weapon attribute.)
                // attr.MageWeapon ==  5  ->  -25 Magery
                // attr.MageWeapon == 10  ->  -20 Magery
                // attr.MageWeapon == 12  ->  -18 Magery
                // attr.MageWeapon == 20  ->  -10 Magery
                // attr.MageWeapon == 30  ->   -0 Magery (Probably only available on artifacts.)
                if ( attr.MageWeapon > 0 )
                {
                    if ( attr.MageWeapon > 20 )
                        weaponAttrMultiplier *= 2.00;
                    else
                    if ( attr.MageWeapon > 10 )
                        weaponAttrMultiplier *= 1.50;
                    else
                    // ( attr.MageWeapon > 0 )
                        weaponAttrMultiplier *= 1.20;

                    // Bonus value for synergy between this and spell channeling.
                    // It doesn't need to be huge, because both of these are already
                    // multiplying each other.
                    if ( meta.IsSpellChanneling )
                        weaponAttrMultiplier *= 1.20;

                    // Also apply some flat bonus on a per-point basis.
                    weaponAttrFlatValue = weaponAttrFlatValue
                        + ( 25 * Math.Min(attr.MageWeapon-00,    10)) // First 10 points aren't worth much.
                        + (100 * Saturate(attr.MageWeapon-10, 0, 20)) // Points from 10 through 20.
                        + (400 * Math.Max(attr.MageWeapon-20, 0));    // Points at 20 and above are super valuable.
                }

                // "A successful hit with such a weapon will allow an attacker to
                // gain life from using the Bleed Attack, which is a special move.
                // All of the damage done through a bleed attack is directly
                // transferred to the attacker’s health."
                // https://www.uoguide.com/Blood_Drinker
                weaponAttrFlatValue += 100 * attr.BloodDrinker; // 2-50% ??

                // "A successful hit with such a weapon initiates a damage increase that is modified by several factors."
                // "Significant damage received from opponents will be added to
                // the attacker’s Battle Lust, causing them to do more damage to
                // all opponents the attacker engages. This damage bonus is further
                // modified by how many opponents the attacker is aggressed against."
                // "The damage bonus is 15% per opponent, with a cap of 45% in PvP and 90% in PvE.
                // Battle Lust is gained every two seconds and decays at a rate of one point every six seconds."
                // https://www.uoguide.com/Battle_Lust
                weaponAttrFlatValue += 100 * attr.BattleLust;   // 2-50% ??

                weaponAttrFlatValue += 100 * attr.HitCurse;     // 2-50%
                weaponAttrFlatValue += 100 * attr.HitFatigue;   // 2-50%
                weaponAttrFlatValue += 100 * attr.HitManaDrain; // 2-50%

                // "A successful hit with such a weapon causes a glass shard to
                // break off from the weapon (reducing its durability), striking
                // the victim and causing bleed effect and four seconds forced
                // walking. This property stacks with the bleed special move to
                // cause additional damage and extend the duration of the special attack."
                // "Splintering Weapon effect will not trigger when performing Disarm."
                // https://www.uoguide.com/Splintering_Weapon
                weaponAttrFlatValue += 100 * attr.SplinteringWeapon;

                // "Reactive Paralyze is an item property found on shields and
                // two-handed weapons. If the wielder effectively parries an
                // attacker's blow then there is a 30% chance to cast the
                // magery spell paralyze on the attacker."
                // https://www.uoguide.com/Reactive_Paralyze
                if ( attr.ReactiveParalyze != 0 && !hasReactiveParalyze )
                {
                    hasReactiveParalyze = true;
                    weaponAttrMultiplier *= 1.20; // Seems to be on/off instead of point-value.
                }
            }

            if (item is BaseWeapon)
            {
                // Note sure why these aren't also "WeaponAttributes", but sure,
                // let's add another block statement.
                var weapon = item as BaseWeapon;
                var attr = weapon.ExtendedWeaponAttributes;

                // "20% chance to inflict a stamina drain over time on targets
                // for 4 seconds, which prevents consumption of refreshment potions.
                // Will not activate with special moves.
                // While the wielder has 30 or more mana, victims take additional
                // physical damage, independent of the chance to activate the
                // stamina drain. Will not activate with special moves.
                // When the damage bonus activates it will consumes 30 mana and is
                // influenced by LMC Victims receive a 60 second immunity from Bone Breaker"
                // https://uo.com/wiki/ultima-online-wiki/items/magic-item-properties/
                //
                // grepping for this in code only brings up the artifact, not the property.
                // Even the artifact doesn't seem to have this property.
                // It might not be implemented right now (2020-06-11).
                //
                // Obligatory: https://www.youtube.com/watch?v=9c6DjLv4fic
                //
                if ( attr.BoneBreaker != 0 )
                    weaponAttrMultiplier *= 1.20;

                // "A chance to activate a swarm of insects on their targets,
                // causing physical damage over time until target takes fire damage
                // or equips a torch.  Will not activate with special moves."
                // https://uo.com/wiki/ultima-online-wiki/items/magic-item-properties/
                //
                // This is a numeric property. Details in:
                // Scripts/Items/Equipment/Weapons/BaseWeapon.cs
                // Scripts/Items/Artifacts/Equipment/Weapons/BowOfTheInfiniteSwarm.cs:
                weaponAttrFlatValue += 200 * attr.HitSwarm;

                // "A chance to activate devastating energy sparks on their targets,
                // causing energy damage over time. Will not activate with special moves.
                // Any post-resist damage done to a target is given back to the attacker
                // as mana. The effect is doubled on monsters."
                // https://uo.com/wiki/ultima-online-wiki/items/magic-item-properties/
                //
                // This is a numeric property. Details in:
                // Scripts/Items/Equipment/Weapons/BaseWeapon.cs
                // Scripts/Items/Artifacts/Equipment/Weapons/TheDeceiver.cs
                weaponAttrFlatValue += 400 * attr.HitSparks;

                // "This on hit property will only trigger when targets health is below 50%.
                // As the targets health decreases the chance for the property to fire will
                // increase along with the damage. Bane will damage the target with 30%
                // of the targets max hit points in physical damage."
                // https://uo.com/wiki/ultima-online-wiki/items/magic-item-properties/
                //
                // This is an on/off property. Details in:
                // Scripts/Items/Equipment/Weapons/BaseWeapon.cs
                // Scripts/Items/Artifacts/Equipment/Weapons/Abhorrence.cs
                // Scripts/Items/Artifacts/Equipment/Weapons/CaptainJohnesBlade.cs
                if ( attr.Bane != 0 )
                    weaponAttrMultiplier *= 1.20;

                // Mystic:
                //
                // Wrong interpretation:
                // "Mystic refers to one of three types of weapons: Power; Vanquishing; and Mystic.
                // Mystic is the most powerful of the three. Mystic weapons will have that word,
                // in yellow, on the top line of the tool tips. All three weapon types were looted
                // off of monsters killed during the Britain, Ophidian and Magincia Invasions.
                // During the Ophidian and Magincia invasions, Mystic weapons were the only effective
                // means to damage Daemon Berserkers.
                //
                // Power, Vanquishing, and Mystic weapons have a long history in UO.
                // The vestigial skill Item Identification was once the only way to discern such weapons.
                //
                // Notes:
                //  * Mystic weapons always have 40% Damage Increase
                //  * Only weapons introduced prior to the Mondain's Legacy have been available with mystic
                // "
                // https://www.uoguide.com/Mystic_(Item_Property)
                //
                // Correct interpretation:
                // Actually, after looking at ServUO source code, this seems
                // to be more like "Mage Weapon", except for the Mysticism skill.
                //
                if ( attr.MysticWeapon > 0 )
                {
                    if ( attr.MysticWeapon > 20 )
                        weaponAttrMultiplier *= 2.00;
                    else
                    if ( attr.MysticWeapon > 10 )
                        weaponAttrMultiplier *= 1.50;
                    else
                    // ( attr.MysticWeapon > 0 )
                        weaponAttrMultiplier *= 1.20;

                    // Bonus value for synergy between this and spell channeling.
                    // It doesn't need to be huge, because both of these are already
                    // multiplying each other.
                    if ( meta.IsSpellChanneling )
                        weaponAttrMultiplier *= 1.20;

                    // Also apply some flat bonus on a per-point basis.
                    weaponAttrFlatValue = weaponAttrFlatValue
                        + ( 25 * Math.Min(attr.MysticWeapon-00,    10)) // First 10 points aren't worth much.
                        + (100 * Saturate(attr.MysticWeapon-10, 0, 20)) // Points from 10 through 20.
                        + (400 * Math.Max(attr.MysticWeapon-20, 0));    // Points at 20 and above are (maybe?) super valuable.
                }

                // "Assassin Honed is an item property introduced with Publish 74,
                // it is found on weapons in the treasure room in Wrong.
                //
                // * A successful hit with a weapon will provide bonus damage if
                //     the attacker is facing the same direction as the target.
                // * The percentage of the damage is based on the weapons
                //     original swing speed. The faster the sing speed of the weapon
                //     the higher the damage bonus. Swing Speed Increase has no affect.
                // * Ranged weapons have a 50% chance to proc.
                // * The damage bonus is subject to the 300% damage cap.
                // * Items with the property cannot be Enhanced or Runic Reforged.
                //     the can be imbued.
                //
                // The damage bonus scales based on the original weapon speed were
                // 2 second weapons can receive up to a 73% damage bonus while
                // 4 seconds weapons can receive up to a 33% damage bonus when triggered."
                // https://www.uoguide.com/Assassin_Honed
                //
                // Based on Scripts/Services/Dungeons/WrongDungeon/EnchantedHotItem.cs,
                // this seems to be on/off. (0 = off, 1 = on)
                //
                if ( attr.AssassinHoned != 0 )
                    weaponAttrMultiplier *= 1.20;

                // Focus:
                //
                // Is assigned in Scripts/Items/Resource/FocusingGemOfVirtueBane.cs
                // and seems to be on/off. (0 = off, 1 = on)
                // In Scripts/Abilities/Focus.cs, the UpdateBuff method uses
                // the "RageFocusingBuff" icon when applying the buff.
                //
                // So it would seem that this is "Rage Focus":
                //
                // "Rage Focus is an item property found on weapons that causes
                // the damage inflicted to start off below -40% of the weapon's
                // base damage but steadily rises until it is +20%. I can surpass
                // the 300% damage cap. In effect, initial damage is lower than
                // normal, but eventually reaches a point where the attacks are
                // higher than otherwise possible before the cycle ends and then
                // begins to repeat.
                //
                // Overall, the effect of the property is to give more cumulative
                // damage when used than when not, with less damage initially and
                // then more damage conclusively."
                // https://www.uoguide.com/Rage_Focus
                if ( attr.Focus != 0 )
                    weaponAttrMultiplier *= 1.20;

                // The guides seem to also lack entries for Hit Explosion.
                // However, the code in BaseWeapon.cs seems to indicate that this
                // does exactly what it says on the tin: a random chance to
                // inflict the (Magery) explosion spell on the target. Ouch!
                // There are a few codepaths in "Scripts/Services/Seasonal Events/RisingTide/Rewards.cs"
                // that assign this property to 15.
                // Seems like it should be worth a good bit.
                weaponAttrFlatValue += 1000 * attr.HitExplosion;
            }

            if (item is ISlayer)
            {
                var slayer = item as ISlayer;
                if ( slayer.Slayer  != SlayerName.None
                ||   slayer.Slayer2 != SlayerName.None )
                    weaponAttrMultiplier *= 1.20;
            }

            // ============================================
            // ------------- Armor Attributes -------------
            int armorAttrFlatValue = 0;
            double armorAttrMultiplier = 1.00;
            if (item is BaseArmor)
            {
                var armor = item as BaseArmor;
                var attr = armor.ArmorAttributes;

                armorAttrFlatValue +=  10 * attr.LowerStatReq;

                // "* When an item takes damage, the Self Repair property adds
                //      durability equal to its intensity (so SR 1 would add one point,
                //      SR 5 would add 5 points.)"
                //  * It then sets a timer, and Self Repair doesn't go off again on
                //      that item for 60 seconds, even if the item gets damaged again.
                //  * If an item with the Self Repair property is imbued, Self Repair
                //      will be stripped from the item. Likewise, Self Repair is not an
                //      imbuable property.
                // "
                // https://www.uoguide.com/Self_Repair
                // Self repair is weird.
                // But also looks potentially useful once understood.
                armorAttrFlatValue += 200 * attr.SelfRepair;

                // "Mage Armor is an property found on armor which allows for
                // meditation in armor pieces which are non-medable, such as
                // Platemail, Chainmail, Ringmail, Studded and Bone Armor.
                // A full suit of Mage Armor would allow meditation the same
                // as standard medable equipment, providing all set pieces
                // have the Mage Armor property. It also negates the armor
                // penalty on the Stealth skill."
                // https://www.uoguide.com/Mage_Armor
                if ( attr.MageArmor != 0 )
                    armorAttrMultiplier *= 1.20;

                // "Reactive Paralyze is an item property found on shields and
                // two-handed weapons. If the wielder effectively parries an
                // attacker's blow then there is a 30% chance to cast the
                // magery spell paralyze on the attacker."
                // https://www.uoguide.com/Reactive_Paralyze
                if ( attr.ReactiveParalyze != 0 && !hasReactiveParalyze )
                {
                    hasReactiveParalyze = true;
                    armorAttrMultiplier *= 1.20; // Seems to be on/off instead of point-value.
                }

                // "Soul Charge is an item property found on certain shields.
                // It provides a chance to convert a percentage of damage dealt
                // to a player into mana and can only be triggered every 15 seconds."
                // https://www.uoguide.com/Soul_Charge
                // I had to grep the source code to find it out, but this is actually
                // a numeric attribute and not just on/off.
                // I saw a few artifacts have 20, 20, and 30 of this.
                // So it's probably going to be in the 1-10, 1-15, or 1-20 range normally.
                armorAttrFlatValue += 200 * attr.SoulCharge;
            }

            // ===========================================
            // -------------- SA Absorption --------------
            int absorptionFlatValue = 0;
            if (item is IItemWithSAAbsorptionAttributes)
            {
                var absorber = item as IItemWithSAAbsorptionAttributes;
                var attr = absorber.AbsorptionAttributes;

                // "It converts a percentage of damage dealt to a player back as health.
                // However, the damage inflicted must be the same type as the eater
                // for the property to function. The percentage of the damage converted
                // to health stacks with other eaters of the same type, but is capped at 30%."
                //
                // "Eaters are charged over time provided you don't get damaged during
                // that time. Damage Eater properties have a capacity to store up to
                // 20 healing charges and convert charges every three seconds from the
                // last time damage was received before they stop converting damage."
                // "Some special attacks like Bleed are considered direct damage and
                // only triggered with all type damage eater and not kinetic eater."
                // https://www.uoguide.com/Damage_Eater
                // (There's more, but this is probably enough for pricing decisions and general knowledge.)
                absorptionFlatValue +=  200 * attr.EaterFire;
                absorptionFlatValue +=  200 * attr.EaterCold;
                absorptionFlatValue +=  200 * attr.EaterPoison;
                absorptionFlatValue +=  200 * attr.EaterEnergy;
                absorptionFlatValue +=  200 * attr.EaterKinetic; // Physical damage.
                absorptionFlatValue += 1000 * attr.EaterDamage;  // ALL damage. (Seems OP; might actually be worth this much.)

                // "Resonance is an item property found on certain weapons and shields.
                // It provides a chance to resist spell-casting interruption if the damage
                // type received is the same type as the resonance. It is capped at 40%."
                // https://www.uoguide.com/Resonance
                absorptionFlatValue += 200 * attr.ResonanceFire;
                absorptionFlatValue += 200 * attr.ResonanceCold;
                absorptionFlatValue += 200 * attr.ResonancePoison;
                absorptionFlatValue += 200 * attr.ResonanceEnergy;
                absorptionFlatValue += 200 * attr.ResonanceKinetic;

                // From Scripts/Misc/AOS.cs:
                // /* Soul Charge is wrong.
                //  * Do not use these types.
                //  * Use AosArmorAttribute type only.
                //  * Fill these in with any new attributes.*/
                // attr.SoulChargeFire;
                // attr.SoulChargeCold;
                // attr.SoulChargePoison;
                // attr.SoulChargeEnergy;
                // attr.SoulChargeKinetic;

                // "Casting Focus is an item property found on certain armor.
                // It provides a chance to resist any interruptions while casting spells.
                // It has a cumulative cap of 12%. Inscription skill also grants a 5% bonus
                // in addition (1% bonus for every 10 skill points above 50)
                // and can exceed the item cap."
                // https://www.uoguide.com/Casting_Focus
                absorptionFlatValue += 200 * attr.CastingFocus;
            }

            // ============================================
            // ------------- Durability Bonus -------------

            // Note that we are only counting EXPLICIT durability.
            // Durability from materials/exceptional doesn't count.
            // Those are nice too, but we don't want to double-dip.
            int durabilityBonus = 0;
            if ( item is BaseArmor )
            {
                // AosArmorAttributes (see Scripts/Misc/AOS.cs)
                var armor = item as BaseArmor;
                durabilityBonus = armor.ArmorAttributes.DurabilityBonus;
            }
            else
            if ( item is BaseWeapon )
            {
                // AosWeaponAttributes (see Scripts/Misc/AOS.cs)
                var weapon = item as BaseWeapon;
                durabilityBonus = weapon.WeaponAttributes.DurabilityBonus;
            }

            // Price should not be increased for things like
            // Exceptional daggers made out of iron with no
            // imbuing/altering done. Not only is this sploity,
            // but it can double-dip: Exceptional items are
            // better /because/ of things like durability, so
            // we should either boost price for exceptionalism
            // or for durability, but not both.
            if ( meta.IsEasilyManufactured )
                durabilityBonus = 0;

            // Try to make it follow the amount of utility that would be
            // provided if this item were to last that much longer.
            // This is why it's a multiplier: if it's a good item
            // that lasts twice as long, it's worth twice as much, and
            // that's a lot. If it's a bad item that lasts twice as
            // long, and it's worth twice as much, that's not a lot.
            double durabilityMultiplier = (((double)durabilityBonus)+100.0) / 100.0;

            // Coalesce these to make the math less terrible looking.
            int roleSpecificFlatValue = armorAttrFlatValue + weaponAttrFlatValue;
            double roleSpecificMultiplier = armorAttrMultiplier * weaponAttrMultiplier;

            // Declaration of the return value.
            PricingCoefficients c;

            /*
            Console.WriteLine("");
            Console.WriteLine("qualityMultiplier == {0}", qualityMultiplier);
            */

            // Put this in BEFORE fixed adds.
            // It's mostly just a title, and the bonuses it can add are
            // going to be small compared to other things.
            // If it's an exceptional item that's also imbued or altered
            // (ex: something that took considerable effort to make), then
            // those attributes will not be excluded due to being
            // player-made and will get counted later on anyways.
            // So basically, there's no reason to give qualityMultiplier
            // a big say in the price of an item. Though it's nice to give
            // it a bonus off of the base price for role-playing purposes.
            c.PreMultiplier = qualityMultiplier;

            /*
            Console.WriteLine("");
            Console.WriteLine("miscFlatValue         == {0}", miscFlatValue);
            Console.WriteLine("absorptionFlatValue   == {0}", absorptionFlatValue);
            Console.WriteLine("aosAttrFlatValue      == {0}", aosAttrFlatValue);
            Console.WriteLine("roleSpecificFlatValue == {0}", roleSpecificFlatValue);
            */

            // Non-percentage boosts, mostly for numeric attributes
            // (things that aren't on/off).
            c.FlatBonus = (0
                + miscFlatValue
                + absorptionFlatValue
                + aosAttrFlatValue
                + roleSpecificFlatValue // weapon + armor attributes
                );

            /*
            Console.WriteLine("");
            Console.WriteLine("miscMultiplier            == {0}", miscMultiplier);
            Console.WriteLine("aosAttrMultiplier         == {0}", aosAttrMultiplier);
            Console.WriteLine("roleSpecificMultiplier    == {0}", roleSpecificMultiplier);
            Console.WriteLine("negativityMultiplier      == {0}", negativityMultiplier);
            Console.WriteLine("elementalDamageMultiplier == {0}", elementalDamageMultiplier);
            Console.WriteLine("materialMultiplier        == {0}", materialMultiplier);
            Console.WriteLine("durabilityMultiplier      == {0}", durabilityMultiplier);
            */

            // The PostMultiplier is where most of the on/off attributes
            // contribute to the price increase. An exception to the on/off
            // category would be durability, where the multiplier scales
            // with its numeric value (under the naive logic that if it
            // lasts twice as long, then it provided twice as much utility).
            c.PostMultiplier = (1.0
                * miscMultiplier
                * aosAttrMultiplier
                * roleSpecificMultiplier    // weapon * armor attributes
                * negativityMultiplier      // No corresponding flat value increase.
                * elementalDamageMultiplier // No corresponding flat value increase.
                * materialMultiplier        // No corresponding flat value increase.
                * durabilityMultiplier      // No corresponding flat value increase.
                );

            return c;
        }

        public int GetSellPriceFor(Item item, BaseVendor vendor)
        {
            int priceAsInt = 0;
            m_Table.TryGetValue(item.GetType(), out priceAsInt);

            // ============================================
            // --------------- Beverages :p ---------------

            // This is done first for the convenience of still
            // having 'price' as an integer. That's not terribly
            // important though, as conversion only adds a
            // statement or two and maybe a variable.

            if (item is BaseBeverage)
            {
                int price1 = priceAsInt, price2 = priceAsInt;

                if (item is Pitcher)
                {
                    price1 = 3;
                    price2 = 5;
                }
                else if (item is BeverageBottle)
                {
                    price1 = 3;
                    price2 = 3;
                }
                else if (item is Jug)
                {
                    price1 = 6;
                    price2 = 6;
                }

                BaseBeverage bev = (BaseBeverage)item;

                if (bev.IsEmpty || bev.Content == BeverageType.Milk)
                    priceAsInt = price1;
                else
                    priceAsInt = price2;
            }

            // ============================================
            // Convert to (double) floating point so that
            // fractional calculations like multipliers can
            // be made without losing precision before the
            // result is cast/rounded back to an integer.
            double price = priceAsInt;

            // ============================================
            // This was in the original/upstream version of
            // the method. I'm not sure exactly what's going
            // on, but it seems to be adjusting the price
            // based on the vendor's supply+demand.
            if (vendor != null && BaseVendor.UseVendorEconomy)
            {
                IBuyItemInfo buyInfo = vendor.GetBuyInfo().OfType<GenericBuyInfo>().FirstOrDefault(info => info.EconomyItem && info.Type == item.GetType());

                if (buyInfo != null)
                {
                    //int sold = buyInfo.TotalSold;
                    price = buyInfo.Price * 0.75;

                    // Original code returned from method at this statement.
                    price = Math.Max(1, price);
                }
            }

            // ============================================
            // Extract some metadata for easy use later on.
            // In other words: this should happen early
            // because the information will be needed soon
            // after to validate certain attributes
            // (ex: to reject or positive attributes that are
            // too easy to manufacture or are duplicated with
            // player sourced attributes like "Exceptional").
            SellPriceMetadata meta = GetSellPriceMetadata(item);

            // ============================================
            // Now that we have metadata, it is time to
            // generate the parameters (coefficients) that
            // will be applied to our pricing function.
            PricingCoefficients c = GetPricingCoefficients(item, meta);

            /*
            Console.WriteLine("");
            Console.WriteLine("After gathering numbers:");
            Console.WriteLine("");
            Console.WriteLine("price == {0}", price);
            Console.WriteLine("PreMultiplier == {0}", c.PreMultiplier);
            */
            price *= c.PreMultiplier;

            /*
            Console.WriteLine("");
            Console.WriteLine("price == {0}", price);
            Console.WriteLine("FlatBonus == {0}", c.FlatBonus);
            */

            price += c.FlatBonus;

            /*
            Console.WriteLine("");
            Console.WriteLine("price == {0}", price);
            Console.WriteLine("PostMultiplier == {0}", c.PostMultiplier);
            */
            price *= c.PostMultiplier;

            /*
            Console.WriteLine("");
            Console.WriteLine("price == {0} (after multiplications)", price);
            */
            priceAsInt = (int)Math.Round(price);

            /*
            Console.WriteLine("");
            Console.WriteLine("priceAsInt == {0}  (after conversion to int)", priceAsInt);
            */

            if (priceAsInt < 1)
                priceAsInt = 1;

            return priceAsInt;
        }

        public string GetItemSellPriceFormulaString(Item item)
        {
                // (b*m0 + k)*m1 = b*m0*m1 + k*m1
                // (By distributive property of multiplication.)
                // This gives us a way to write the pricing formula
                // that more immediately represents what a player is
                // likely to see when they try to sell that item.
                // A BaseVendor object would be necessary to calculate
                // the "BaseSellPrice" part of this, but we don't have
                // that if we are inspecting an item in isolation, so
                // this ends up being an estimate, but hopefully a helpful one.
                PricingCoefficients c = this.GetPricingCoefficients(item);
                return String.Format("(BaseSellPrice * {0}) + {1}",
                    c.PreMultiplier * c.PostMultiplier,
                    c.FlatBonus * c.PostMultiplier);
        }

        public int GetBuyPriceFor(Item item)
        {
            return GetBuyPriceFor(item, null);
        }

        public int GetBuyPriceFor(Item item, BaseVendor vendor)
        {
            return (int)(1.90 * GetSellPriceFor(item, vendor));
        }

        public string GetNameFor(Item item)
        {
            if (item.Name != null)
                return item.Name;
            else
                return item.LabelNumber.ToString();
        }

        public bool IsSellable(Item item)
        {
            if (item.QuestItem)
                return false;

            //if ( item.Hue != 0 )
            //return false;

            return IsInList(item.GetType());
        }

        public bool IsResellable(Item item)
        {
            if (item.QuestItem)
                return false;

            //if ( item.Hue != 0 )
            //return false;

            return IsInList(item.GetType());
        }

        public bool IsInList(Type type)
        {
            return m_Table.ContainsKey(type);
        }

        public bool IsItemWorthGoingIntoDebt(Item item, BaseVendor vendor)
        {
            int basePrice = GetBaseSellPriceFor(item);
            int fullPrice = GetSellPriceFor(item, vendor);
            return IsItemWorthGoingIntoDebt(item, basePrice, fullPrice);
        }

        public static bool IsItemWorthGoingIntoDebt(
            Item item,
            int  basePrice,
            int  fullPrice)
        {
            bool itemWorthGoingIntoDebt = false;
            if ( basePrice != 0 )
                itemWorthGoingIntoDebt =
                    (fullPrice / basePrice > SplurgeRatioThreshold);

            // Don't do anything special for player-manufactured items.
            SellPriceMetadata meta = GetSellPriceMetadata(item);
            if ( meta.IsEasilyManufactured )
                itemWorthGoingIntoDebt = false;

            return itemWorthGoingIntoDebt;
        }

        public int MaxPayForItem(
            DateTime   transactionTime,
            Item       item,
            BaseVendor vendor)
        {
            bool worthIt = IsItemWorthGoingIntoDebt(item, vendor);
            return MaxPayForItem(transactionTime, item, worthIt);
        }

        public int MaxPayForItem(
            DateTime transactionTime,
            Item     item,
            bool     itemWorthGoingIntoDebt)
        {
            var intervalStart = transactionTime.AddDays(-1);
            var itemType = item.GetType();

            if ( itemWorthGoingIntoDebt )
                return Int32.MaxValue;

            if ( m_SalesLog == null || m_SalesLog.Count == 0 )
                return MaxSpendingOnEachItemPerDay;

            int amountSpent = 0;
            int i = 0;
            while (i < m_SalesLog.Count)
            {
                var sale = this.m_SalesLog[i];

                // Remove outdated entries.
                if ( sale.TransactionTime < intervalStart )
                {
                    // This is fast O(1) removal, but it does leave the
                    // array unsorted. We're scanning it anyways, so
                    // it shouldn't matter.
                    sale = m_SalesLog[m_SalesLog.Count-1];
                    m_SalesLog[i] = sale;
                    m_SalesLog.RemoveAt(m_SalesLog.Count-1);
                }

                // Very important :p
                i++;

                // Skip irrelevant item entries.
                if ( sale.ItemType != itemType )
                    continue;

                // Sum all gold paid.
                amountSpent += sale.GoldPaid;
            }

            return Math.Max(0, MaxSpendingOnEachItemPerDay - amountSpent);
        }

        public void OnSold(DateTime transactionTime, Type itemType, int itemQty, int amountPaid)
        {
            var salesLog = this.SalesLog;

            Sale s;
            s.TransactionTime = transactionTime;
            s.ItemType     = itemType;
            s.ItemQuantity = itemQty;
            s.GoldPaid     = amountPaid;
            salesLog.Add(s);
        }
    }

    public struct PricingCoefficients
    {
        public double  PreMultiplier;  // Base price is first multiplied by this.
        public int     FlatBonus;      // Then this is added.
        public double  PostMultiplier; // Then that is multiplied by this.

        // The FlatBonus and the PostMultipliers will be the most
        // significant contributors.
    }
}
