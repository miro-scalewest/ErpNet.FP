namespace ErpNet.FP.Core
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// Represents one System Receipt, which can be printed on a fiscal printer.
    /// </summary>
    public class NonFiscalReceipt : Credentials
    {
        /// <summary>
        /// The line items of the receipt.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public IList<FreeTextItem>? Items { get; set; }
    }
}