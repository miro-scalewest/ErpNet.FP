namespace ErpNet.FP.Core
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public enum UIDType
    {
        /// <summary>
        /// Bulstat
        /// </summary>
        Bulstat = 0,

        /// <summary>
        /// ЕГN
        /// </summary>
        PersonalID = 1,

        /// <summary>
        /// ЛНЧ
        /// </summary>
        PersonalIDForeigner = 2,

        /// <summary>
        /// Служебен номер
        /// </summary>
        ServiceNumber = 3
    }

    public class Invoice
    {
        /// <summary>
        /// The unique identifier (EIK).
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string UID { get; set; } = string.Empty;

        /// <summary>
        /// Identifier type
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public UIDType Type { get; set; }

        /// <summary>
        /// Seller name
        /// </summary>
        public string SellerName { get; set; } = string.Empty;

        /// <summary>
        /// Receiver name
        /// </summary>
        public string ReceiverName { get; set; } = string.Empty;

        /// <summary>
        /// Client name
        /// </summary>
        public string BuyerName { get; set; } = string.Empty;

        /// <summary>
        /// Tax number
        /// </summary>
        public string VatNumber { get; set; } = string.Empty;

        /// <summary>
        /// Client address
        /// </summary>
        public string ClientAddress { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents one Receipt, which can be printed on a fiscal printer.
    /// </summary>
    public class Receipt : Credentials
    {
        /// <summary>
        /// The unique sale number is a fiscally controlled number.
        /// </summary>
        //[JsonProperty(Required = Required.Always)]
        public string UniqueSaleNumber { get; set; } = string.Empty;

        public Invoice? Invoice { get; set; }

        /// <summary>
        /// The line items of the receipt.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public IList<Item>? Items { get; set; }

        /// <summary>
        /// The payments of the receipt. 
        /// The total amount should match the total amount of the line items.
        /// </summary>
        public IList<Payment>? Payments { get; set; }

        /// <summary>
        /// Print duplicate immediately
        /// </summary>
        [JsonProperty()]
        public Boolean PrintDuplicate { get; set; }
    }
}