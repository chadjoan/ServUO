#region References
using Server.Accounting;
using Server.ContextMenus;
using Server.Engines.BulkOrders;
using Server.Items;
using Server.Misc;
using Server.Mobiles;
using Server.Network;
using Server.Regions;
using Server.Services.Virtues;
using Server.Targeting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
#endregion

namespace Server.Mobiles
{
    public enum VendorShoeType
    {
        None,
        Shoes,
        Boots,
        Sandals,
        ThighBoots
    }

    public abstract class BaseVendor : BaseCreature, IVendor
    {
        public static bool UseVendorEconomy = !Siege.SiegeShard;
        public static int BuyItemChange = Config.Get("Vendors.BuyItemChange", 1000);
        public static int SellItemChange = Config.Get("Vendors.SellItemChange", 1000);
        public static int EconomyStockAmount = Config.Get("Vendors.EconomyStockAmount", 500);
        public static TimeSpan DelayRestock = TimeSpan.FromMinutes(Config.Get("Vendors.RestockDelay", 60));
        public static int MaxSell = Config.Get("Vendors.MaxSell", 500);

        public static List<BaseVendor> AllVendors { get; private set; }

        static BaseVendor()
        {
            AllVendors = new List<BaseVendor>(0x4000);
        }

        private   VendorLedger m_Ledger = null; // Requires 'this', so must be initialized in constructor.
        protected VendorLedger Ledger
        {
            get
            {
                if ( this.m_Ledger == null )
                    this.m_Ledger = new VendorLedger(this);
                return this.m_Ledger;
            }
        }

        protected abstract List<SBInfo> SBInfos { get; }

        private readonly ArrayList m_ArmorBuyInfo = new ArrayList();
        private readonly ArrayList m_ArmorSellInfo = new ArrayList();

        private DateTime m_LastRestock;

        public override bool CanTeach => true;

        public override bool BardImmune => true;

        public override bool PlayerRangeSensitive => true;

        public override bool UseSmartAI => true;

        public override bool AlwaysInnocent => true;

        public virtual bool IsActiveVendor => true;
        public virtual bool IsActiveBuyer => IsActiveVendor && !Siege.SiegeShard; // response to vendor SELL
        public virtual bool IsActiveSeller => IsActiveVendor; // repsonse to vendor BUY
        public virtual bool HasHonestyDiscount => true;

        public virtual NpcGuild NpcGuild => NpcGuild.None;

        public virtual bool ChangeRace => true;

        public override bool IsInvulnerable => true;

        public virtual DateTime NextTrickOrTreat { get; set; }
        public virtual double GetMoveDelay => Utility.RandomMinMax(30, 120);

        public override bool ShowFameTitle => false;

        public virtual bool IsValidBulkOrder(Item item)
        {
            return false;
        }

        public virtual Item CreateBulkOrder(Mobile from, bool fromContextMenu)
        {
            return null;
        }

        public virtual bool SupportsBulkOrders(Mobile from)
        {
            return false;
        }

        public virtual TimeSpan GetNextBulkOrder(Mobile from)
        {
            return TimeSpan.Zero;
        }

        public virtual void OnSuccessfulBulkOrderReceive(Mobile from)
        { }

        public virtual BODType BODType => BODType.Smith;

        public virtual int GetPriceScalar()
        {
            return 100;
        }

        public void UpdateBuyInfo()
        {
            int priceScalar = GetPriceScalar();

            IBuyItemInfo[] buyinfo = (IBuyItemInfo[])m_ArmorBuyInfo.ToArray(typeof(IBuyItemInfo));

            if (buyinfo != null)
            {
                foreach (IBuyItemInfo info in buyinfo)
                {
                    info.PriceScalar = priceScalar;
                }
            }
        }

        private class BulkOrderInfoEntry : ContextMenuEntry
        {
            private readonly Mobile m_From;
            private readonly BaseVendor m_Vendor;

            public BulkOrderInfoEntry(Mobile from, BaseVendor vendor)
                : base(6152, 10)
            {
                Enabled = vendor.CheckVendorAccess(from);

                m_From = from;
                m_Vendor = vendor;
            }

            public override void OnClick()
            {
                if (!m_From.InRange(m_Vendor.Location, 10))
                    return;

                EventSink.InvokeBODOffered(new BODOfferEventArgs(m_From, m_Vendor));

                if (m_Vendor.SupportsBulkOrders(m_From) && m_From is PlayerMobile)
                {
                    if (BulkOrderSystem.NewSystemEnabled)
                    {
                        if (BulkOrderSystem.CanGetBulkOrder(m_From, m_Vendor.BODType) || m_From.AccessLevel > AccessLevel.Player)
                        {
                            Item bulkOrder = BulkOrderSystem.CreateBulkOrder(m_From, m_Vendor.BODType, true);

                            if (bulkOrder is LargeBOD)
                            {
                                m_From.CloseGump(typeof(LargeBODAcceptGump));
                                m_From.SendGump(new LargeBODAcceptGump(m_From, (LargeBOD)bulkOrder));
                            }
                            else if (bulkOrder is SmallBOD)
                            {
                                m_From.CloseGump(typeof(SmallBODAcceptGump));
                                m_From.SendGump(new SmallBODAcceptGump(m_From, (SmallBOD)bulkOrder));
                            }
                        }
                        else
                        {
                            TimeSpan ts = BulkOrderSystem.GetNextBulkOrder(m_Vendor.BODType, (PlayerMobile)m_From);

                            int totalSeconds = (int)ts.TotalSeconds;
                            int totalHours = (totalSeconds + 3599) / 3600;
                            int totalMinutes = (totalSeconds + 59) / 60;

                            m_Vendor.SayTo(m_From, 1072058, totalMinutes.ToString(), 0x3B2); // An offer may be available in about ~1_minutes~ minutes.
                        }
                    }
                    else
                    {
                        TimeSpan ts = m_Vendor.GetNextBulkOrder(m_From);

                        int totalSeconds = (int)ts.TotalSeconds;
                        int totalHours = (totalSeconds + 3599) / 3600;
                        int totalMinutes = (totalSeconds + 59) / 60;

                        if (totalMinutes == 0)
                        {
                            m_From.SendLocalizedMessage(1049038); // You can get an order now.

                            Item bulkOrder = m_Vendor.CreateBulkOrder(m_From, true);

                            if (bulkOrder is LargeBOD)
                            {
                                m_From.CloseGump(typeof(LargeBODAcceptGump));
                                m_From.SendGump(new LargeBODAcceptGump(m_From, (LargeBOD)bulkOrder));
                            }
                            else if (bulkOrder is SmallBOD)
                            {
                                m_From.CloseGump(typeof(SmallBODAcceptGump));
                                m_From.SendGump(new SmallBODAcceptGump(m_From, (SmallBOD)bulkOrder));
                            }
                        }
                        else
                        {
                            int oldSpeechHue = m_Vendor.SpeechHue;
                            m_Vendor.SpeechHue = 0x3B2;

                            m_Vendor.SayTo(m_From, 1072058, totalMinutes.ToString(), 0x3B2);
                            // An offer may be available in about ~1_minutes~ minutes.

                            m_Vendor.SpeechHue = oldSpeechHue;
                        }
                    }
                }
            }
        }

        private class BribeEntry : ContextMenuEntry
        {
            private readonly Mobile m_From;
            private readonly BaseVendor m_Vendor;

            public BribeEntry(Mobile from, BaseVendor vendor)
                : base(1152294, 2)
            {
                Enabled = vendor.CheckVendorAccess(from);

                m_From = from;
                m_Vendor = vendor;
            }

            public override void OnClick()
            {
                if (!m_From.InRange(m_Vendor.Location, 2) || !(m_From is PlayerMobile))
                    return;

                if (m_Vendor.SupportsBulkOrders(m_From) && m_From is PlayerMobile)
                {
                    if (m_From.NetState != null && m_From.NetState.IsEnhancedClient)
                    {
                        Timer.DelayCall(TimeSpan.FromMilliseconds(100), m_Vendor.TryBribe, m_From);
                    }
                    else
                    {
                        m_Vendor.TryBribe(m_From);
                    }
                }
            }
        }

        private class ClaimRewardsEntry : ContextMenuEntry
        {
            private readonly Mobile m_From;
            private readonly BaseVendor m_Vendor;

            public ClaimRewardsEntry(Mobile from, BaseVendor vendor)
                : base(1155593, 3)
            {
                Enabled = vendor.CheckVendorAccess(from);

                m_From = from;
                m_Vendor = vendor;
            }

            public override void OnClick()
            {
                if (!m_From.InRange(m_Vendor.Location, 3) || !(m_From is PlayerMobile))
                    return;

                BODContext context = BulkOrderSystem.GetContext(m_From);
                int pending = context.GetPendingRewardFor(m_Vendor.BODType);

                if (pending > 0)
                {
                    if (context.PointsMode == PointsMode.Enabled)
                    {
                        m_From.SendGump(new ConfirmBankPointsGump((PlayerMobile)m_From, m_Vendor, m_Vendor.BODType, pending, pending * 0.02));
                    }
                    else
                    {
                        m_From.SendGump(new RewardsGump(m_Vendor, (PlayerMobile)m_From, m_Vendor.BODType, pending));
                    }
                }
                else if (!BulkOrderSystem.CanClaimRewards(m_From))
                {
                    m_Vendor.SayTo(m_From, 1157083, 0x3B2); // You must claim your last turn-in reward in order for us to continue doing business.
                }
                else
                {
                    m_From.SendGump(new RewardsGump(m_Vendor, (PlayerMobile)m_From, m_Vendor.BODType));
                }
            }
        }

        public BaseVendor(string title)
            : base(AIType.AI_Vendor, FightMode.None, 2, 1, 0.5, 5)
        {
            AllVendors.Add(this);

            LoadSBInfo();

            Title = title;

            InitBody();
            InitOutfit();

            Container pack;
            //these packs MUST exist, or the client will crash when the packets are sent
            pack = new Backpack();
            pack.Layer = Layer.ShopBuy;
            pack.Movable = false;
            pack.Visible = false;
            AddItem(pack);

            pack = new Backpack();
            pack.Layer = Layer.ShopResale;
            pack.Movable = false;
            pack.Visible = false;
            AddItem(pack);

            BribeMultiplier = Utility.Random(10);

            m_LastRestock = DateTime.UtcNow;

            m_Ledger = new VendorLedger(this);
        }

        public BaseVendor(Serial serial)
            : base(serial)
        {
            AllVendors.Add(this);
        }

        public override void OnDelete()
        {
            base.OnDelete();

            AllVendors.Remove(this);
        }

        public override void OnAfterDelete()
        {
            base.OnAfterDelete();

            AllVendors.Remove(this);
        }

        public DateTime LastRestock { get { return m_LastRestock; } set { m_LastRestock = value; } }

        public virtual TimeSpan RestockDelay => DelayRestock;

        public Container BuyPack
        {
            get
            {
                Container pack = FindItemOnLayer(Layer.ShopBuy) as Container;

                if (pack == null)
                {
                    pack = new Backpack();
                    pack.Layer = Layer.ShopBuy;
                    pack.Visible = false;
                    AddItem(pack);
                }

                return pack;
            }
        }

        public abstract void InitSBInfo();

        public virtual bool IsTokunoVendor => (Map == Map.Tokuno);
        public virtual bool IsStygianVendor => (Map == Map.TerMur);

        protected void LoadSBInfo()
        {
            m_LastRestock = DateTime.UtcNow;

            for (int i = 0; i < m_ArmorBuyInfo.Count; ++i)
            {
                GenericBuyInfo buy = m_ArmorBuyInfo[i] as GenericBuyInfo;

                if (buy != null)
                {
                    buy.DeleteDisplayEntity();
                }
            }

            SBInfos.Clear();

            InitSBInfo();

            m_ArmorBuyInfo.Clear();
            m_ArmorSellInfo.Clear();

            for (int i = 0; i < SBInfos.Count; i++)
            {
                SBInfo sbInfo = SBInfos[i];
                m_ArmorBuyInfo.AddRange(sbInfo.BuyInfo);
                m_ArmorSellInfo.Add(sbInfo.SellInfo);
            }
        }

        public virtual bool GetGender()
        {
            return Utility.RandomBool();
        }

        public virtual void InitBody()
        {
            InitStats(100, 100, 25);

            SpeechHue = Utility.RandomDyedHue();
            Hue = Utility.RandomSkinHue();
            Female = GetGender();

            if (Female)
            {
                Body = 0x191;
                Name = NameList.RandomName("female");
            }
            else
            {
                Body = 0x190;
                Name = NameList.RandomName("male");
            }
        }

        public virtual int GetRandomHue()
        {
            switch (Utility.Random(5))
            {
                default:
                case 0:
                    return Utility.RandomBlueHue();
                case 1:
                    return Utility.RandomGreenHue();
                case 2:
                    return Utility.RandomRedHue();
                case 3:
                    return Utility.RandomYellowHue();
                case 4:
                    return Utility.RandomNeutralHue();
            }
        }

        public virtual int GetShoeHue()
        {
            if (0.1 > Utility.RandomDouble())
            {
                return 0;
            }

            return Utility.RandomNeutralHue();
        }

        public virtual VendorShoeType ShoeType => VendorShoeType.Shoes;

        public virtual void CheckMorph()
        {
            if (!ChangeRace)
                return;

            if (CheckGargoyle())
            {
                return;
            }
            else if (CheckTerMur())
            {
                return;
            }
            else if (CheckNecromancer())
            {
                return;
            }
            else if (CheckTokuno())
            {
                return;
            }
        }

        public virtual bool CheckTokuno()
        {
            if (Map != Map.Tokuno)
            {
                return false;
            }

            NameList n;

            if (Female)
            {
                n = NameList.GetNameList("tokuno female");
            }
            else
            {
                n = NameList.GetNameList("tokuno male");
            }

            if (!n.ContainsName(Name))
            {
                TurnToTokuno();
            }

            return true;
        }

        public virtual void TurnToTokuno()
        {
            if (Female)
            {
                Name = NameList.RandomName("tokuno female");
            }
            else
            {
                Name = NameList.RandomName("tokuno male");
            }
        }

        public virtual bool CheckGargoyle()
        {
            Map map = Map;

            if (map != Map.Ilshenar)
            {
                return false;
            }

            if (!Region.IsPartOf("Gargoyle City"))
            {
                return false;
            }

            if (Body != 0x2F6 || (Hue & 0x8000) == 0)
            {
                TurnToGargoyle();
            }

            return true;
        }

        #region SA Change
        public virtual bool CheckTerMur()
        {
            Map map = Map;

            if (map != Map.TerMur || Server.Spells.SpellHelper.IsEodon(map, Location))
                return false;

            if (Body != 0x29A && Body != 0x29B)
                TurnToGargRace();

            return true;
        }
        #endregion

        public virtual bool CheckNecromancer()
        {
            Map map = Map;

            if (map != Map.Malas)
            {
                return false;
            }

            if (!Region.IsPartOf("Umbra"))
            {
                return false;
            }

            if (Hue != 0x83E8)
            {
                TurnToNecromancer();
            }

            return true;
        }

        public override void OnAfterSpawn()
        {
            CheckMorph();
        }

        protected override void OnMapChange(Map oldMap)
        {
            base.OnMapChange(oldMap);

            CheckMorph();

            LoadSBInfo();
        }

        public virtual int GetRandomNecromancerHue()
        {
            switch (Utility.Random(20))
            {
                case 0:
                    return 0;
                case 1:
                    return 0x4E9;
                default:
                    return Utility.RandomList(0x485, 0x497);
            }
        }

        public virtual void TurnToNecromancer()
        {
            for (int i = 0; i < Items.Count; ++i)
            {
                Item item = Items[i];

                if (item is Hair || item is Beard)
                {
                    item.Hue = 0;
                }
                else if (item is BaseClothing || item is BaseWeapon || item is BaseArmor || item is BaseTool)
                {
                    item.Hue = GetRandomNecromancerHue();
                }
            }

            HairHue = 0;
            FacialHairHue = 0;

            Hue = 0x83E8;
        }

        public virtual void TurnToGargoyle()
        {
            for (int i = 0; i < Items.Count; ++i)
            {
                Item item = Items[i];

                if (item is BaseClothing || item is Hair || item is Beard)
                {
                    item.Delete();
                }
            }

            HairItemID = 0;
            FacialHairItemID = 0;

            Body = 0x2F6;
            Hue = Utility.RandomBrightHue() | 0x8000;
            Name = NameList.RandomName("gargoyle vendor");

            CapitalizeTitle();
        }

        #region SA
        public virtual void TurnToGargRace()
        {
            for (int i = 0; i < Items.Count; ++i)
            {
                Item item = Items[i];

                if (item is BaseClothing)
                {
                    item.Delete();
                }
            }

            Race = Race.Gargoyle;

            Hue = Race.RandomSkinHue();

            HairItemID = Race.RandomHair(Female);
            HairHue = Race.RandomHairHue();

            FacialHairItemID = Race.RandomFacialHair(Female);
            if (FacialHairItemID != 0)
            {
                FacialHairHue = Race.RandomHairHue();
            }
            else
            {
                FacialHairHue = 0;
            }

            InitGargOutfit();

            if (Female = GetGender())
            {
                Body = 0x29B;
                Name = NameList.RandomName("gargoyle female");
            }
            else
            {
                Body = 0x29A;
                Name = NameList.RandomName("gargoyle male");
            }

            CapitalizeTitle();
        }
        #endregion

        public virtual void CapitalizeTitle()
        {
            string title = Title;

            if (title == null)
            {
                return;
            }

            string[] split = title.Split(' ');

            for (int i = 0; i < split.Length; ++i)
            {
                if (Insensitive.Equals(split[i], "the"))
                {
                    continue;
                }

                if (split[i].Length > 1)
                {
                    split[i] = Char.ToUpper(split[i][0]) + split[i].Substring(1);
                }
                else if (split[i].Length > 0)
                {
                    split[i] = Char.ToUpper(split[i][0]).ToString();
                }
            }

            Title = String.Join(" ", split);
        }

        public virtual int GetHairHue()
        {
            return Utility.RandomHairHue();
        }

        public virtual void InitOutfit()
        {
            if (Backpack == null)
            {
                Item backpack = new Backpack();
                backpack.Movable = false;
                AddItem(backpack);
            }

            switch (Utility.Random(3))
            {
                case 0:
                    SetWearable(new FancyShirt(GetRandomHue()));
                    break;
                case 1:
                    SetWearable(new Doublet(GetRandomHue()));
                    break;
                case 2:
                    SetWearable(new Shirt(GetRandomHue()));
                    break;
            }

            switch (ShoeType)
            {
                case VendorShoeType.Shoes:
                    SetWearable(new Shoes(GetShoeHue()));
                    break;
                case VendorShoeType.Boots:
                    SetWearable(new Boots(GetShoeHue()));
                    break;
                case VendorShoeType.Sandals:
                    SetWearable(new Sandals(GetShoeHue()));
                    break;
                case VendorShoeType.ThighBoots:
                    SetWearable(new ThighBoots(GetShoeHue()));
                    break;
            }

            int hairHue = GetHairHue();

            Utility.AssignRandomHair(this, hairHue);
            Utility.AssignRandomFacialHair(this, hairHue);

            if (Body == 0x191)
            {
                FacialHairItemID = 0;
            }

            if (Body == 0x191)
            {
                switch (Utility.Random(6))
                {
                    case 0:
                        SetWearable(new ShortPants(GetRandomHue()));
                        break;
                    case 1:
                    case 2:
                        SetWearable(new Kilt(GetRandomHue()));
                        break;
                    case 3:
                    case 4:
                    case 5:
                        SetWearable(new Skirt(GetRandomHue()));
                        break;
                }
            }
            else
            {
                switch (Utility.Random(2))
                {
                    case 0:
                        SetWearable(new LongPants(GetRandomHue()));
                        break;
                    case 1:
                        SetWearable(new ShortPants(GetRandomHue()));
                        break;
                }
            }
        }

        #region SA
        public virtual void InitGargOutfit()
        {
            for (int i = 0; i < Items.Count; ++i)
            {
                Item item = Items[i];

                if (item is BaseClothing)
                {
                    item.Delete();
                }
            }

            if (Female)
            {
                switch (Utility.Random(2))
                {
                    case 0:
                        SetWearable(new FemaleGargishClothLegs(GetRandomHue()));
                        SetWearable(new FemaleGargishClothKilt(GetRandomHue()));
                        SetWearable(new FemaleGargishClothChest(GetRandomHue()));
                        break;
                    case 1:
                        SetWearable(new FemaleGargishClothKilt(GetRandomHue()));
                        SetWearable(new FemaleGargishClothChest(GetRandomHue()));
                        break;
                }
            }
            else
            {
                switch (Utility.Random(2))
                {
                    case 0:
                        SetWearable(new MaleGargishClothLegs(GetRandomHue()));
                        SetWearable(new MaleGargishClothKilt(GetRandomHue()));
                        SetWearable(new MaleGargishClothChest(GetRandomHue()));
                        break;
                    case 1:
                        SetWearable(new MaleGargishClothKilt(GetRandomHue()));
                        SetWearable(new MaleGargishClothChest(GetRandomHue()));
                        break;
                }
            }
        }
        #endregion

        [CommandProperty(AccessLevel.GameMaster)]
        public bool ForceRestock
        {
            get { return false; }
            set
            {
                if (value)
                {
                    Restock();
                    Say("Restocked!");
                }
            }
        }

        public virtual void Restock()
        {
            m_LastRestock = DateTime.UtcNow;

            IBuyItemInfo[] buyInfo = GetBuyInfo();

            foreach (IBuyItemInfo bii in buyInfo)
            {
                bii.OnRestock();
            }
        }

        private static readonly TimeSpan InventoryDecayTime = TimeSpan.FromHours(1.0);

        public virtual void VendorBuy(Mobile from)
        {
            if (!IsActiveSeller)
            {
                return;
            }

            if (!from.CheckAlive())
            {
                return;
            }

            if (!CheckVendorAccess(from))
            {
                Say(501522); // I shall not treat with scum like thee!
                return;
            }

            if (DateTime.UtcNow - m_LastRestock > RestockDelay)
            {
                Restock();
            }

            UpdateBuyInfo();

            int count = 0;
            List<BuyItemState> list;
            IBuyItemInfo[] buyInfo = GetBuyInfo();
            IShopSellInfo[] sellInfo = GetSellInfo();

            list = new List<BuyItemState>(buyInfo.Length);
            Container cont = BuyPack;

            List<ObjectPropertyList> opls = null;

            for (int idx = 0; idx < buyInfo.Length; idx++)
            {
                IBuyItemInfo buyItem = buyInfo[idx];

                if (buyItem.Amount <= 0 || list.Count >= 250)
                {
                    continue;
                }

                // NOTE: Only GBI supported; if you use another implementation of IBuyItemInfo, this will crash
                GenericBuyInfo gbi = (GenericBuyInfo)buyItem;
                IEntity disp = gbi.GetDisplayEntity();

                if (Siege.SiegeShard && !Siege.VendorCanSell(gbi.Type))
                {
                    continue;
                }

                list.Add(
                    new BuyItemState(
                        buyItem.Name,
                        cont.Serial,
                        disp == null ? (Serial)0x7FC0FFEE : disp.Serial,
                        buyItem.Price,
                        buyItem.Amount,
                        buyItem.ItemID,
                        buyItem.Hue));
                count++;

                if (opls == null)
                {
                    opls = new List<ObjectPropertyList>();
                }

                if (disp is Item)
                {
                    opls.Add(((Item)disp).PropertyList);
                }
                else if (disp is Mobile)
                {
                    opls.Add(((Mobile)disp).PropertyList);
                }
            }

            List<Item> playerItems = cont.Items;

            for (int i = playerItems.Count - 1; i >= 0; --i)
            {
                if (i >= playerItems.Count)
                {
                    continue;
                }

                Item item = playerItems[i];

                if ((item.LastMoved + InventoryDecayTime) <= DateTime.UtcNow)
                {
                    item.Delete();
                }
            }

            for (int i = 0; i < playerItems.Count; ++i)
            {
                Item item = playerItems[i];

                if (Siege.SiegeShard && !Siege.VendorCanSell(item.GetType()))
                {
                    continue;
                }

                int price = 0;
                string name = null;

                foreach (IShopSellInfo ssi in sellInfo)
                {
                    if (ssi.IsSellable(item))
                    {
                        price = ssi.GetBuyPriceFor(item, this);
                        name = ssi.GetNameFor(item);
                        break;
                    }
                }

                if (name != null && list.Count < 250)
                {
                    list.Add(new BuyItemState(name, cont.Serial, item.Serial, price, item.Amount, item.ItemID, item.Hue));
                    count++;

                    if (opls == null)
                    {
                        opls = new List<ObjectPropertyList>();
                    }

                    opls.Add(item.PropertyList);
                }
            }

            if (list.Count > 0)
            {
                list.Sort(new BuyItemStateComparer());

                SendPacksTo(from);

                NetState ns = from.NetState;

                if (ns == null)
                {
                    return;
                }

                from.Send(new VendorBuyContent(list));

                from.Send(new VendorBuyList(this, list));

                from.Send(new DisplayBuyList(this));

                from.Send(new MobileStatus(from)); //make sure their gold amount is sent

                if (opls != null)
                {
                    for (int i = 0; i < opls.Count; ++i)
                    {
                        from.Send(opls[i]);
                    }
                }

                SayTo(from, 500186, 0x3B2); // Greetings.  Have a look around.
            }
        }

        public virtual void SendPacksTo(Mobile from)
        {
            Item pack = FindItemOnLayer(Layer.ShopBuy);

            if (pack == null)
            {
                pack = new Backpack();
                pack.Layer = Layer.ShopBuy;
                pack.Movable = false;
                pack.Visible = false;
                SetWearable(pack);
            }

            from.Send(new EquipUpdate(pack));

            pack = FindItemOnLayer(Layer.ShopSell);

            if (pack != null)
            {
                from.Send(new EquipUpdate(pack));
            }

            pack = FindItemOnLayer(Layer.ShopResale);

            if (pack == null)
            {
                pack = new Backpack();
                pack.Layer = Layer.ShopResale;
                pack.Movable = false;
                pack.Visible = false;
                SetWearable(pack);
            }

            from.Send(new EquipUpdate(pack));
        }

        public virtual void VendorSell(Mobile from)
        {
            if (!IsActiveBuyer)
            {
                return;
            }

            if (!from.CheckAlive())
            {
                return;
            }

            if (!CheckVendorAccess(from))
            {
                Say(501522); // I shall not treat with scum like thee!
                return;
            }

            DateTime transactionTime = DateTime.UtcNow;
            int  cashOnHand = this.Ledger.GetCashOnHand(transactionTime);
            int  numSellableItems = 0;
            int  numAffordableItems = 0;

            Container pack = from.Backpack;

            if (pack != null)
            {
                IShopSellInfo[] info = GetSellInfo();

                Dictionary<Item, SellItemState> table = new Dictionary<Item, SellItemState>();

                foreach (IShopSellInfo ssi in info)
                {
                    Item[] items = pack.FindItemsByType(ssi.Types);

                    foreach (Item item in items)
                    {
                        if (item is Container && (item).Items.Count != 0)
                        {
                            continue;
                        }

                        if (item.IsStandardLoot() && item.Movable && ssi.IsSellable(item))
                        {
                            numSellableItems++;

                            int basePriceEach = ssi.GetBaseSellPriceFor(item);
                            int fullPriceEach = ssi.GetSellPriceFor(item, this);

                            bool worthIt =
                                GenericSellInfo.IsItemWorthGoingIntoDebt(
                                    item, basePriceEach, fullPriceEach);

                            int maxSaleGold = ssi.MaxPayForItem(transactionTime, item, worthIt);
                            if ( fullPriceEach > maxSaleGold )
                                continue; // Vendor can't afford ANY of this item. So don't offer to buy it.

                            if ( fullPriceEach > cashOnHand
                            && !(worthIt && fullPriceEach/10 < cashOnHand) )
                                continue; // Vendor can't afford ANY of this item. So don't offer to buy it.

                            numAffordableItems++;
                            table[item] = new SellItemState(item, fullPriceEach, ssi.GetNameFor(item));
                        }
                    }
                }

                if (table.Count > 0)
                {
                    SendPacksTo(from);

                    from.Send(new VendorSellList(this, table.Values));
                }
                else if ( numSellableItems > 0 && numAffordableItems == 0 )
                {
                    Say(true, "I can't afford anything you have.");
                }
                else
                {
                    Say(true, "You have nothing I would be interested in.");
                }
            }
        }

        public override bool OnDragDrop(Mobile from, Item dropped)
        {
            #region Honesty Item Check
            HonestyItemSocket honestySocket = dropped.GetSocket<HonestyItemSocket>();

            if (honestySocket != null)
            {
                bool gainedPath = false;

                if (honestySocket.HonestyOwner == this)
                {
                    VirtueHelper.Award(from, VirtueName.Honesty, 120, ref gainedPath);
                    from.SendMessage(gainedPath ? "You have gained a path in Honesty!" : "You have gained in Honesty.");
                    SayTo(from, 1074582); //Ah!  You found my property.  Thank you for your honesty in returning it to me.
                    dropped.Delete();
                    return true;
                }
                else
                {
                    SayTo(from, 501550, 0x3B2); // I am not interested in this.
                    return false;
                }
            }
            #endregion

            if (ConvertsMageArmor && dropped is BaseArmor && CheckConvertArmor(from, (BaseArmor)dropped))
            {
                return false;
            }

            if (dropped is SmallBOD || dropped is LargeBOD)
            {
                PlayerMobile pm = from as PlayerMobile;
                IBOD bod = dropped as IBOD;

                if (bod != null && BulkOrderSystem.NewSystemEnabled && Bribes != null && Bribes.ContainsKey(from) && Bribes[from].BOD == bod)
                {
                    if (BulkOrderSystem.CanExchangeBOD(from, this, bod, Bribes[from].Amount))
                    {
                        DoBribe(from, bod);
                        return false;
                    }
                }

                if (pm != null && pm.NextBODTurnInTime > DateTime.UtcNow)
                {
                    SayTo(from, 1079976, 0x3B2); // You'll have to wait a few seconds while I inspect the last order.
                    return false;
                }
                else if (!IsValidBulkOrder(dropped) || !SupportsBulkOrders(from))
                {
                    SayTo(from, 1045130, 0x3B2); // That order is for some other shopkeeper.
                    return false;
                }
                else if (!BulkOrderSystem.CanClaimRewards(from))
                {
                    SayTo(from, 1157083, 0x3B2); // You must claim your last turn-in reward in order for us to continue doing business.
                    return false;
                }
                else if (bod == null || !bod.Complete)
                {
                    SayTo(from, 1045131, 0x3B2); // You have not completed the order yet.
                    return false;
                }

                Item reward;
                int gold, fame;

                if (dropped is SmallBOD)
                {
                    ((SmallBOD)dropped).GetRewards(out reward, out gold, out fame);
                }
                else
                {
                    ((LargeBOD)dropped).GetRewards(out reward, out gold, out fame);
                }

                from.SendSound(0x3D);

                if (BulkOrderSystem.NewSystemEnabled && from is PlayerMobile)
                {
                    SayTo(from, 1157204, from.Name, 0x3B2); // Ho! Ho! Thank ye ~1_PLAYER~ for giving me a Bulk Order Deed!

                    BODContext context = BulkOrderSystem.GetContext(from);

                    int points = 0;
                    double banked = 0.0;

                    if (dropped is SmallBOD)
                        BulkOrderSystem.ComputePoints((SmallBOD)dropped, out points, out banked);
                    else
                        BulkOrderSystem.ComputePoints((LargeBOD)dropped, out points, out banked);

                    switch (context.PointsMode)
                    {
                        case PointsMode.Enabled:
                            context.AddPending(BODType, points);
                            from.SendGump(new ConfirmBankPointsGump((PlayerMobile)from, this, BODType, points, banked));
                            break;
                        case PointsMode.Disabled:
                            context.AddPending(BODType, points);
                            from.SendGump(new RewardsGump(this, (PlayerMobile)from, BODType, points));
                            break;
                        case PointsMode.Automatic:
                            BulkOrderSystem.SetPoints(from, BODType, banked);
                            from.SendGump(new RewardsGump(this, (PlayerMobile)from, BODType));
                            break;
                    }

                    // On EA, you have to choose the reward before you get the gold/fame reward.  IF you right click the gump, you lose
                    // the gold/fame for that bod.

                    Banker.Deposit(from, gold, true);
                }
                else
                {
                    SayTo(from, 1045132, 0x3B2); // Thank you so much!  Here is a reward for your effort.

                    if (reward != null)
                    {
                        from.AddToBackpack(reward);
                    }

                    Banker.Deposit(from, gold, true);
                }

                Titles.AwardFame(from, fame, true);

                OnSuccessfulBulkOrderReceive(from);
                Server.Engines.CityLoyalty.CityLoyaltySystem.OnBODTurnIn(from, gold);

                if (pm != null)
                {
                    pm.NextBODTurnInTime = DateTime.UtcNow + TimeSpan.FromSeconds(2.0);
                }

                dropped.Delete();
                return true;
            }
            else if (AcceptsGift(from, dropped))
            {
                dropped.Delete();
            }

            return base.OnDragDrop(from, dropped);
        }

        public bool AcceptsGift(Mobile from, Item dropped)
        {
            string name;

            if (dropped.Name != null)
            {
                if (dropped.Amount > 0)
                {
                    name = String.Format("{0} {1}", dropped.Amount, dropped.Name);
                }
                else
                {
                    name = dropped.Name;
                }
            }
            else
            {
                name = Server.Engines.VendorSearching.VendorSearch.GetItemName(dropped);
            }

            if (!String.IsNullOrEmpty(name))
            {
                PrivateOverheadMessage(MessageType.Regular, 0x3B2, true, String.Format("Thou art giving me {0}.", name), from.NetState);
            }
            else
            {
                SayTo(from, 1071971, String.Format("#{0}", dropped.LabelNumber.ToString()), 0x3B2); // Thou art giving me ~1_VAL~?
            }

            if (dropped is Gold)
            {
                SayTo(from, 501548, 0x3B2); // I thank thee.
                Titles.AwardFame(from, dropped.Amount / 100, true);

                return true;
            }

            IShopSellInfo[] info = GetSellInfo();

            foreach (IShopSellInfo ssi in info)
            {
                if (ssi.IsSellable(dropped))
                {
                    SayTo(from, 501548, 0x3B2); // I thank thee.
                    Titles.AwardFame(from, ssi.GetSellPriceFor(dropped, this) * dropped.Amount, true);

                    return true;
                }
            }

            SayTo(from, 501550, 0x3B2); // I am not interested in this.

            return false;
        }

        #region BOD Bribing
        [CommandProperty(AccessLevel.GameMaster)]
        public int BribeMultiplier { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime NextMultiplierDecay { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public DateTime WatchEnds { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public int RecentBribes { get; set; }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool UnderWatch => WatchEnds > DateTime.MinValue;

        public Dictionary<Mobile, PendingBribe> Bribes { get; set; }

        private void CheckNextMultiplierDecay(bool force = true)
        {
            int minDays = Config.Get("Vendors.BribeDecayMinTime", 25);
            int maxDays = Config.Get("Vendors.BribeDecayMaxTime", 30);

            if (force || (NextMultiplierDecay > DateTime.UtcNow + TimeSpan.FromDays(maxDays)))
                NextMultiplierDecay = DateTime.UtcNow + TimeSpan.FromDays(Utility.RandomMinMax(minDays, maxDays));
        }

        public void TryBribe(Mobile m)
        {
            if (UnderWatch)
            {
                if (WatchEnds < DateTime.UtcNow)
                {
                    WatchEnds = DateTime.MinValue;
                    RecentBribes = 0;
                }
                else
                {
                    SayTo(m, 1152293, 0x3B2); // My business is being watched by the Guild, so I can't be messing with bulk orders right now. Come back when there's less heat on me!
                    return;
                }
            }

            SayTo(m, 1152295, 0x3B2); // So you want to do a little business under the table?
            m.SendLocalizedMessage(1152296); // Target a bulk order deed to show to the shopkeeper.

            m.BeginTarget(-1, false, Server.Targeting.TargetFlags.None, (from, targeted) =>
            {
                IBOD bod = targeted as IBOD;

                if (bod is Item && ((Item)bod).IsChildOf(from.Backpack))
                {
                    if (BulkOrderSystem.CanExchangeBOD(from, this, bod, -1))
                    {
                        int amount = BulkOrderSystem.GetBribe(bod);
                        amount *= BribeMultiplier;

                        if (Bribes == null)
                            Bribes = new Dictionary<Mobile, PendingBribe>();

                        // Per EA, new bribe replaced old pending bribe
                        if (!Bribes.ContainsKey(m))
                        {
                            Bribes[m] = new PendingBribe(bod, amount);
                        }
                        else
                        {
                            Bribes[m].BOD = bod;
                            Bribes[m].Amount = amount;
                        }

                        SayTo(from, 1152292, amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US")), 0x3B2);
                        // If you help me out, I'll help you out. I can replace that bulk order with a better one, but it's gonna cost you ~1_amt~ gold coin. Payment is due immediately. Just hand me the order and I'll pull the old switcheroo.
                    }
                }
                else if (bod == null)
                {
                    SayTo(from, 1152297, 0x3B2); // That is not a bulk order deed.
                }
            });
        }

        public void DoBribe(Mobile m, IBOD bod)
        {
            BulkOrderSystem.MutateBOD(bod);

            RecentBribes++;

            if (RecentBribes >= 3 && Utility.Random(6) < RecentBribes)
            {
                WatchEnds = DateTime.UtcNow + TimeSpan.FromMinutes(Utility.RandomMinMax(120, 180));
            }

            SayTo(m, 1152303, 0x3B2); // You'll find this one much more to your liking. It's been a pleasure, and I look forward to you greasing my palm again very soon.

            if (Bribes.ContainsKey(m))
            {
                Bribes.Remove(m);
            }

            BribeMultiplier++;
            CheckNextMultiplierDecay();
        }

        #endregion

        private GenericBuyInfo LookupDisplayObject(object obj)
        {
            IBuyItemInfo[] buyInfo = GetBuyInfo();

            for (int i = 0; i < buyInfo.Length; ++i)
            {
                GenericBuyInfo gbi = (GenericBuyInfo)buyInfo[i];

                if (gbi.GetDisplayEntity() == obj)
                {
                    return gbi;
                }
            }

            return null;
        }

        private void ProcessSinglePurchase(
            BuyItemResponse buy,
            IBuyItemInfo bii,
            List<BuyItemResponse> validBuy,
            ref int controlSlots,
            ref bool fullPurchase,
            ref double cost)
        {
            int amount = buy.Amount;

            if (amount > bii.Amount)
            {
                amount = bii.Amount;
            }

            if (amount <= 0)
            {
                return;
            }

            int slots = bii.ControlSlots * amount;

            if (controlSlots >= slots)
            {
                controlSlots -= slots;
            }
            else
            {
                fullPurchase = false;
                return;
            }

            cost = (double)bii.Price * amount;
            validBuy.Add(buy);
        }

        private void ProcessValidPurchase(int amount, IBuyItemInfo bii, Mobile buyer, Container cont)
        {
            if (amount > bii.Amount)
            {
                amount = bii.Amount;
            }

            if (amount < 1)
            {
                return;
            }

            bii.Amount -= amount;

            IEntity o = bii.GetEntity();

            if (o is Item)
            {
                Item item = (Item)o;

                if (item.Stackable)
                {
                    item.Amount = amount;

                    if (cont == null || !cont.TryDropItem(buyer, item, false))
                    {
                        item.MoveToWorld(buyer.Location, buyer.Map);
                    }
                }
                else
                {
                    item.Amount = 1;

                    if (cont == null || !cont.TryDropItem(buyer, item, false))
                    {
                        item.MoveToWorld(buyer.Location, buyer.Map);
                    }

                    for (int i = 1; i < amount; i++)
                    {
                        item = bii.GetEntity() as Item;

                        if (item != null)
                        {
                            item.Amount = 1;

                            if (cont == null || !cont.TryDropItem(buyer, item, false))
                            {
                                item.MoveToWorld(buyer.Location, buyer.Map);
                            }
                        }
                    }
                }

                bii.OnBought(buyer, this, item, amount);
            }
            else if (o is Mobile)
            {
                Mobile m = (Mobile)o;

                bii.OnBought(buyer, this, m, amount);

                m.Direction = (Direction)Utility.Random(8);
                m.MoveToWorld(buyer.Location, buyer.Map);
                m.PlaySound(m.GetIdleSound());

                if (m is BaseCreature)
                {
                    ((BaseCreature)m).SetControlMaster(buyer);
                }

                for (int i = 1; i < amount; ++i)
                {
                    m = bii.GetEntity() as Mobile;

                    if (m != null)
                    {
                        m.Direction = (Direction)Utility.Random(8);
                        m.MoveToWorld(buyer.Location, buyer.Map);

                        if (m is BaseCreature)
                        {
                            ((BaseCreature)m).SetControlMaster(buyer);
                        }
                    }
                }
            }
        }

        public virtual bool OnBuyItems(Mobile buyer, List<BuyItemResponse> list)
        {
            if (!IsActiveSeller)
            {
                return false;
            }

            if (!buyer.CheckAlive())
            {
                return false;
            }

            if (!CheckVendorAccess(buyer))
            {
                Say(501522); // I shall not treat with scum like thee!
                return false;
            }

            UpdateBuyInfo();

            //var buyInfo = GetBuyInfo();
            IShopSellInfo[] info = GetSellInfo();
            double totalCost = 0.0;
            List<BuyItemResponse> validBuy = new List<BuyItemResponse>(list.Count);
            Container cont;
            bool bought = false;
            bool fromBank = false;
            bool fullPurchase = true;
            int controlSlots = buyer.FollowersMax - buyer.Followers;
            DateTime transactionTime = DateTime.UtcNow;

            foreach (BuyItemResponse buy in list)
            {
                Serial ser = buy.Serial;
                int amount = buy.Amount;
                double cost = 0;

                if (ser.IsItem)
                {
                    Item item = World.FindItem(ser);

                    if (item == null)
                    {
                        continue;
                    }

                    GenericBuyInfo gbi = LookupDisplayObject(item);

                    if (gbi != null)
                    {
                        ProcessSinglePurchase(buy, gbi, validBuy, ref controlSlots, ref fullPurchase, ref cost);
                    }
                    else if (item != BuyPack && item.IsChildOf(BuyPack))
                    {
                        if (amount > item.Amount)
                        {
                            amount = item.Amount;
                        }

                        if (amount <= 0)
                        {
                            continue;
                        }

                        foreach (IShopSellInfo ssi in info)
                        {
                            if (ssi.IsSellable(item))
                            {
                                if (ssi.IsResellable(item))
                                {
                                    cost = (double)ssi.GetBuyPriceFor(item, this) * amount;
                                    validBuy.Add(buy);
                                    break;
                                }
                            }
                        }
                    }

                    if (validBuy.Contains(buy))
                    {
                        if (ValidateBought(buyer, item))
                        {
                            totalCost += cost;
                        }
                        else
                        {
                            validBuy.Remove(buy);
                        }
                    }
                }
                else if (ser.IsMobile)
                {
                    Mobile mob = World.FindMobile(ser);

                    if (mob == null)
                    {
                        continue;
                    }

                    GenericBuyInfo gbi = LookupDisplayObject(mob);

                    if (gbi != null)
                    {
                        ProcessSinglePurchase(buy, gbi, validBuy, ref controlSlots, ref fullPurchase, ref cost);
                    }

                    if (validBuy.Contains(buy))
                    {
                        if (ValidateBought(buyer, mob))
                        {
                            totalCost += cost;
                        }
                        else
                        {
                            validBuy.Remove(buy);
                        }
                    }
                }
            } //foreach

            if (fullPurchase && validBuy.Count == 0)
            {
                SayTo(buyer, 500190, 0x3B2); // Thou hast bought nothing!
            }
            else if (validBuy.Count == 0)
            {
                SayTo(buyer, 500187, 0x3B2); // Your order cannot be fulfilled, please try again.
            }

            if (validBuy.Count == 0)
            {
                return false;
            }

            bought = buyer.AccessLevel >= AccessLevel.GameMaster;
            cont = buyer.Backpack;

            double discount = 0.0;

            if (HasHonestyDiscount)
            {
                double discountPc = 0;
                switch (VirtueHelper.GetLevel(buyer, VirtueName.Honesty))
                {
                    case VirtueLevel.Seeker:
                        discountPc = .1;
                        break;
                    case VirtueLevel.Follower:
                        discountPc = .2;
                        break;
                    case VirtueLevel.Knight:
                        discountPc = .3; break;
                    default:
                        discountPc = 0;
                        break;
                }

                discount = totalCost - (totalCost * (1.0 - discountPc));
                totalCost -= discount;
            }

            if (!bought && cont != null && ConsumeGold(cont, totalCost))
            {
                bought = true;
            }

            if (!bought)
            {
                if (totalCost <= Int32.MaxValue)
                {
                    if (Banker.Withdraw(buyer, (int)totalCost))
                    {
                        bought = true;
                        fromBank = true;
                    }
                }
                else if (buyer.Account != null && AccountGold.Enabled)
                {
                    if (buyer.Account.WithdrawCurrency(totalCost / AccountGold.CurrencyThreshold))
                    {
                        bought = true;
                        fromBank = true;
                    }
                }
            }

            if (!bought)
            {
                cont = buyer.FindBankNoCreate();

                if (cont != null && ConsumeGold(cont, totalCost))
                {
                    bought = true;
                    fromBank = true;
                }
            }

            if (!bought)
            {
                // ? Begging thy pardon, but thy bank account lacks these funds.
                // : Begging thy pardon, but thou casnt afford that.
                SayTo(buyer, totalCost >= 2000 ? 500191 : 500192, 0x3B2);

                return false;
            }

            // Add money to the Vendor's account.
            // If there is a maximum on the amount of cash-on-hand they can
            // hold, then the ledger's transaction logic will handle that.
            if (bought && totalCost != 0)
                this.Ledger.AddTransaction(
                    transactionTime, (int)Math.Round(totalCost), buyer);

            buyer.PlaySound(0x32);

            cont = buyer.Backpack ?? buyer.BankBox;

            foreach (BuyItemResponse buy in validBuy)
            {
                Serial ser = buy.Serial;
                int amount = buy.Amount;

                if (amount < 1)
                {
                    continue;
                }

                if (ser.IsItem)
                {
                    Item item = World.FindItem(ser);

                    if (item == null)
                    {
                        continue;
                    }

                    GenericBuyInfo gbi = LookupDisplayObject(item);

                    if (gbi != null)
                    {
                        ProcessValidPurchase(amount, gbi, buyer, cont);
                    }
                    else
                    {
                        if (amount > item.Amount)
                        {
                            amount = item.Amount;
                        }

                        foreach (IShopSellInfo ssi in info)
                        {
                            if (ssi.IsSellable(item))
                            {
                                if (ssi.IsResellable(item))
                                {
                                    Item buyItem;

                                    if (amount >= item.Amount)
                                    {
                                        buyItem = item;
                                    }
                                    else
                                    {
                                        buyItem = LiftItemDupe(item, item.Amount - amount);

                                        if (buyItem == null)
                                        {
                                            buyItem = item;
                                        }
                                    }

                                    if (cont == null || !cont.TryDropItem(buyer, buyItem, false))
                                    {
                                        buyItem.MoveToWorld(buyer.Location, buyer.Map);
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
                else if (ser.IsMobile)
                {
                    Mobile mob = World.FindMobile(ser);

                    if (mob == null)
                    {
                        continue;
                    }

                    GenericBuyInfo gbi = LookupDisplayObject(mob);

                    if (gbi != null)
                    {
                        ProcessValidPurchase(amount, gbi, buyer, cont);
                    }
                }
            } //foreach

            if (discount > 0)
            {
                SayTo(buyer, 1151517, discount.ToString(), 0x3B2);
            }

            if (fullPurchase)
            {
                if (buyer.AccessLevel >= AccessLevel.GameMaster)
                {
                    SayTo(
                        buyer,
                        0x3B2,
                        "I would not presume to charge thee anything.  Here are the goods you requested.",
                        null,
                        true);
                }
                else if (fromBank)
                {
                    SayTo(
                        buyer,
                        0x3B2,
                        "The total of thy purchase is {0} gold, which has been withdrawn from your bank account.  My thanks for the patronage.",
                        totalCost.ToString(),
                        true);
                }
                else
                {
                    SayTo(buyer, String.Format("The total of thy purchase is {0} gold.  My thanks for the patronage.", totalCost), 0x3B2, true);
                }
            }
            else
            {
                if (buyer.AccessLevel >= AccessLevel.GameMaster)
                {
                    SayTo(
                        buyer,
                        0x3B2,
                        "I would not presume to charge thee anything.  Unfortunately, I could not sell you all the goods you requested.",
                        null,
                        true);
                }
                else if (fromBank)
                {
                    SayTo(
                        buyer,
                        0x3B2,
                        "The total of thy purchase is {0} gold, which has been withdrawn from your bank account.  My thanks for the patronage.  Unfortunately, I could not sell you all the goods you requested.",
                        totalCost.ToString(),
                        true);
                }
                else
                {
                    SayTo(
                        buyer,
                        0x3B2,
                        "The total of thy purchase is {0} gold.  My thanks for the patronage.  Unfortunately, I could not sell you all the goods you requested.",
                        totalCost.ToString(),
                        true);
                }
            }

            return true;
        }

        public virtual bool ValidateBought(Mobile buyer, Item item)
        {
            return true;
        }

        public virtual bool ValidateBought(Mobile buyer, Mobile m)
        {
            return true;
        }

        public static bool ConsumeGold(Container cont, double amount)
        {
            return ConsumeGold(cont, amount, true);
        }

        public static bool ConsumeGold(Container cont, double amount, bool recurse)
        {
            Queue<Gold> gold = new Queue<Gold>(FindGold(cont, recurse));
            double total = gold.Aggregate(0.0, (c, g) => c + g.Amount);

            if (total < amount)
            {
                gold.Clear();

                return false;
            }

            double consume = amount;

            while (consume > 0)
            {
                Gold g = gold.Dequeue();

                if (g.Amount > consume)
                {
                    g.Consume((int)consume);

                    consume = 0;
                }
                else
                {
                    consume -= g.Amount;

                    g.Delete();
                }
            }

            gold.Clear();

            return true;
        }

        private static IEnumerable<Gold> FindGold(Container cont, bool recurse)
        {
            if (cont == null || cont.Items.Count == 0)
            {
                yield break;
            }

            if (cont is ILockable && ((ILockable)cont).Locked)
            {
                yield break;
            }

            if (cont is TrapableContainer && ((TrapableContainer)cont).TrapType != TrapType.None)
            {
                yield break;
            }

            int count = cont.Items.Count;

            while (--count >= 0)
            {
                if (count >= cont.Items.Count)
                {
                    continue;
                }

                Item item = cont.Items[count];

                if (item is Container)
                {
                    if (!recurse)
                    {
                        continue;
                    }

                    foreach (Gold gold in FindGold((Container)item, true))
                    {
                        yield return gold;
                    }
                }
                else if (item is Gold)
                {
                    yield return (Gold)item;
                }
            }
        }

        public virtual bool CheckVendorAccess(Mobile from)
        {
            GuardedRegion reg = (GuardedRegion)Region.GetRegion(typeof(GuardedRegion));

            if (reg != null && !reg.CheckVendorAccess(this, from))
            {
                return false;
            }

            if (Region != from.Region)
            {
                reg = (GuardedRegion)from.Region.GetRegion(typeof(GuardedRegion));

                if (reg != null && !reg.CheckVendorAccess(this, from))
                {
                    return false;
                }
            }

            return true;
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool DumpLedger
        {
            get
            {
                DumpLedgerImpl();
                return false;
            }

            set
            {
                if (value)
                    DumpLedgerImpl();
            }
        }

        private void DumpLedgerImpl()
        {
            List<BookPageInfo> pages =
                this.Ledger.ToBookPages();

            BaseBook theLedger;
            var rng = new Random();
            var pageCount = pages.Count;
            var writable = true;
            switch(rng.Next(0,4))
            {
                case 0: theLedger = new TanBook(pageCount, writable);   break;
                case 1: theLedger = new RedBook(pageCount, writable);   break;
                case 2: theLedger = new BlueBook(pageCount, writable);  break;
                case 3: theLedger = new BrownBook(pageCount, writable); break;
                default: return;
            }
            theLedger.Title  = "Cash Account Ledger";
            theLedger.Author = this.Name;
            for ( int i = 0; i < pageCount; i++ )
                theLedger.Pages[i] = pages[i];

            var tmpItem = this.Holding;
            this.Holding = theLedger;
            this.Drop(this.Location);
            this.Holding = tmpItem;
            Say("I have written my ledger into a book and placed it on the floor.");
        }

        /*
        private static int GetLineNumber(
            [CallerLineNumber] int lineNumber = 0)
        {
            return lineNumber;
        }
        */

        private static int GetLineNumber()
        {
            StackFrame callStack = new StackFrame(1, true);
            return callStack.GetFileLineNumber();
        }

        public virtual bool OnSellItems(Mobile seller, List<SellItemResponse> list)
        {
            if (!IsActiveBuyer)
            {
                return false;
            }

            if (!seller.CheckAlive())
            {
                return false;
            }

            if (!CheckVendorAccess(seller))
            {
                Say(501522); // I shall not treat with scum like thee!
                return false;
            }

            seller.PlaySound(0x32);

            LedgerEntry ledgerEntry;
            ledgerEntry.TransactionTime = DateTime.UtcNow;
            ledgerEntry.TransactionAmount = 0;
            ledgerEntry.OtherParty = seller;
            ledgerEntry.RunningBalance = 0;

            IShopSellInfo[] info = GetSellInfo();
            IBuyItemInfo[] buyInfo = GetBuyInfo();
            int GiveGold = 0;
            int Sold = 0;
            Container cont;

            foreach (SellItemResponse resp in list)
            {
                if (resp.Item.RootParent != seller || resp.Amount <= 0 || !resp.Item.IsStandardLoot() || !resp.Item.Movable ||
                    (resp.Item is Container && (resp.Item).Items.Count != 0))
                {
                    continue;
                }

                foreach (IShopSellInfo ssi in info)
                {
                    if (ssi.IsSellable(resp.Item))
                    {
                        Sold++;
                        break;
                    }
                }
            }

            if (Sold > MaxSell)
            {
                SayTo(seller, "You may only sell {0} items at a time!", MaxSell, 0x3B2, true);
                return false;
            }
            else if (Sold == 0)
            {
                return true;
            }

            bool tooMuchOfSomething = false;
            bool ranOutOfCash = false;
            int  numberOfSales = 0;
            int  cashOnHand =
                this.Ledger.GetCashOnHand(ledgerEntry.TransactionTime);

            foreach (SellItemResponse resp in list)
            {
                if (resp.Item.RootParent != seller || resp.Amount <= 0 || !resp.Item.IsStandardLoot() || !resp.Item.Movable ||
                    (resp.Item is Container && (resp.Item).Items.Count != 0))
                {
                    continue;
                }

                foreach (IShopSellInfo ssi in info)
                {
                    if (!ssi.IsSellable(resp.Item))
                        continue;

                    int amount = resp.Amount;

                    int singlePrice = ssi.GetSellPriceFor(resp.Item, this);
                    int stackPrice = singlePrice * amount;

                    // This would cause div-by-zero.
                    // And since we don't want to sell something for nothing,
                    // we skip these.
                    if ( singlePrice == 0 )
                        continue;

                    if (amount > resp.Item.Amount)
                    {
                        amount = resp.Item.Amount;
                    }

                    if (!ssi.IsResellable(resp.Item))
                    {
                        resp.Item.Delete();

                        // The amount < resp.Item.Amount never seems to be executed.
                        /*
                        else
                        {
                            if (amount < resp.Item.Amount)
                            {
                                resp.Item.Amount -= amount;
                            }
                            else
                            {
                                resp.Item.Delete();
                            }
                        }*/
                        // Essentially an Assertion, but we don't want to crash a server for it.
                        if ( amount < resp.Item.Amount )
                            Console.WriteLine("Warning: Thought this code was unreachable, but it was reached. --Chad "
                                +"(in Scripts/Mobiles/NPCs/BaseVendor.cs)");
                    }
                    else
                    {
                        // In the first part of this block, the vendor will
                        // determine if it can afford and if it wants to
                        // buy this item (or maybe just part of a stack).
                        int basePrice = ssi.GetBaseSellPriceFor(resp.Item);
                        int fullPrice = singlePrice;

                        bool itemWorthGoingIntoDebt =
                            GenericSellInfo.IsItemWorthGoingIntoDebt(
                                resp.Item, basePrice, fullPrice);

                        DateTime transTime = ledgerEntry.TransactionTime;
                        int maxSaleGold =
                            ssi.MaxPayForItem(
                                transTime, resp.Item, itemWorthGoingIntoDebt);

                        // Reduce stack count until vendor is willing
                        // to buy the quantity offered.
                        if ( stackPrice > maxSaleGold )
                        {
                            amount = maxSaleGold / singlePrice;
                            stackPrice = singlePrice * amount;
                            resp.Item.Amount = amount;
                            tooMuchOfSomething = true;
                            if ( amount == 0 )
                                continue;
                        }

                        if ( amount <= 1 )
                        {
                            // Reject individual items if vendor can't afford them.
                            if ( singlePrice > cashOnHand )
                            {
                                if ( !itemWorthGoingIntoDebt )
                                {
                                    ranOutOfCash = true;
                                    continue;
                                }

                                // We can go into debt if we have 10% of the item's value in cash.
                                if ( singlePrice/10 > cashOnHand )
                                {
                                    ranOutOfCash = true;
                                    continue;
                                }

                                // Sold!
                            }
                        }
                        else // amount > 1
                        {
                            // Reduce stack count until vendor can afford it.
                            if ( stackPrice > cashOnHand )
                            {
                                amount = cashOnHand / singlePrice;
                                stackPrice = singlePrice * amount;
                                resp.Item.Amount = amount;
                                ranOutOfCash = true;
                                if ( amount == 0 )
                                    continue;
                            }
                        }

                        // Commit this line-item to the sale.
                        numberOfSales++;
                        ssi.OnSold(
                            ledgerEntry.TransactionTime,
                            resp.Item.GetType(),
                            amount,
                            stackPrice);

                        ledgerEntry.TransactionAmount -= stackPrice;
                        cashOnHand -= stackPrice;

                        // Past this point it's all about determining
                        // whether the item(s) *poof* or transfer to
                        // the vendor's stock.

                        bool found = false;

                        foreach (IBuyItemInfo bii in buyInfo)
                        {
                            if (!bii.Restock(resp.Item, amount))
                                continue;

                            bii.OnSold(this, amount);

                            resp.Item.Consume(amount);
                            found = true;

                            break;
                        }

                        if (!found)
                        {
                            cont = BuyPack;

                            if (amount < resp.Item.Amount)
                            {
                                Item item = LiftItemDupe(resp.Item, resp.Item.Amount - amount);

                                if (item != null)
                                {
                                    item.SetLastMoved();
                                    cont.DropItem(item);
                                }
                                else
                                {
                                    resp.Item.SetLastMoved();
                                    cont.DropItem(resp.Item);
                                }
                            }
                            else
                            {
                                resp.Item.SetLastMoved();
                                cont.DropItem(resp.Item);
                            }
                        }
                    }

                    GiveGold += stackPrice;

                    EventSink.InvokeValidVendorSell(new ValidVendorSellEventArgs(seller, this, resp.Item, singlePrice));

                    break;
                }
            }

            if ( ranOutOfCash )
            {
                if ( numberOfSales > 0 )
                    SayTo(seller, "I couldn't afford all of that, so I just bought some of it.");
                else
                    SayTo(seller, "I can't afford any of that.");
            }
            else
            if ( tooMuchOfSomething )
                SayTo(seller, "I couldn't buy all of that; I have enough now.");

            if ( ledgerEntry.TransactionAmount != 0 )
                this.Ledger.AddTransaction(ledgerEntry);

            /*
            if ( seller.AccessLevel == AccessLevel.GameMaster
            ||   seller.AccessLevel == AccessLevel.Administrator )
                SayTo(seller, "My cash on hand is now {0}",
                    this.Ledger.GetCashOnHand(ledgerEntry.TransactionTime));
            */

            if (GiveGold > 0)
            {
                while (GiveGold > 60000)
                {
                    seller.AddToBackpack(new Gold(60000));
                    GiveGold -= 60000;
                }

                seller.AddToBackpack(new Gold(GiveGold));

                seller.PlaySound(0x0037); //Gold dropping sound

                if (SupportsBulkOrders(seller))
                {
                    Item bulkOrder = CreateBulkOrder(seller, false);

                    if (bulkOrder is LargeBOD)
                    {
                        seller.SendGump(new LargeBODAcceptGump(seller, (LargeBOD)bulkOrder));
                    }
                    else if (bulkOrder is SmallBOD)
                    {
                        seller.SendGump(new SmallBODAcceptGump(seller, (SmallBOD)bulkOrder));
                    }
                }
            }
            //no cliloc for this?
            //SayTo( seller, true, "Thank you! I bought {0} item{1}. Here is your {2}gp.", Sold, (Sold > 1 ? "s" : ""), GiveGold );

            return true;
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            // Dawdy World customization:
            // We'll just count in thousands; each thousand-version will have
            // a corresponding upstream (official) version that will correspond,
            // which will be in the less significant digits (so the official
            // releases could get up to version 999 before we have a collision).
            writer.Write(1003); // version

            // Version 1003  (Viking 1000 + ServUO 3)
            this.Ledger.Serialize(writer);

            // Version 3
            // Version 2
            writer.Write(BribeMultiplier);
            writer.Write(NextMultiplierDecay);
            writer.Write(RecentBribes);

            List<SBInfo> sbInfos = SBInfos;

            for (int i = 0; sbInfos != null && i < sbInfos.Count; ++i)
            {
                SBInfo sbInfo = sbInfos[i];
                List<GenericBuyInfo> buyInfo = sbInfo.BuyInfo;

                for (int j = 0; buyInfo != null && j < buyInfo.Count; ++j)
                {
                    GenericBuyInfo gbi = buyInfo[j];

                    int maxAmount = gbi.MaxAmount;
                    int doubled = 0;
                    int bought = gbi.TotalBought;
                    int sold = gbi.TotalSold;

                    switch (maxAmount)
                    {
                        case 40:
                            doubled = 1;
                            break;
                        case 80:
                            doubled = 2;
                            break;
                        case 160:
                            doubled = 3;
                            break;
                        case 320:
                            doubled = 4;
                            break;
                        case 640:
                            doubled = 5;
                            break;
                        case 999:
                            doubled = 6;
                            break;
                    }

                    if (doubled > 0 || bought > 0 || sold > 0)
                    {
                        writer.WriteEncodedInt(1 + ((j * sbInfos.Count) + i));
                        writer.WriteEncodedInt(doubled);
                        writer.WriteEncodedInt(bought);
                        writer.WriteEncodedInt(sold);
                    }
                }
            }

            writer.WriteEncodedInt(0);

            if (NextMultiplierDecay != DateTime.MinValue && NextMultiplierDecay < DateTime.UtcNow)
            {
                Timer.DelayCall(TimeSpan.FromSeconds(10), () =>
                {
                    if (BribeMultiplier > 0)
                        BribeMultiplier /= 2;

                    CheckNextMultiplierDecay();
                });
            }
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();

            LoadSBInfo();

            List<SBInfo> sbInfos = SBInfos;

            switch (version)
            {
                case 1003: // Viking 1000 + ServUO 3
                    this.Ledger.Deserialize(reader, this);
                    goto case 3; // Version 1003 is based on version 3.

                case 3:
                case 2:
                    BribeMultiplier = reader.ReadInt();
                    NextMultiplierDecay = reader.ReadDateTime();
                    CheckNextMultiplierDecay(false); // Reset NextMultiplierDecay if it is out of range of the config
                    RecentBribes = reader.ReadInt();
                    goto case 1;
                case 1:
                    {
                        int index;

                        while ((index = reader.ReadEncodedInt()) > 0)
                        {
                            int doubled = reader.ReadEncodedInt();
                            int bought = 0;
                            int sold = 0;

                            if (version >= 3)
                            {
                                bought = reader.ReadEncodedInt();
                                sold = reader.ReadEncodedInt();
                            }

                            if (sbInfos != null)
                            {
                                index -= 1;
                                int sbInfoIndex = index % sbInfos.Count;
                                int buyInfoIndex = index / sbInfos.Count;

                                if (sbInfoIndex >= 0 && sbInfoIndex < sbInfos.Count)
                                {
                                    SBInfo sbInfo = sbInfos[sbInfoIndex];
                                    List<GenericBuyInfo> buyInfo = sbInfo.BuyInfo;

                                    if (buyInfo != null && buyInfoIndex >= 0 && buyInfoIndex < buyInfo.Count)
                                    {
                                        GenericBuyInfo gbi = buyInfo[buyInfoIndex];

                                        int amount = 20;

                                        switch (doubled)
                                        {
                                            case 0:
                                                break;
                                            case 1:
                                                amount = 40;
                                                break;
                                            case 2:
                                                amount = 80;
                                                break;
                                            case 3:
                                                amount = 160;
                                                break;
                                            case 4:
                                                amount = 320;
                                                break;
                                            case 5:
                                                amount = 640;
                                                break;
                                            case 6:
                                                amount = 999;
                                                break;
                                        }

                                        if (version == 2 && gbi.Stackable)
                                        {
                                            gbi.Amount = gbi.MaxAmount = BaseVendor.EconomyStockAmount;
                                        }
                                        else
                                        {
                                            gbi.Amount = gbi.MaxAmount = amount;
                                        }

                                        gbi.TotalBought = bought;
                                        gbi.TotalSold = sold;
                                    }
                                }
                            }
                        }

                        break;
                    }
            }

            if (IsParagon)
            {
                IsParagon = false;
            }

            if (version == 1)
            {
                BribeMultiplier = Utility.Random(10);
            }

            Timer.DelayCall(TimeSpan.Zero, CheckMorph);
        }

        public override void AddCustomContextEntries(Mobile from, List<ContextMenuEntry> list)
        {
            if (ConvertsMageArmor)
            {
                list.Add(new UpgradeMageArmor(from, this));
            }

            if (from.Alive && IsActiveVendor)
            {
                if (SupportsBulkOrders(from))
                {
                    list.Add(new BulkOrderInfoEntry(from, this));

                    if (BulkOrderSystem.NewSystemEnabled)
                    {
                        list.Add(new BribeEntry(from, this));
                        list.Add(new ClaimRewardsEntry(from, this));
                    }
                }

                if (IsActiveSeller)
                {
                    list.Add(new VendorBuyEntry(from, this));
                }

                if (IsActiveBuyer)
                {
                    list.Add(new VendorSellEntry(from, this));
                }
            }

            base.AddCustomContextEntries(from, list);
        }

        public virtual IShopSellInfo[] GetSellInfo()
        {
            return (IShopSellInfo[])m_ArmorSellInfo.ToArray(typeof(IShopSellInfo));
        }

        public virtual IBuyItemInfo[] GetBuyInfo()
        {
            return (IBuyItemInfo[])m_ArmorBuyInfo.ToArray(typeof(IBuyItemInfo));
        }

        #region Mage Armor Conversion
        public virtual bool ConvertsMageArmor => false;

        private readonly List<PendingConvert> _PendingConvertEntries = new List<PendingConvert>();

        private bool CheckConvertArmor(Mobile from, BaseArmor armor)
        {
            PendingConvert convert = GetConvert(from, armor);

            if (convert == null || !(from is PlayerMobile))
                return false;

            object state = convert.Armor;

            RemoveConvertEntry(convert);
            from.CloseGump(typeof(Server.Gumps.ConfirmCallbackGump));

            from.SendGump(new Server.Gumps.ConfirmCallbackGump((PlayerMobile)from, 1049004, 1154115, state, null,
                (m, obj) =>
                {
                    BaseArmor ar = obj as BaseArmor;

                    if (!Deleted && ar != null && armor.IsChildOf(m.Backpack) && CanConvertArmor(m, ar))
                    {
                        if (!InRange(m.Location, 3))
                        {
                            m.SendLocalizedMessage(1149654); // You are too far away.
                        }
                        else if (!Banker.Withdraw(m, 250000, true))
                        {
                            m.SendLocalizedMessage(1019022); // You do not have enough gold.
                        }
                        else
                        {
                            ConvertMageArmor(m, ar);
                        }
                    }
                },
                (m, obj) =>
                {
                    PendingConvert con = GetConvert(m, armor);

                    if (con != null)
                    {
                        RemoveConvertEntry(con);
                    }
                }));

            return true;
        }

        protected virtual bool CanConvertArmor(Mobile from, BaseArmor armor)
        {
            if (armor == null || armor is BaseShield/*|| armor.ArtifactRarity != 0 || armor.IsArtifact*/)
            {
                from.SendLocalizedMessage(1113044); // You can't convert that.
                return false;
            }

            if (armor.ArmorAttributes.MageArmor == 0 &&
                Server.SkillHandlers.Imbuing.GetTotalMods(armor) > 4)
            {
                from.SendLocalizedMessage(1154119); // This action would exceed a stat cap
                return false;
            }

            return true;
        }

        public void TryConvertArmor(Mobile from, BaseArmor armor)
        {
            if (CanConvertArmor(from, armor))
            {
                from.SendLocalizedMessage(1154117); // Ah yes, I will convert this piece of armor but it's gonna cost you 250,000 gold coin. Payment is due immediately. Just hand me the armor.

                PendingConvert convert = GetConvert(from, armor);

                if (convert != null)
                {
                    convert.ResetTimer();
                }
                else
                {
                    _PendingConvertEntries.Add(new PendingConvert(from, armor, this));
                }
            }
        }

        public virtual void ConvertMageArmor(Mobile from, BaseArmor armor)
        {
            if (armor.ArmorAttributes.MageArmor > 0)
                armor.ArmorAttributes.MageArmor = 0;
            else
                armor.ArmorAttributes.MageArmor = 1;

            from.SendLocalizedMessage(1154118); // Your armor has been converted.
        }

        private void RemoveConvertEntry(PendingConvert convert)
        {
            _PendingConvertEntries.Remove(convert);

            if (convert.Timer != null)
            {
                convert.Timer.Stop();
            }
        }

        private PendingConvert GetConvert(Mobile from, BaseArmor armor)
        {
            return _PendingConvertEntries.FirstOrDefault(c => c.From == from && c.Armor == armor);
        }

        protected class PendingConvert
        {
            public Mobile From { get; set; }
            public BaseArmor Armor { get; set; }
            public BaseVendor Vendor { get; set; }

            public Timer Timer { get; set; }
            public DateTime Expires { get; set; }

            public bool Expired => DateTime.UtcNow > Expires;

            public PendingConvert(Mobile from, BaseArmor armor, BaseVendor vendor)
            {
                From = from;
                Armor = armor;
                Vendor = vendor;

                ResetTimer();
            }

            public void ResetTimer()
            {
                if (Timer != null)
                {
                    Timer.Stop();
                    Timer = null;
                }

                Expires = DateTime.UtcNow + TimeSpan.FromSeconds(120);

                Timer = Timer.DelayCall(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), OnTick);
                Timer.Start();
            }

            public void OnTick()
            {
                if (Expired)
                {
                    Vendor.RemoveConvertEntry(this);
                }
            }
        }
        #endregion
    }
}

namespace Server.ContextMenus
{
    public class VendorBuyEntry : ContextMenuEntry
    {
        private readonly BaseVendor m_Vendor;

        public VendorBuyEntry(Mobile from, BaseVendor vendor)
            : base(6103, 8)
        {
            m_Vendor = vendor;
            Enabled = vendor.CheckVendorAccess(from);
        }

        public override void OnClick()
        {
            m_Vendor.VendorBuy(Owner.From);
        }
    }

    public class VendorSellEntry : ContextMenuEntry
    {
        private readonly BaseVendor m_Vendor;

        public VendorSellEntry(Mobile from, BaseVendor vendor)
            : base(6104, 8)
        {
            m_Vendor = vendor;
            Enabled = vendor.CheckVendorAccess(from);
        }

        public override void OnClick()
        {
            m_Vendor.VendorSell(Owner.From);
        }
    }

    public class UpgradeMageArmor : ContextMenuEntry
    {
        public Mobile From { get; set; }
        public BaseVendor Vendor { get; set; }

        public UpgradeMageArmor(Mobile from, BaseVendor vendor)
            : base(1154114) // Convert Mage Armor
        {
            Enabled = vendor.CheckVendorAccess(from);

            From = from;
            Vendor = vendor;
        }

        public override void OnClick()
        {
            From.Target = new InternalTarget(From, Vendor);
            From.SendLocalizedMessage(1154116); // Target a piece of armor to show to the guild master.
        }

        private class InternalTarget : Target
        {
            public Mobile From { get; set; }
            public BaseVendor Vendor { get; set; }

            public InternalTarget(Mobile from, BaseVendor vendor)
                : base(1, false, TargetFlags.None)
            {
                From = from;
                Vendor = vendor;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                if (targeted is BaseArmor)
                {
                    BaseArmor armor = (BaseArmor)targeted;
                    Vendor.TryConvertArmor(from, armor);
                }
            }
        }
    }

    public struct LedgerEntry
    {
        public  DateTime  TransactionTime;
        public  int       TransactionAmount;
        public  Mobile    OtherParty;
        public  int       RunningBalance;

        public string ToString()
        {
            return String.Format(
                "{{TransactionTime: {0:yyyy-MM-dd.HH:mm:ss}, TransactionAmount: {1}, OtherParty: {2}, RunningBalance: {3}}}",
                TransactionTime, TransactionAmount, OtherParty, RunningBalance);
        }
    }

    public sealed class VendorLedger
    {
        BaseVendor parent;

        // Cash-on-hand parameters. All cash is measured in gold coins.
        public static int TargetCashOnHandLow   = Config.Get("Vendors.TargetCashOnHandLow", 8000);
        public static int TargetCashOnHandHigh  = Config.Get("Vendors.TargetCashOnHandHigh", 24000);

        public static int MaxDailyCashInflow    = Config.Get("Vendors.MaxDailyCashInflow", 4800);
        public static int MaxDailyCashOutflow   = Config.Get("Vendors.MaxDailyCashOutflow", 2400);

        public static int HoursSpreadOnCashDays = Config.Get("Vendors.HoursSpreadOnCashDays", 2);

        public static int MaxCashOnHandLow      = Config.Get("Vendors.MaxCashOnHandLow", 40000);
        public static int MaxCashOnHandHigh     = Config.Get("Vendors.MaxCashOnHandHigh", 60000);

        public static int MinDaysBeforeMaxCashReset = Config.Get("Vendors.MinDaysBeforeMaxCashReset", 7);
        public static int MaxDaysBeforeMaxCashReset = Config.Get("Vendors.MaxDaysBeforeMaxCashReset", 14);

        public const  int NumDaysCashSimBeforeDormancy = 3;
        public const  int NumSimulatedCashTransacts = 7;

        // Useful for avoiding this calculation during construction/deserialization
        // of instances that don't already have it.
        private static  DateTime  serverStartTime = DateTime.UtcNow;

        // Cash-on-hand working values.

        // The transactions that this vendor has made. This includes all
        // buys/sells with players, as well as simulated "transactions" that
        // keep the vendor's assets from deflating too hard or inflating
        // without bound.
        private List<LedgerEntry>   m_Ledger = new List<LedgerEntry>();

        // This is the vendor's cash-on-hand before the first ledger entry.
        private int                 m_StartingBalance = 0;

        // The maximum amount of cash a vendor can have, even if they sell
        // a lot of stuff to players.
        private int                 m_CurrentMaxCash = MaxCashOnHandLow;

        // The maximum amount of cash a vendor can have is randomized.
        // But it changes occasionally to prevent the world from being
        // too predictable.
        // This variable encodes when we will change it again.
        private DateTime            m_NextMaxCashReset;

        // The current cash-flow simulation.
        // This might contain transactions that have yet to play out,
        // or it could be far in the past, in which case we might
        // need to finish it and run more simulations to catch the
        // vendor up to present-day-present-time.
        private CashFlowSimulation  m_CashFlowSim = null;

        public VendorLedger(BaseVendor parent)
        {
            this.parent = parent;

            // Ensure that this contains a valid value.
            // Try to do it without calling DateTime.UtcNow (or any other
            // manner of peeking at the clock) for every instance because
            // that operation might be very expensive and we might be
            // deserializing thousands of these things on server startup.
            m_NextMaxCashReset = serverStartTime;
        }

        public int GetCashOnHand(DateTime when, bool simulationNeeded = true)
        {
            // Check for vendors that aren't in the economy yet. Fix that.
            // This must be done even if simulationNeeded == false, because
            // later code must assume that there is a non-null m_CashFlowSim
            // object attached to this vendor.
            if ( m_CashFlowSim == null )
                this.InitializeCashSimulations(when);
            else // Vendor is already in the economy.
            {
                if ( simulationNeeded )
                    this.UpdateCashSimulation(when);
            }

            if ( m_Ledger.Count > 0 )
            {
                // Use ReverseFindEntryBeforeTime to wind back time until
                // the next ledger entry that's before 'when'.
                // This is more appropriate than forward-find because
                // this method is more likely to be called 
                int ledgerIdx = ReverseFindEntryBeforeTime(m_Ledger, when);
                if ( ledgerIdx >= 0 )
                    return m_Ledger[ledgerIdx].RunningBalance;
                else
                    return m_StartingBalance; // 'when' is sometime before the start of the ledger.
            }
            else
            {
                // Normally the cash simulation will create ledger entries
                // and the other branch will be taken.
                // It is possible, however, that this vendor just started
                // simulating, and its first daily simulation was randomly
                // placed very close to DateTime.UtcNow. Then, its simulation
                // randomly placed all transactions somewhere at least
                // several hours in advance of the simulation start datetime,
                // and thus in the future of DateTime.UtcNow. If all of the
                // simulated transactions are in the future, and the vendor
                // hasn't recorded any player transactions in its ledger
                // yet, then it is /possible/ that there are NO ledger
                // entries by the time we execute this code.
                // No problem though: we just return our starting balance,
                // which should have already been initialized by
                // this.InitializeCashSimulations() above.
                return m_StartingBalance;
            }
        }

        /// Code that should run only one time for every vendor ever.
        /// Once this has been run, the vendor will have a beginning
        /// balance, and everything needed to recreate their current simulation
        /// is serialized.
        private void InitializeCashSimulations(DateTime when)
        {
            var rng = new Random();
            this.m_StartingBalance =
                rng.Next(TargetCashOnHandLow, TargetCashOnHandHigh);

            // Randomize the first simulation's start-time so that players
            // don't have the power to influence when vendors start their
            // cashflow cycles.
            // We also guarantee that the simulation starts in the past,
            // hence the random interval starts at 1, not 0.
            double minutesOffset = rng.Next(1, 60 * 24);
            minutesOffset = -minutesOffset;
            var simStart = when.AddMinutes(minutesOffset);

            this.m_CashFlowSim = new CashFlowSimulation(
                parent,
                simStart,
                this.m_StartingBalance,
                NumSimulatedCashTransacts);

            // A full update with UpdateCashSimulation is not necessary because
            // we are only initializing vendors with a random number and
            // some random part of one day of cash flow simulation. Thus, all
            // of that is guaranteed to occur within the same day and will
            // not require additional simulations, as is otherwise required
            // when catching up history after some activity+downtime.
            this.MergeCashSimulationLedger(when);
        }

        /// Runs daily cashflow simulations until the given 'upTo' date.
        /// If there is a period of dormancy (cash-on-hand is within target
        /// bounds and no player activity occurs), then m_StartingBalance
        /// will be randomized to some number within the target bounds and
        /// no subsequent simulations will occur.
        private void UpdateCashSimulation(DateTime  upTo)
        {
            int idleDays = 0;
            this.MergeCashSimulationLedger(upTo);

            while ( this.m_CashFlowSim.EndTime < upTo )
            {
                int latestLedgerInSimIdx =
                    ForwardFindEntryBeforeTime(this.m_Ledger, this.m_CashFlowSim.EndTime);
                var nextSimStartTime = this.m_CashFlowSim.EndTime;
                var nextSimStartingCash = this.GetCashOnHand(nextSimStartTime, false);

                // Only start idle-counting when the vendor reaches their target cash-on-hand.
                if ( TargetCashOnHandLow <= nextSimStartingCash 
                &&       nextSimStartingCash < TargetCashOnHandHigh )
                {
                    // Count idle days.
                    // E.g. there is no point simulating a bunch of transactions
                    // for a vendor that hit a reasonable cash-on-hand and then
                    // never saw anyone for 1000 days.

                    // Value returned from ForwardFindEntryBeforeTime indicates
                    // that all ledger entries are before 'upTo'. This is
                    // effectively a check to ensure that there was no player-
                    // driven activity since the end of the last daily sim.
                    if ( latestLedgerInSimIdx + 1 < m_Ledger.Count )
                        idleDays++;
                    /*
                    // This logic is commented out because it implies that a
                    // non-simulated transaction happened AFTER the daily sim
                    // in the last iteration of this loop. That is an indication
                    // of activity (ex: player buy/sell), not dormancy.
                    else
                    if ( latestLedgerInSimIdx + 1 < m_Ledger.Count
                    &&   m_Ledger[latestLedgerInSimIdx+1].TransactionTime > m_CashFlowSim.EndTime )
                        idleDays++;
                    */

                    if ( idleDays >= NumDaysCashSimBeforeDormancy )
                        break;
                }
                else
                    idleDays = 0; // Matters when/if a simulation overshoots. Not sure if that should be allowed.

                this.m_CashFlowSim =
                    new CashFlowSimulation(
                        parent,
                        nextSimStartTime,
                        nextSimStartingCash,
                        NumSimulatedCashTransacts);

                this.MergeCashSimulationLedger(upTo);
            }

            if ( idleDays >= NumDaysCashSimBeforeDormancy )
            {
                // Properly randomize the vendor's cash-on-hand.
                var rng = new Random();
                this.m_StartingBalance =
                    rng.Next(TargetCashOnHandLow, TargetCashOnHandHigh);
                this.m_Ledger.Clear();
            }
        }

        /// Returns the index of the LedgerEntry in the given ledger that
        /// directly precedes (or coincides with) the time given by 'theTime'.
        /// Do it by starting at the beginning of the list and walking forwards.
        /// If 'startAt' is negative, it will be clipped to 0 to prevent out-of-range
        /// exceptions from being thrown.
        /// If 'startAt' is greater than (ledger.Count-1), all entries will be
        /// assumed to be before (earlier) than 'theTime'.
        /// Returns -1 if all entries are past (after) 'theTime'.
        /// If all entries are before (or coincident with) 'theTime', then
        /// the index of the last entry is returned.
        /// The ledger is assumed to be sorted by .TransactionTime
        private static int ForwardFindEntryBeforeTime(List<LedgerEntry> ledger, DateTime theTime, int startAt = 0)
        {
            LedgerEntry e;

            int i = startAt;
            if ( i < 0 ) // Clip to 0 to prevent out-of-bounds issues.
                i = 0;
            else
            if ( i >= ledger.Count ) // This also prevents out-of-bounds issues.
                return ledger.Count-1;
            // Now we can assume that 'i' is valid for dereferencing an element.

            e = ledger[i];
            if ( e.TransactionTime > theTime )
            {
                // In this case, given the sortedness of 'ledger',
                // there will be no entries less than 'theTime'.
                return -1;
            }
            i++;

            for(; i < ledger.Count; i++)
            {
                e = ledger[i];
                if ( e.TransactionTime > theTime )
                    return i-1;
            }

            // All entries are before 'theTime', so just return the last one.
            return ledger.Count - 1;
        }

        /// Like ForwardFindEntryBeforeTime, but starts at the last entry and
        /// walks backwards. This could be much faster in situations where
        /// you know that the target entry is likely near the end of the ledger.
        /// Otherwise the forward-find will, of course, be faster for entries
        /// likely to be near the front (earliest part) of the ledger.
        ///
        /// The 'startAt' parameter defaults to (ledger.Count - 1).
        /// If the 'startAt' parameter is chosen to be something greater than
        /// (ledger.Count-1), it will be clipped to prevent out-of-range
        /// exceptions from occuring.
        /// If the 'startAt' parameter is negative, this will assume that all
        /// entries are past 'theTime'.
        /// Returns -1 if all entries are past (after) 'theTime'.
        /// If all entries are before (or coincident with) 'theTime', then
        /// the index of the last entry is returned.
        ///
        /// The ledger is assumed to be sorted by .TransactionTime
        private static int ReverseFindEntryBeforeTime(
            List<LedgerEntry>  ledger,
            DateTime           theTime)
        {
            return ReverseFindEntryBeforeTime(ledger, theTime, ledger.Count - 1);
        }

        /// ditto
        private static int ReverseFindEntryBeforeTime(
            List<LedgerEntry>  ledger,
            DateTime           theTime,
            int                startAt)
        {
            // Clip choice of 'startAt' index to within the list.
            if ( startAt >= ledger.Count)
                startAt = ledger.Count - 1;
            // The loop's condition ensures that negative values don't
            // attempt to dereference.

            // Offset by 1 to make it possible to avoid duplicating part of the
            // loop in the initialization.
            int i = startAt+1;
            while(i > 0)
            {
                i--;
                LedgerEntry e = ledger[i];
                if ( e.TransactionTime < theTime )
                    return i;
            }

            // All entries are past/after 'theTime', so return -1.
            return -1;
        }
        
        /// This method merges all of the simulation ledger entries into the
        /// vendor's proper ledger (m_Ledger).
        ///
        /// This method is idempotent: if it is ran more than once under the
        /// same condition, then only the first run will make changes.
        /// Thus, it can be ran repeatedly at different times in the day
        /// whenever the vendor's ledger needs to be updated, without worrying
        /// about creating duplicate entries. It has an O(n) time complexity
        /// where n is the number of total ledger entries, so it should not
        /// be ran more than once per world event, simply to keep things
        /// responsive and performant.
        ///
        /// Note that unlike the static MergeLedgerInPlace method, this will
        /// first truncate unnecessary ledger entries and update the
        /// starting balance.
        private void MergeCashSimulationLedger(DateTime upTo)
        {
            // The whole point of this is to merge the cash sim,
            // which is a problem if there's no cash simulation. (There should be, though.)
            if ( m_CashFlowSim == null )
            {
                // Other code will have initialized m_CashFlowSim by now.
                // So, this shouldn't happen, but it's good to handle exceptions and write assertions.
                this.m_Ledger = new List<LedgerEntry>();
                return;
            }

            // Remove outdated ledger entries while updating the starting balance.
            // This will make the merge potentially much more efficient,
            // especially if we get called repeatedly on the same ledgers.
            this.TruncateLedger();

            // Now do what we're here for.
            MergeLedgerInPlace(
                ref this.m_Ledger,
                this.m_CashFlowSim.Ledger,
                upTo,
                this.m_StartingBalance);
        }

        /// This method is idempotent and will not insert an entry in the 'from'
        /// ledger into the 'into' ledger if there is already an equivalent entry
        /// present (same time, transaction amount, and OtherParty).
        ///
        /// It is assumed that both ledgers are sorted by TransactionTime.
        /// 'ledgerInto' will still be sorted after this method returns.
        ///
        private static void MergeLedgerInPlace(
            ref List<LedgerEntry>  intoLedger,
            List<LedgerEntry>      fromLedger,
            DateTime               upTo,
            int                    startingBalance)
        {
            // Start at the most-future 'from' transaction and move backwards
            // until we hit an entry that's before 'upTo'. This might happen
            // immediately. Regardless, this will give us an upper-bound for
            // the amount of entries that could be added to the vendor's
            // own ledger.
            int mostRecentFromLedgerIdx =
                ReverseFindEntryBeforeTime(fromLedger, upTo);
            /*int mostRecentFromLedgerIdx = fromLedger.Length - 1;
            for (; mostRecentFromLedgerIdx >= 0; mostRecentFromLedgerIdx-- )
                if ( upTo >= fromLedger[mostRecentFromLedgerIdx].TransactionTime )
                    break;*/

            // mostRecentFromLedgerIdx is -1 if there were NO 'from' ledger
            // entries before the given 'upTo' date. In that case, there's
            // nothing to add to our ledger yet, and we can just exit
            // this function.
            if ( mostRecentFromLedgerIdx < 0 )
                return;

            // We'll need a blank LedgerEntry.
            // Pulling entries from the lists isn't
            // good for this because either one might be empty.
            LedgerEntry blank;
            blank.TransactionTime    = upTo;
            blank.TransactionAmount  = 0;
            blank.OtherParty         = null;
            blank.RunningBalance     = 0;

            // Create filler at the end of the ledger list.
            // Extra space is going to be needed for efficient merging without
            // unnecessary allocations.
            int oldLedgerLen = intoLedger.Count;
            int maxPossibleAdds = mostRecentFromLedgerIdx + 1;
            for ( int i = 0; i < maxPossibleAdds; i++ )
                intoLedger.Add(blank);

            // Shift the list right-ward to move the space at the end of the
            // list to at the beginning of the list. We need this, and we can't
            // just work backwards, because every ledger entry keeps a running
            // balance that depends on the previous entry. We won't know that
            // unless we are moving front-to-back.
            int j = oldLedgerLen;
            while ( j > 0 )
            {
                j--;
                intoLedger[j + maxPossibleAdds] = intoLedger[j];
            }

            // Now we will be generating a new ledger list while reading from
            // two sources: the existing ledger list and the 'from' ledger.
            // It just so happens that the new ledger list and the old
            // ledger list cohabitate the same "List<LedgerEntry>" object,
            // which allows us to avoid allocating another one. This is
            // possible by the aforementioned extra space at the beginning
            // of the list: we can treat the whole List object as empty because
            // we will never be able to overwrite any entries that are
            // yet-to-be-visited. Also we don't care about losing the original
            // list as long as the new one has at least those entries.
            int srcIndex  = maxPossibleAdds;
            int dstIndex  = 0;
            int fromIndex = 0;
            LedgerEntry previousOutput;
            previousOutput.RunningBalance = startingBalance;
            while (srcIndex < intoLedger.Count || fromIndex < maxPossibleAdds)
            {
                LedgerEntry nextOutput;

                if ( fromIndex >= fromLedger.Count )
                {
                    nextOutput = intoLedger[srcIndex];
                    srcIndex++;
                }
                else
                if ( srcIndex  >= intoLedger.Count )
                {
                    nextOutput = fromLedger[fromIndex];
                    fromIndex++;
                }
                else
                {
                    LedgerEntry nextExisting = intoLedger[srcIndex];
                    LedgerEntry nextFrom     = fromLedger[fromIndex];

                    if ( nextExisting.TransactionTime < nextFrom.TransactionTime )
                    {
                        // An existing ledger entry is chronologically next.
                        nextOutput = nextExisting;
                        srcIndex++;
                    }
                    else
                    if ( nextExisting.TransactionTime > nextFrom.TransactionTime )
                    {
                        // A 'from' ledger entry is chronologically next.
                        nextOutput = nextFrom;
                        fromIndex++;
                    }
                    else // Equal transaction times.
                    {
                        if (nextExisting.TransactionAmount == nextFrom.TransactionAmount
                        &&  nextExisting.OtherParty        == nextFrom.OtherParty )
                        {
                            // To fulfill property of idempotency, we don't insert
                            // any entries that already exist.
                            fromIndex++; // Skip the sim entry.
                            continue; // Do not output into the resulting ledger.
                        }
                        else
                        {
                            // Corner case: simultaneous but different entries.
                            // In this case, it doesn't matter which one is handled
                            // first.
                            nextOutput = nextExisting;
                            srcIndex++;
                        }
                    }
                }

                // Note that negative balances are allowed. (Vendors can go into debt.)
                nextOutput.RunningBalance = previousOutput.RunningBalance + nextOutput.TransactionAmount;
                intoLedger[dstIndex] = nextOutput;
                dstIndex++;
                previousOutput = nextOutput;
            }

            // We may have overestimated how much larger we needed to make the
            // ledger. If that's the case, adjust it shorter by removing the
            // redundant/blank entries at the end of the list.
            if ( dstIndex < intoLedger.Count )
                intoLedger.RemoveRange(dstIndex, intoLedger.Count - dstIndex);
        }

        /// Removes all ledger entries that precede the current
        /// cash simulation's start time, while updating this vendor's
        /// starting balance. These entries shouldn't be needed for anything;
        /// as of this writing the ledger is just used to integrate
        /// player transactions with (currently) simulated transactions,
        /// so it isn't necessary to remember any transactions that aren't
        /// interspersed with (currently) simulated ones.
        /// Assumption: ledger must be sorted by TransactionTime
        /// for this algorithm to work correctly.
        private void TruncateLedger()
        {
            // The whole point of this is to truncate up to the cash sim's start,
            // which is a problem if there's no cash simulation. (There should be, though.)
            if ( m_CashFlowSim == null )
            {
                // Other code will have initialized m_CashFlowSim by now.
                // So, this shouldn't happen, but it's good to handle exceptions and write assertions.
                this.m_Ledger = new List<LedgerEntry>();
                return;
            }

            // There's a current valid daily simulation, so truncate
            // everything up to the start of that sim.
            m_StartingBalance = TruncateLedger(
                ref m_Ledger,  m_CashFlowSim.StartTime,  m_StartingBalance);
        }

        /// Removes all ledger entries that chronologically precede 'upTo'.
        /// Returns the new starting balance, which is also the running balance
        /// of the last truncated entry is returned. If there were no entries,
        /// or no truncation occurred, then the value of the 'startingBalance'
        /// argument is returned.
        /// (The 'startingBalance' parameter otherwise does not affect this
        /// calculation, because the running balance of the last truncated
        /// entry is otherwise used as the return value.)
        /// Assumption: ledger must be sorted by TransactionTime
        /// for this algorithm to work correctly.
        private static int TruncateLedger(ref List<LedgerEntry> ledger, DateTime upTo, int startingBalance)
        {
            // This shouldn't happen, but we'll be paranoid and plan for it regardless.
            if ( ledger == null )
                ledger = new List<LedgerEntry>();

            // This isn't just an optimization: it allows later code to assume
            // that there are elements in this array.
            if ( ledger.Count == 0 )
                return startingBalance;

            // Assumption: ledger must be sorted by TransactionTime
            // for this algorithm to work correctly.
            int oldIdx = ForwardFindEntryBeforeTime(ledger, upTo);

            // By checking this, later code can assume that there is at least
            // one entry in the list that will be truncated (e.g. one "old" entry).
            if ( oldIdx < 0 )
                return startingBalance; // Nothing to truncate.

            /* First draft; less elegant code that doesn't use the forward-find method.
            // Assumption: ledger must be sorted by TransactionTime
            // for this algorithm to work correctly.
            var truncateUpTo = upTo;
            int oldIdx = 0;
            while ( oldIdx < ledger.Count && ledger[oldIdx].TransactionTime < truncateUpTo )
                oldIdx++;

            // By checking this, later code can assume that there is at least
            // one entry in the list that will be truncated (e.g. one "old" entry).
            if ( oldIdx == 0 )
                return startingBalance; // Nothing to truncate.
            */

            // Update the beginning balance.
            // This must be done BEFORE the next step, because we are
            // about to overwrite/delete the source of this information.
            var newStartingBalance = ledger[oldIdx].RunningBalance;
            int srcIdx = oldIdx+1;

            // We'll just slide all of the entries at the end of the ledger
            // up to the beginning of the list.
            int dstIdx = 0;
            while ( srcIdx < ledger.Count )
            {
                ledger[dstIdx] = ledger[srcIdx];
                dstIdx++; srcIdx++;
            }

            // Reclaim memory (sorta).
            // This tells C#/.NET that it doesn't need as many spots in the
            // list anymore, and it may or may not actually reclaim that
            // memory. (It probably will, just not right now.)
            if ( dstIdx < ledger.Count )
                ledger.RemoveRange(dstIdx, ledger.Count - dstIdx);

            return newStartingBalance;
        }
        
        private static int GetLineNumber()
        {
            StackFrame callStack = new StackFrame(1, true);
            return callStack.GetFileLineNumber();
        }
        
        public void AddTransaction(
            DateTime transactionTime,
            int      transactionAmount, // Positive if the vendor made money, negative for loss.
            Mobile   otherParty         // Who is buying from or selling to the vendor.
            )
        {
            LedgerEntry e;
            e.TransactionTime = transactionTime;
            e.TransactionAmount = transactionAmount;
            e.OtherParty = otherParty;
            e.RunningBalance = 0;
            this.AddTransaction(e);
        }

        public void AddTransaction(LedgerEntry e)
        {
            AddTransaction(ref m_Ledger, e, m_StartingBalance);

            // This is done in case the vendor is selling something that
            // might put them over their maximum cash-on-hand. To really
            // know if it is, we'll need to know our up-to-date max-cash value.
            UpdateMaxCash(e.TransactionTime);

            // Check for the previous transaction exceeding the
            // maximum cash on hand, and add an adjustment entry
            // if it does.
            int cashOnHand = this.GetCashOnHand(e.TransactionTime);
            if ( cashOnHand > this.m_CurrentMaxCash )
            {
                // This will probably have to rescan part of the ledger,
                // and is thus suboptimal. It could be better. But it should work.
                int adjustmentAmount = cashOnHand - this.m_CurrentMaxCash;
                adjustmentAmount = -adjustmentAmount;
                this.AddTransaction(
                    e.TransactionTime,
                    adjustmentAmount,
                    e.OtherParty
                    );
            }
        }

        public static void AddTransaction(
            ref List<LedgerEntry>  ledger,
            LedgerEntry            e,
            int                    startingBalance)
        {
            ledger.Add(e);

            // Keep the array sorted.
            // This should be done, even if it doesn't make sense
            // to insert an element with an earlier date AFTER
            // inserting an element with a later date.
            // Most likely, this will not need to do any iterations.
            int i = ledger.Count - 1;
            LedgerEntry prev = ledger[i];
            while ( i >= 1 )
            {
                i--;
                prev = ledger[i+0];
                if ( prev.TransactionTime < e.TransactionTime )
                    break; // Sorted.

                // Swap.
                ledger[i+0] = e;    // Previously 'prev'
                ledger[i+1] = prev; // Previously 'e'
            }

            // Prepare for running balance update by retrieving
            // the element before the one we just inserted.
            if ( i <= 0 )
            {
                // If there is no prior element; use starting balance.
                prev.RunningBalance = startingBalance;
                i = 0; // Shouldn't be necessary. It's like assert(i==0), but we shouldn't crash for this.
            }
            else
            {
                prev = ledger[i];
                i++;
            }

            // Update running balances.
            LedgerEntry next;
            for ( ; i < ledger.Count; i++ )
            {
                next = ledger[i];
                next.RunningBalance =
                    prev.RunningBalance + next.TransactionAmount;
                ledger[i] = next;
                prev = next;
            }
        }

        private void UpdateMaxCash(DateTime transactionTime)
        {
            if ( transactionTime < m_NextMaxCashReset )
                return; // It's current. No update needed.

            var rng = new Random();

            // Calculate when it should update next.
            if ( transactionTime > m_NextMaxCashReset.AddDays(MaxDaysBeforeMaxCashReset) )
            {
                // The last reset is far back in history.
                // It's not worth calling GetNextMaxCashTime possibly hundreds
                // of times. Just picking a random day within the max-days range
                // and iterating from that should be plenty of obfuscation.

                // Random value between 0.0 and 1.0
                double randRangeInSeconds = rng.NextDouble();

                // Scale it to the min/max range.
                randRangeInSeconds *= (60*60*24)*MaxDaysBeforeMaxCashReset;
                
                // This should place m_NextMaxCashReset within 'MaxDaysBeforeMaxCashReset'
                // days of the transaction time.
                randRangeInSeconds = -randRangeInSeconds; // Actually subtract seconds.
                m_NextMaxCashReset = transactionTime.AddSeconds(randRangeInSeconds);
            }

            // Now that we've made sure that (the previous) m_NextMaxCashReset
            // is within 'MaxDaysBeforeMaxCashReset' of our transaction time
            // (which is essentially the "now" time, for this calculation)
            // we will iterate it until it's in the future. This should be
            // a small number of iterations, unless MinDaysBeforeMaxCashReset
            // is set really small compared to MaxDaysBeforeMaxCashReset AND
            // we get a lot of unlucky roles.
            while ( transactionTime >= m_NextMaxCashReset )
                this.m_NextMaxCashReset =
                    GetNextMaxCashTime(rng, this.m_NextMaxCashReset);

            // Now for the max cash value itself:
            this.m_CurrentMaxCash =
                rng.Next(MaxCashOnHandLow, MaxCashOnHandHigh);
            // Note that there is no re-running of this calculation for each
            // iteration of reset-time. It's not a derivitive quantity like the
            // cash on hand (cash on hand is supposed to act kind of like it
            // would in an economy), so the only value that matters is the most
            // recent one.
        }

        private static DateTime GetNextMaxCashTime(Random rng, DateTime currentCashResetTime)
        {
            // Random value between 0.0 and 1.0
            double randRangeInSeconds = rng.NextDouble();

            // Scale it to the min/max range.
            randRangeInSeconds *= (60*60*24)*(MaxDaysBeforeMaxCashReset - MinDaysBeforeMaxCashReset);

            // Place the next cash reset this much farther out.
            return currentCashResetTime.AddSeconds(randRangeInSeconds);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            this.ToString(ref sb);
            return sb.ToString();
        }

        public void ToString(ref StringBuilder text)
        {
            text.Append("== Ledger Header ==\r\n");
            text.AppendFormat("Starting Balance = {0}\r\n", this.m_StartingBalance);
            text.AppendFormat("Max cash on hand = {0}\r\n", this.m_CurrentMaxCash);
            text.AppendFormat(
                "Max cash reset = {0,-16:yyyy-MM-dd.HH:mm}\r\n",
                this.m_NextMaxCashReset);

            text.Append("\r\n");
            text.Append("-- Cash Flow Simulation --\r\n");
            if ( this.m_CashFlowSim == null )
                text.Append("Cash flow sim is null.\r\n");
            else
                this.m_CashFlowSim.ToString(ref text);

            text.Append("\r\n");
            text.Append          ("   Trans Time   |Amount|Balance");
            // -------------------|YYYY-MM-DD.HH:MM|NNNNNN|NNNNNNN|--
            text.Append          ("----------------+------+-------");
            for ( int i = 0; i < this.m_Ledger.Count; i++ )
                text.AppendFormat("{0,-16:yyyy-MM-dd.HH:mm}|{1,6}|{2,7}\r\n",
                    this.m_Ledger[i].TransactionTime,
                    this.m_Ledger[i].TransactionAmount,
                    this.m_Ledger[i].RunningBalance);
        }

        private static char ToSuperscriptChar(char ch)
        {
            // Reference:
            // https://en.wikipedia.org/wiki/Unicode_subscripts_and_superscripts
            // https://stackoverflow.com/questions/17908593/how-to-find-the-unicode-of-the-subscript-alphabet
            switch(ch)
            {
                // Unicode gives a lot of options for space characters.
                // We're picking the one that renders as close to the
                // width of the letters/digits as possible on the
                // Enhanced Client. (The classic client doesn't render
                // all of these codepoints correctly anyways.)
                case ' ': return '\x2004';

                // Lowercase letters.
                case 'a': return '\x1d43';
                case 'b': return '\x1d47';
                case 'c': return '\x1d9c';
                case 'd': return '\x1d48';
                case 'e': return '\x1d49';
                case 'f': return '\x1da0';
                case 'g': return '\x1d4d';
                case 'h': return '\x02b0';
                case 'i': return '\x2071';
                case 'j': return '\x02b2';
                case 'k': return '\x1d4f';
                case 'l': return '\x02e1';
                case 'm': return '\x1d50';
                case 'n': return '\x207f';
                case 'o': return '\x1d52';
                case 'p': return '\x1d56';
                // Note that 'q' is missing. Just to annoy you. Because Unicode Consortium.
                case 'r': return '\x02b3';
                case 's': return '\x02e2';
                case 't': return '\x1d57';
                case 'u': return '\x1d58';
                case 'v': return '\x1d5b';
                case 'w': return '\x02b7';
                case 'x': return '\x02e3';
                case 'y': return '\x02b8';
                case 'z': return '\x1dbb';
                
                // Capital letters.
                // Mapped to lowercase when upper is not available.
                // (see default case)
                case 'A': return '\x1d2c';
                case 'B': return '\x1d2e';
                case 'D': return '\x1d30';
                case 'E': return '\x1d31';
                case 'G': return '\x1d33';
                case 'H': return '\x1d34';
                case 'I': return '\x1d35';
                case 'J': return '\x1d36';
                case 'K': return '\x1d37';
                case 'L': return '\x1d38';
                case 'M': return '\x1d39';
                case 'N': return '\x1d3a';
                case 'O': return '\x1d3c';
                case 'P': return '\x1d3e';
                case 'R': return '\x1d3f';
                case 'T': return '\x1d40';
                case 'U': return '\x1d41';
                case 'V': return '\x2c7d';
                case 'W': return '\x1d42';

                // Numbers and symbols.
                case '0': return '\x2070';
                case '1': return '\x00B9';
                case '2': return '\x00B2';
                case '3': return '\x00B3';
                case '+': return '\x207A';
                case '-': return '\x207B';
                case '=': return '\x207C';
                case '(': return '\x207D';
                case ')': return '\x207E';
                default:
                    if ( '4' <= ch && ch <= '9' )
                        return (char)((ch - '0') + '\x2070');
                    else
                    if ( 'A' <= ch && ch <= 'Z' && ch != 'Q' )
                        // Unmatched uppers get replaced with lowers.
                        // (Unicode does not support all of them as of 2020-06-08, and might never.)
                        return ToSuperscriptChar((char)((ch - 'A') + 'a'));
                    else
                        return ch;
            }
        }

        private static char ToSubscriptChar(char ch)
        {
            // Reference:
            // https://en.wikipedia.org/wiki/Unicode_subscripts_and_superscripts
            // https://stackoverflow.com/questions/17908593/how-to-find-the-unicode-of-the-subscript-alphabet
            switch(ch)
            {
                // Unicode gives a lot of options for space characters.
                // We're picking the one that renders as close to the
                // width of the letters/digits as possible on the
                // Enhanced Client. (The classic client doesn't render
                // all of these codepoints correctly anyways.)
                case ' ': return '\x2004';

                // Lowercase letters, or at least the few that are implemented.
                case 'a': return '\x2090';
                case 'e': return '\x2091';
                case 'h': return '\x2095';
                case 'i': return '\x1d62';
                case 'j': return '\x2c7c';
                case 'k': return '\x2096';
                case 'l': return '\x2097';
                case 'm': return '\x2098';
                case 'n': return '\x2099';
                case 'o': return '\x2092';
                case 'p': return '\x209a';
                case 'r': return '\x1d63';
                case 's': return '\x209b';
                case 't': return '\x209c';
                case 'u': return '\x1d64';
                case 'v': return '\x1d65';
                case 'x': return '\x2093';
                
                // Unicode does not support uppercase subscripts. Too bad, so sad.
                // We will substitute these for lowercase subscripts whenever
                // it allows the character to print as subscript.
                // See default case for details.

                // Digits and specials.
                case '+': return '\x208A';
                case '-': return '\x208B';
                case '=': return '\x208C';
                case '(': return '\x208D';
                case ')': return '\x208E';
                default:
                    if ( '0' <= ch && ch <= '9' )
                        return (char)((ch - '0') + '\x2080');
                    else
                    if ( 'A' <= ch && ch <= 'Z' )
                    {
                        // Unmatched uppers get replaced with lowers, if possible.
                        // (Unicode does not support all of them as of 2020-06-08, and might never.)
                        char lowerReplacement = (char)((ch - 'A') + 'a');
                        char lowerSubscript   = ToSubscriptChar(lowerReplacement);
                        if ( lowerReplacement == lowerSubscript )
                            return ch; // No subscript available at all.
                        else
                            return lowerSubscript; // Lowercase it to get SOMETHING.
                    }
                    else
                        return ch;
            }
        }

        private static string ToSuperscript(string text)
        {
            var sb = new StringBuilder();
            foreach( char ch in text )
                sb.Append(ToSuperscriptChar(ch));
            return sb.ToString();
        }

        private static string ToSubscript(string text)
        {
            var sb = new StringBuilder();
            foreach( char ch in text )
                sb.Append(ToSubscriptChar(ch));
            return sb.ToString();
        }

        private static string Underscore(string text)
        {
            var sb = new StringBuilder();
            foreach( char ch in text )
            {
                sb.Append(ch);
                sb.Append('\x0332'); // "combining low line". It's an underscore, but it composes.
            }
            return sb.ToString();
        }

        public List<BookPageInfo> ToBookPages()
        {
            var pages = new List<BookPageInfo>();
            string[] lines;
            
            // Header page.
            lines = new string[8];
            lines[0] = "== Ledger Header ==";
            lines[1] = "Starting balance:";
            lines[2] = String.Format("    {0} gp", this.m_StartingBalance);
            lines[3] = "Max cash on hand:";
            lines[4] = String.Format("    {0} gp", this.m_CurrentMaxCash);
            lines[5] = "Max cash resets on:";
            lines[6] = String.Format("  {0,-16:yyyy-MM-dd.HH:mm}", this.m_NextMaxCashReset);
            if ( this.m_CashFlowSim == null )
                lines[7] = "Cash flow sim is null.";
            else
                lines[7] = "";
            pages.Add(new BookPageInfo(lines));

            // Cash flow simulation
            if ( this.m_CashFlowSim != null )
                pages.AddRange(this.m_CashFlowSim.ToBookPages());

            // Ledger Entries.
            // Note that we can fit 8 per page.
            // Pages have 9 lines, so the 9th line (1st line, really)
            // is used for the header, which leaves 8 for the rest.
            if ( this.m_Ledger == null )
            {
                lines = new string[2];
                lines.Append("List of ledger entries");
                lines.Append("is null.");
                pages.Add(new BookPageInfo(lines));
            }
            else
            {
                // Valid ledger.
                int nEntryPages = this.m_Ledger.Count / 8;
                if ( nEntryPages * 8 != this.m_Ledger.Count )
                    nEntryPages++; // Round up always.

                var text = new StringBuilder();
                for ( int pgNum = 0; pgNum < nEntryPages; pgNum++ )
                {
                    int startIdx = pgNum*8;
                    int entriesRemaining = (this.m_Ledger.Count - startIdx);
                    int nEntries = 8;
                    if ( entriesRemaining < 8 )
                        nEntries = entriesRemaining;

                    int nLines = nEntries + 1; // ex: 8 entries + 1 header
                    lines = new string[nLines];

                    // The \x2009 is a "thin" space. It aligns the column line.
                    lines[0] = Underscore("Trans Time|Amt\x2009| Bal");
                    for ( int lineIdx = 1; lineIdx < nLines; lineIdx++ )
                    {
                        int entryIdx = startIdx + (lineIdx-1);
                        LedgerEntry e = this.m_Ledger[entryIdx];
                        text.AppendFormat(ToSubscript(String.Format(
                            "{0,-16:yyyy-MM-dd HH mm}", e.TransactionTime)));
                        text.Append("|");
                        text.AppendFormat(ToSubscript(String.Format(
                            "{0,6}", e.TransactionAmount)));
                        text.Append("|");
                        text.AppendFormat(ToSubscript(String.Format(
                            "{0,7}", e.RunningBalance)));

                        lines[lineIdx] = text.ToString();
                        text.Clear();
                    }
                    pages.Add(new BookPageInfo(lines));
                }
            }
            // Ledger entry list output is done.

            /*
            // debugging space widths on EC
            lines = new string[9];
            lines[0] = "Unicode spaces 1";
            lines[1] = "\x0020\x0020\x0020\x0020|";
            lines[2] = ToSuperscript("0020");
            lines[3] = "\x2000\x2000\x2000\x2000|";
            lines[4] = ToSuperscript("2000");
            lines[5] = "\x2001\x2001\x2001\x2001|";
            lines[6] = ToSuperscript("2001");
            lines[7] = "\x2002\x2002\x2002\x2002|";
            lines[8] = ToSuperscript("2002");
            pages.Add(new BookPageInfo(lines));
            
            lines = new string[9];
            lines[0] = "Unicode spaces 2";
            lines[1] = "\x2003\x2003\x2003\x2003|";
            lines[2] = ToSuperscript("2003");
            lines[3] = "\x2004\x2004\x2004\x2004|";
            lines[4] = ToSuperscript("2004");
            lines[5] = "\x2005\x2005\x2005\x2005|";
            lines[6] = ToSuperscript("2005");
            lines[7] = "\x2006\x2006\x2006\x2006|";
            lines[8] = ToSuperscript("2006");
            pages.Add(new BookPageInfo(lines));
            
            lines = new string[9];
            lines[0] = "Unicode spaces 3";
            lines[1] = "\x2007\x2007\x2007\x2007|";
            lines[2] = ToSuperscript("2007");
            lines[3] = "\x2008\x2008\x2008\x2008|";
            lines[4] = ToSuperscript("2008");
            lines[5] = "\x2009\x2009\x2009\x2009|";
            lines[6] = ToSuperscript("2009");
            lines[7] = "\x200A\x200A\x200A\x200A|";
            lines[8] = ToSuperscript("200a");
            pages.Add(new BookPageInfo(lines));
            
            lines = new string[7];
            lines[0] = "Unicode spaces 4";
            lines[1] = "\x202F\x202F\x202F\x202F|";
            lines[2] = ToSuperscript("202f");
            lines[3] = "\x205F\x205F\x205F\x205F|";
            lines[4] = ToSuperscript("205f");
            lines[5] = "\x3000\x3000\x3000\x3000|";
            lines[6] = ToSuperscript("3000");
            pages.Add(new BookPageInfo(lines));
            */

            return pages;
        }

        private sealed class CashFlowSimulation
        {
            /// This parameter establishes the start of an interval of time
            /// in which the vendor "made" or "lost" money (cash/gp).
            public  DateTime StartTime;

            /// The amount of cash the vendor had at the beginning of this
            /// interval of time.
            public int       StartingCash;

            /// The number of "transactions" the vendor is to make in the
            /// timespan given by "StartTime" and "EndTime". Each transaction
            /// is a debit or credit that brings the vendor's cash-on-hand
            /// closer to its assigned target cash-on-hand.
            public  int      NumberOfTransactions;

            /// This is the number that seeded the random number generator
            /// that was used to make this simulation.
            public  int      Seed;

            // Everything below this point is derived from the above params.
            public  DateTime EndTime;

            public List<LedgerEntry> Ledger { get { return m_Ledger; } }
            private List<LedgerEntry>  m_Ledger;

            public CashFlowSimulation(
                BaseVendor  vendor,
                DateTime    startTime,
                int         startingCash,
                int         numTransactions)
            {
                var rngBootStrap = new Random();
                this.initialize(
                    vendor,
                    startTime,
                    startingCash,
                    numTransactions,
                    rngBootStrap.Next());
            }

            public CashFlowSimulation(
                BaseVendor  vendor,
                DateTime    startTime,
                int         startingCash,
                int         numTransactions,
                int         rngSeed)
            {
                this.initialize(
                    vendor,
                    startTime,
                    startingCash,
                    numTransactions,
                    rngSeed);
            }

            public void initialize(
                BaseVendor  vendor,
                DateTime    startTime,
                int         startingCash,
                int         numTransactions,
                int         rngSeed)
            {
                var rng = new Random(rngSeed);

                this.StartTime            = startTime;
                this.StartingCash         = startingCash;
                this.NumberOfTransactions = numTransactions;
                this.Seed                 = rngSeed;
                this.m_Ledger             = null;

                // Calculate how long this vendor's day is.
                // (They work pretty long hours. I mean, I've probably worked
                // a 26 hour day at some point, but these guys might do that
                // every other day! They must never sleep...)
                double intervalSpreadInSeconds = (60.0 * 60.0 * HoursSpreadOnCashDays);
                double intervalSpanInSeconds = (0.0
                    + (60.0 * 60.0 * 24.0)            // One Day
                    + (((rng.NextDouble() * 2) - 1.0) // Random value between -1.0 and 1.0
                    * intervalSpreadInSeconds)        // Magnitude of randomization
                );

                this.EndTime = startTime.AddSeconds(intervalSpanInSeconds);

                // This is how much our universe /wants/ the vendor to have
                // at the end of the day. However, cash flow is limited, so
                // the vendor might not hit this number (which is good,
                // because we don't want to have a vendor make a ton of
                // money off of a player and then get cleaned out within
                // an hour... routinely).
                int targetCashOnHand = rng.Next(
                    TargetCashOnHandLow, TargetCashOnHandHigh);

                // Figure out how much this vendor is going to make/lose today.
                int netCashFlow   = 0;
                int distance      = (targetCashOnHand - startingCash);
                int absDistance   = Math.Abs(distance);
                if ( distance > 0 )
                {
                    // Cash comes IN
                    netCashFlow = rng.Next(0, MaxDailyCashInflow);
                    netCashFlow = Math.Min(netCashFlow, absDistance);
                }
                else
                if ( distance < 0 )
                {
                    // Cash goes OUT
                    netCashFlow = rng.Next(0, MaxDailyCashOutflow);
                    netCashFlow = Math.Min(netCashFlow, absDistance);
                    netCashFlow = -netCashFlow;
                }

                // Now we know how much the vendor's balance is going to change
                // (assuming no player interaction).
                // In this part, we generate a ledger that would cause that
                // change in balance. We're going to go about it simply and just
                // assume that all transactions run in the same direction. It's
                // not very realistic, but it should do just fine to obscure
                // game mechanics.
                //
                // This is going to involve creating randomized partitions in
                // both time and in the cash flow.

                // We do this in a while-loop because there are improbably
                // things that could go wrong, and we want to be able to
                // grab more random numbers and try again.
                var transTimeWeights   = new double[numTransactions];
                var transAmountWeights = new double[numTransactions];
                while(true)
                {
                    // First, we calculate weights for each partition:
                    for ( int i = 0; i < numTransactions; i++ )
                    {
                        transTimeWeights[i]   = rng.NextDouble();
                        transAmountWeights[i] = rng.NextDouble();
                    }

                    // Next, we normalize these, so that each weight
                    // represents a percentage of the time interval
                    // or the cash flow. I say "percentage" because
                    // all of these values should sum up to %100 (=1.0),
                    // no more and no less.
                    // It wouldn't be a proper partitioning if the
                    // sum of the parts didn't add up to the whole.
                    double timeMagnitude = 0.0;
                    double flowMagnitude = 0.0;
                    for ( int i = 0; i < numTransactions; i++ )
                    {
                        timeMagnitude += transTimeWeights[i];
                        flowMagnitude += transAmountWeights[i];
                    }

                    // Prevent div-by-zero.
                    if ( timeMagnitude == 0 || flowMagnitude == 0 )
                    {
                        // Super unlikely, but technically possible.
                        // Causes retry.
                        continue;
                    }

                    for ( int i = 0; i < numTransactions; i++ )
                    {
                        transTimeWeights[i]   /= timeMagnitude;
                        transAmountWeights[i] /= flowMagnitude;
                    }

                    break;
                }

                // Yay, normalization GET!
                // Now we multiply these normal vectors by their
                // respective magnitudes (time and flow) to get
                // ledger entries. Next time you talk to your
                // accountant, just tell them that all they need to be
                // able to cook your books right is to just use
                // a little bit of vector math... they'll love you.
                //
                // Note that this generates an array sorted by TransactionTime.
                // We will expect all ledgers to be datetime-sorted.
                // In this case, it helps us compute the running balance.
                // After this, the caller may need to merge this ledger
                // with another one, at which point sortedness helps again.
                int prevBalance = startingCash;
                this.m_Ledger = new List<LedgerEntry>(numTransactions);
                for ( int i = 0; i < numTransactions; i++ )
                {
                    LedgerEntry e;
                    e.TransactionTime = 
                        startTime.AddSeconds(
                            Math.Round(
                                transTimeWeights[i] * intervalSpanInSeconds
                            )
                        );

                    e.TransactionAmount =
                        (int)Math.Round(transAmountWeights[i] * netCashFlow);

                    // Avoid putting null here.
                    // null might cause crashing. That's bad.
                    // Instead, we'll just have the vendor play with itself.
                    e.OtherParty = vendor;
                    
                    // Note that we cannot yet calculate running balance,
                    // because the ledger list might not be sorted.
                    e.RunningBalance = 0;

                    this.m_Ledger.Add(e);
                }
                
                // Sort the ledger by date.
                // We will expect all ledgers to be date-sorted.
                // In this case, it helps us compute the running balance.
                // After this, the caller may need to merge this ledger
                // with another one, at which point sorting helps again.
                this.m_Ledger.Sort(
                    (a,b) => a.TransactionTime.CompareTo(b.TransactionTime));

                // Calculate running balances, now that the thing is sorted.
                for ( int i = 0; i < numTransactions; i++ )
                {
                    LedgerEntry e = this.m_Ledger[i];
                    e.RunningBalance = prevBalance + e.TransactionAmount;
                    this.m_Ledger[i] = e;
                }
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                this.ToString(ref sb);
                return sb.ToString();
            }

            public void ToString(ref StringBuilder text)
            {
                text.AppendFormat("Start Time = {0,-16:yyyy-MM-dd.HH:mm:ss}\r\n", StartTime);
                text.AppendFormat("End Time   = {0,-16:yyyy-MM-dd.HH:mm:ss}\r\n", EndTime);
                text.AppendFormat("Starting Cash = {0}\r\n", StartingCash);
                text.AppendFormat("Num Transactions = {0}\r\n", NumberOfTransactions);
                text.AppendFormat("Random Seed = {0:X}", Seed);
                if ( this.m_Ledger == null )
                    text.AppendFormat("Cash sim ledger is null.");
                else
                {
                    text.Append("Amounts = [");
                    int i = 0;
                    if ( i < this.m_Ledger.Count )
                        text.AppendFormat("{0}", this.m_Ledger[i++].TransactionAmount);
                    while ( i < this.m_Ledger.Count )
                        text.AppendFormat(", {0}", this.m_Ledger[i++].TransactionAmount);
                    text.Append("]\r\n");
                }
            }

            public List<BookPageInfo> ToBookPages()
            {
                var pages = new List<BookPageInfo>();
                string[] lines;
                
                // Header page.
                lines = new string[8];
                lines[0] = "== Cash Sim. ==";
                lines[1] = "Start time:";
                lines[2] = String.Format("    {0,-16:yyyy-MM-dd.HH:mm}", this.StartTime);
                lines[3] = "End time:";
                lines[4] = String.Format("    {0,-16:yyyy-MM-dd.HH:mm}", this.EndTime);
                lines[5] = String.Format("Start Cash: {0}", this.StartingCash);
                lines[6] = String.Format("# of Trans: {0}", this.NumberOfTransactions);
                lines[7] = String.Format("Rand Seed: {0:X}", this.Seed);
                pages.Add(new BookPageInfo(lines));
                return pages;
            }

            public void Serialize(GenericWriter writer)
            {
                writer.Write(this.StartTime);
                writer.Write(this.StartingCash);
                writer.Write(this.NumberOfTransactions);
                writer.Write(this.Seed);
            }

            public static CashFlowSimulation Deserialize(GenericReader reader, BaseVendor vendor)
            {
                DateTime  startTime        = reader.ReadDateTime();
                int       startingCash     = reader.ReadInt();
                int       numTransactions  = reader.ReadInt();
                int       rngSeed          = reader.ReadInt();
                return new CashFlowSimulation(
                    vendor,
                    startTime,
                    startingCash,
                    numTransactions,
                    rngSeed );
            }
        }

        public void Serialize(GenericWriter writer)
        {
            // Version 1
            writer.Write((int)1); // version
            writer.Write(m_StartingBalance);

            if ( m_Ledger == null )
                writer.Write((int)0);
            else
                writer.Write(m_Ledger.Count);

            for ( int i = 0; i < m_Ledger.Count; i++ )
            {
                LedgerEntry e = m_Ledger[i];
                writer.Write(e.TransactionTime);
                writer.Write(e.TransactionAmount);
                writer.Write(e.OtherParty);
            }

            writer.Write(m_CurrentMaxCash);
            writer.Write(m_NextMaxCashReset);

            if ( this.m_CashFlowSim == null )
                writer.Write(false);
            else
            {
                writer.Write(true);
                this.m_CashFlowSim.Serialize(writer);
            }
        }

        public void Deserialize(GenericReader reader, BaseVendor vendor)
        {
            int version = reader.ReadInt();
            
            switch(version)
            {
                case 1:
                    m_StartingBalance = reader.ReadInt();
                
                    int ledgerCount = reader.ReadInt();
                    if ( ledgerCount > 0 )
                    {
                        m_Ledger.Capacity = ledgerCount;

                        LedgerEntry prev;
                        prev.RunningBalance = m_StartingBalance;
                        for ( int i = 0; i < ledgerCount; i++ )
                        {
                            LedgerEntry e;
                            e.TransactionTime   = reader.ReadDateTime();
                            e.TransactionAmount = reader.ReadInt();
                            e.OtherParty        = reader.ReadMobile();
                            e.RunningBalance    = prev.RunningBalance + e.TransactionAmount;
                            m_Ledger.Add(e);
                            prev = e;
                        }
                    }

                    m_CurrentMaxCash   = reader.ReadInt();
                    m_NextMaxCashReset = reader.ReadDateTime();

                    if ( reader.ReadBool() )
                        this.m_CashFlowSim = CashFlowSimulation.Deserialize(reader, vendor);
                    break;
            }
        }
    }
}

namespace Server
{
    public interface IShopSellInfo
    {
        //get display name for an item
        string GetNameFor(Item item);

        //get price for an item which the player is selling
        int GetSellPriceFor(Item item);
        int GetSellPriceFor(Item item, BaseVendor vendor);

        //get price for an item which the player is buying
        int GetBuyPriceFor(Item item);
        int GetBuyPriceFor(Item item, BaseVendor vendor);

        //can we sell this item to this vendor?
        bool IsSellable(Item item);

        //What do we sell?
        Type[] Types { get; }

        //does the vendor resell this item?
        bool IsResellable(Item item);

        // Sell price before any modifies for quality/magic.
        int GetBaseSellPriceFor(Type type);
        int GetBaseSellPriceFor(Item item);

        // This is intended to handle corner cases like
        // allowing players to profit more from magic items.
        bool IsItemWorthGoingIntoDebt(Item item, BaseVendor vendor);

        // Limit the amount of gold made from one type of item.
        // The version of this method that requires a BaseVendor
        // object will run the "worth going into debt" calculation
        // and then invoke the other version.
        int MaxPayForItem(
            DateTime   transactionTime,
            Item       item,
            BaseVendor vendor);

        int MaxPayForItem(
            DateTime transactionTime,
            Item     item,
            bool     itemWorthGoingIntoDebt);

        // Record sales. Ex: to prevent selling way too much of one thing.
        void OnSold(DateTime transactionTime, Type itemType, int itemQty, int amountPaid);
    }

    public interface IBuyItemInfo
    {
        //get a new instance of an object (we just bought it)
        IEntity GetEntity();

        int ControlSlots { get; }

        int PriceScalar { get; set; }

        bool Stackable { get; set; }
        int TotalBought { get; set; }
        int TotalSold { get; set; }

        void OnBought(Mobile buyer, BaseVendor vendor, IEntity entity, int amount);
        void OnSold(BaseVendor vendor, int amount);

        //display price of the item
        int Price { get; }

        //display name of the item
        string Name { get; }

        //display hue
        int Hue { get; }

        //display id
        int ItemID { get; }

        //amount in stock
        int Amount { get; set; }

        //max amount in stock
        int MaxAmount { get; }

        //Attempt to restock with item, (return true if restock sucessful)
        bool Restock(Item item, int amount);

        //called when its time for the whole shop to restock
        void OnRestock();
    }
}
