namespace ErpNet.FP.Core.Drivers.BgTremol
{
    using System;
    using System.Globalization;
    using Serilog;

    public partial class BgTremolZfpFiscalPrinter : BgZfpFiscalPrinter
    {
        protected new const byte
            CommandPrintBriefReportForDate = 0x7B,
            CommandPrintDetailedReportForDate = 0x7A;

        public override (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var startDateString = startDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var endDateString = endDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var headerData = string.Join(";", startDateString, endDateString);
            Log.Information("Tremol report: " + headerData);

            return Request(
                type == ReportType.Brief
                    ? CommandPrintBriefReportForDate
                    : CommandPrintDetailedReportForDate,
                headerData
            );
        }
    }
}