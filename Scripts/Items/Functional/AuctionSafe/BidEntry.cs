using Server.Accounting;
using Server.Mobiles;
using System;

namespace Server.Engines.Auction
{
    public class BidEntry : IComparable<BidEntry>
    {
        public PlayerMobile Mobile { get; set; }
        public long CurrentBid { get; set; }

        //Converts to gold/plat
        public int TotalGoldBid => (int)(CurrentBid >= Account.CurrencyThreshold ? CurrentBid - (TotalPlatBid * Account.CurrencyThreshold) : CurrentBid);
        public int TotalPlatBid => (int)(CurrentBid >= Account.CurrencyThreshold ? CurrentBid / Account.CurrencyThreshold : 0);

        public BidEntry(PlayerMobile m, long bid = 0)
        {
            Mobile = m;
            CurrentBid = bid;
        }

        public BidEntry(PlayerMobile m, GenericReader reader)
        {
            Mobile = m;

            int version = reader.ReadInt();
            CurrentBid = reader.ReadLong();
        }

        public void Serialize(GenericWriter writer)
        {
            writer.Write(0);
            writer.Write(CurrentBid);
        }

        public int CompareTo(BidEntry entry)
        {
            if (CurrentBid > entry.CurrentBid)
                return 1;

            return 0;
        }
    }
}