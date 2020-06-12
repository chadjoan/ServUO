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

        public int GetSellPriceFor(Item item, BaseVendor vendor)
        {
            int price = 0;
            m_Table.TryGetValue(item.GetType(), out price);

            if (vendor != null && BaseVendor.UseVendorEconomy)
            {
                IBuyItemInfo buyInfo = vendor.GetBuyInfo().OfType<GenericBuyInfo>().FirstOrDefault(info => info.EconomyItem && info.Type == item.GetType());

                if (buyInfo != null)
                {
                    int sold = buyInfo.TotalSold;
                    price = (int)(buyInfo.Price * .75);

                    return Math.Max(1, price);
                }
            }

            if (item is BaseArmor)
            {
                BaseArmor armor = (BaseArmor)item;

                if (armor.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (armor.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);

                price += 5 * armor.ArmorAttributes.DurabilityBonus;

                if (price < 1)
                    price = 1;
            }
            else if (item is BaseWeapon)
            {
                BaseWeapon weapon = (BaseWeapon)item;

                if (weapon.Quality == ItemQuality.Low)
                    price = (int)(price * 0.60);
                else if (weapon.Quality == ItemQuality.Exceptional)
                    price = (int)(price * 1.25);

                price += 100 * weapon.WeaponAttributes.DurabilityBonus;

                price += 10 * weapon.Attributes.WeaponDamage;

                if (price < 1)
                    price = 1;
            }
            else if (item is BaseBeverage)
            {
                int price1 = price, price2 = price;

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
                    price = price1;
                else
                    price = price2;
            }

            return price;
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
            if (item is BaseArmor)
            {
                BaseArmor armor = (BaseArmor)item;

                if (armor.Quality == ItemQuality.Low)
                    itemWorthGoingIntoDebt = false;
                else if (armor.Quality == ItemQuality.Exceptional)
                    itemWorthGoingIntoDebt = false;
            }
            else if (item is BaseWeapon)
            {
                BaseWeapon weapon = (BaseWeapon)item;

                if (weapon.Quality == ItemQuality.Low)
                    itemWorthGoingIntoDebt = false;
                else if (weapon.Quality == ItemQuality.Exceptional)
                    itemWorthGoingIntoDebt = false;
            }
            
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
}
