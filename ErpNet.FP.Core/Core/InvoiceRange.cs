namespace ErpNet.FP.Core
{
    using System.Numerics;

    public class InvoiceRange
    {
        /// <summary>
        /// Range start
        /// </summary>
        public BigInteger Start { get; set; } = 0;

        /// <summary>
        /// Receiver name
        /// </summary>
        public BigInteger End { get; set; } = 0;
    }
}