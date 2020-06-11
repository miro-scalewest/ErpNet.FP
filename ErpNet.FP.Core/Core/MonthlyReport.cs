namespace ErpNet.FP.Core
{
    using System.Collections.Generic;
    using Newtonsoft.Json;

    public enum ReportType
    {
        Short = 0,
        Detailed = 1,
    }

    /// <summary>
    /// Represents one System Receipt, which can be printed on a fiscal printer.
    /// </summary>
    public class MonthlyReport
    {
        /// <summary>
        /// Start date of the report
        /// </summary>
        public System.DateTime StartDate { get; set; }

        /// <summary>
        /// End date of the report
        /// </summary>
        public System.DateTime EndDate { get; set; }

        /// <summary>
        /// Type of the report
        /// </summary>
        public ReportType Type { get; set; } = ReportType.Short;
    }
}