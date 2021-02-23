namespace ErpNet.FP.Core.Drivers.BgEltrade
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Serilog;

    /// <summary>
    /// Fiscal printer using the ISL implementation of Eltrade Bulgaria.
    /// </summary>
    /// <seealso cref="BgIslFiscalPrinter" />
    public partial class BgEltradeIslFiscalPrinter : BgIslFiscalPrinter
    {
        protected const byte
            CommandPrintBriefReportForDate = 0x4F,
            CommandPrintDetailedReportForDate = 0x5E,
            EltradeCommandOpenFiscalReceipt = 0x90,
            CommandGetInvoiceRange = 0x42,
            CommandSetInvoiceRange = 0x42;


        public override IDictionary<PaymentType, string> GetPaymentTypeMappings()
        {
            var paymentTypeMappings = new Dictionary<PaymentType, string> {
                { PaymentType.Cash,          "P" },
                { PaymentType.Check,         "N" },
                { PaymentType.Coupons,       "C" },
                { PaymentType.ExtCoupons,    "D" },
                { PaymentType.Packaging,     "I" },
                { PaymentType.InternalUsage, "J" },
                { PaymentType.Damage,        "K" },
                { PaymentType.Card,          "L" },
                { PaymentType.Bank,          "M" },
                { PaymentType.Reserved1,     "Q" },
                { PaymentType.Reserved2,     "R" }
            };
            ServiceOptions.RemapPaymentTypes(Info.SerialNumber, paymentTypeMappings);
            return paymentTypeMappings;
        }

        public override (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            bool isInvoice = false)
        {
            string theOperator = string.IsNullOrEmpty(operatorId)
                ? Options.ValueOrDefault("Operator.Name", "Operator")
                : operatorId;
            string header;
            if (string.IsNullOrEmpty(uniqueSaleNumber))
            {
                header = string.Join(",",
                    theOperator);
            }
            else
            {
                header = string.Join(",",
                    theOperator,
                    uniqueSaleNumber
                );
            }

            if (isInvoice)
            {
                header += ",I";
            }

            return Request(EltradeCommandOpenFiscalReceipt, header);
        }

        public override (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            var clientData = (new StringBuilder()).Append(invoice.UID);

            if (!String.IsNullOrEmpty(invoice.SellerName))
            {
                clientData.Append('\t').Append(invoice.SellerName);

                if (!String.IsNullOrEmpty(invoice.ReceiverName))
                {
                    clientData.Append('\t').Append(invoice.ReceiverName);

                    if (!String.IsNullOrEmpty(invoice.BuyerName))
                    {
                        clientData.Append('\t').Append(invoice.BuyerName);

                        if (!String.IsNullOrEmpty(invoice.VatNumber))
                        {
                            clientData.Append('\t').Append(invoice.VatNumber);

                            if (!String.IsNullOrEmpty(invoice.ClientAddress))
                            {
                                clientData.Append('\t').Append(invoice.ClientAddress);
                            }
                        }
                    }
                }
            }

            return Request(
                CommandSetClientInfo,
                clientData.ToString()
            );
        }

        public override DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange)
        {
            var creditNotesRange = GetRange(true);

            if (creditNotesRange.Ok
                && (
                    (!creditNotesRange.Start.HasValue || creditNotesRange.Start == 0)
                    || (!creditNotesRange.End.HasValue || creditNotesRange.End == 0)
                    || creditNotesRange.Start >= creditNotesRange.End
                )
            )
            {
                Log.Information("Setting credit note range");
                var res = SetRange(invoiceRange, true);

                if (res.Ok)
                {
                    Log.Information("Credit note range set");
                }
                else
                {
                    Log.Error("Couldn't set credit note range");
                    foreach (StatusMessage message in res.Messages)
                    {
                        if (message.Type == StatusMessageType.Error)
                        {
                            Log.Error($"[{message.Code}] {message.Text}");
                        }
                    }
                }
            }

            return SetRange(invoiceRange);
        }

        public DeviceStatus SetRange(InvoiceRange invoiceRange, bool creditNote = false)
        {
            var (_, result) =  Request(CommandSetInvoiceRange, (creditNote ? "S" : "") + invoiceRange.Start + "," + invoiceRange.End);
            if (!result.Ok)
            {
                result.AddError("E499", "An error occurred while setting invoice range");
                return result;
            }

            var (_, setCreditNoteRange) =  Request(CommandSetInvoiceRange, $"S{invoiceRange.Start},{invoiceRange.End}");
            if (!setCreditNoteRange.Ok)
            {
                result.AddError("E499", "An error occurred while setting credit notes range");
                return result;
            }

            return result;
        }

        public DeviceStatusWithInvoiceRange GetRange(bool creditNote = false)
        {
            var (data, result) =  Request(CommandGetInvoiceRange, creditNote ? "S" : null);
            var response = new DeviceStatusWithInvoiceRange(result);
            if (!result.Ok)
            {
                response.AddError("E499", "An error occurred while setting invoice range");
                return response;
            }

            var split = data.Split(",");
            try
            {
                response.Start = int.Parse(split[0]);
                response.End = int.Parse(split[1]);
            }
            catch (Exception e)
            {
                response.AddError("E409", "Error occurred while parsing invoice range data");
                response.AddInfo(e.Message);
            }

            return response;
        }

        public override DeviceStatusWithInvoiceRange GetInvoiceRange()
        {
            return GetRange();
        }

        public override string GetReversalReasonText(ReversalReason reversalReason)
        {
            return reversalReason switch
            {
                ReversalReason.OperatorError => "O",
                ReversalReason.Refund => "R",
                ReversalReason.TaxBaseReduction => "T",
                _ => "O",
            };
        }

        public override (ReceiptInfo, DeviceStatus) PrintReversalReceipt(ReversalReceipt reversalReceipt)
        {
            var receiptInfo = new ReceiptInfo();

            if (reversalReceipt.Invoice != null)
            {
                var (isValid, rangeCheckResult) = CreditNoteRangeCheck();

                if (!rangeCheckResult.Ok || !isValid)
                {
                    return (receiptInfo, rangeCheckResult);
                }
            }

            // Abort all unfinished or erroneus receipts
            AbortReceipt();

            // Receipt header
            var (_, deviceStatus) = OpenReversalReceipt(
                reversalReceipt.Reason,
                reversalReceipt.ReceiptNumber,
                reversalReceipt.ReceiptDateTime,
                reversalReceipt.FiscalMemorySerialNumber,
                reversalReceipt.UniqueSaleNumber,
                reversalReceipt.Operator,
                reversalReceipt.OperatorPassword,
                reversalReceipt.InvoiceNumber);
            if (!deviceStatus.Ok)
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occured while opening new fiscal reversal receipt");
                return (receiptInfo, deviceStatus);
            }

            (receiptInfo, deviceStatus) = PrintReceiptBody(reversalReceipt);
            if (!deviceStatus.Ok)
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occured while printing receipt items");
            }

            return (receiptInfo, deviceStatus);
        }

        public (bool, DeviceStatus) CreditNoteRangeCheck()
        {
            var range = GetRange(true);
            if (!range.Ok)
            {
                range.AddError("E405", "Error occurred while fetching credit note range");
                return (false, range);
            }

            if (!range.Start.HasValue
                || !range.End.HasValue
                || range.Start == 0
                || range.End == 0
                || range.Start >= range.End)
            {
                range.AddError("405", "Credit note range is not set");
                return (false, range);
            }

            return (true, range);
        }

        public override (string, DeviceStatus) OpenReversalReceipt(ReversalReason reason,
            string receiptNumber,
            DateTime receiptDateTime,
            string fiscalMemorySerialNumber,
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            string invoiceNumber)
        {
            // Protocol: <OperName>,<UNP>[,Type[ ,<FMIN>,<Reason>,<num>[,<time>[,<inv>]]]]
            var header = string.Join(",",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Name", "Operator")
                        :
                        operatorId,
                    uniqueSaleNumber,
                    "S",
                    fiscalMemorySerialNumber,
                    GetReversalReasonText(reason),
                    receiptNumber,
                    receiptDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                });

            if (!String.IsNullOrEmpty(invoiceNumber))
            {
                header += "," + invoiceNumber;
            }

            return Request(EltradeCommandOpenFiscalReceipt, header);
        }

        public override (string, DeviceStatus) PrintNonFiscalReceiptText(
            string text,
            bool bold = false,
            bool italic = false,
            bool underline = false,
            LineHeight height = LineHeight.OneLine
        )
        {
            // Eltrade doesn't support text styling
            return Request(CommandNonFiscalReceiptText, text.WithMaxLength(Info.CommentTextMaxLength));
        }

        public override (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var startDateString = startDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var endDateString = endDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var headerData = string.Join(",", startDateString, endDateString);
            Console.WriteLine("Eltrade: " + headerData);

            return Request(
                type == ReportType.Brief
                    ? CommandPrintBriefReportForDate
                    : CommandPrintDetailedReportForDate,
                headerData
            );
        }

        // 6 Bytes x 8 bits
        protected static readonly (string?, string, StatusMessageType)[] StatusBitsStrings = new (string?, string, StatusMessageType)[] {
            ("E401", "Incoming data has syntax error", StatusMessageType.Error),
            ("E402", "Code of incoming command is invalid", StatusMessageType.Error),
            ("E103", "The clock needs setting", StatusMessageType.Error),
            (null, "Not connected a customer display", StatusMessageType.Info),
            ("E303", "Failure in printing mechanism", StatusMessageType.Error),
            ("E199", "General error", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E403", "During command some of the fields for the sums overflow", StatusMessageType.Error),
            ("E404", "Command cannot be performed in the current fiscal mode", StatusMessageType.Error),
            ("E104", "Operational memory was cleared", StatusMessageType.Error),
            ("E102", "Low battery (the clock is in reset state)", StatusMessageType.Error),
            ("E105", "RAM failure after switch ON", StatusMessageType.Error),
            ("E302", "Paper cover is open", StatusMessageType.Error),
            ("E599", "The internal terminal is not working", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E301", "No paper", StatusMessageType.Error),
            ("W301", "Not enough paper", StatusMessageType.Warning),
            ("E206", "End of KLEN(under 1MB free)", StatusMessageType.Error),
            (null, "A fiscal receipt is opened", StatusMessageType.Info),
            ("W202", "Coming end of KLEN (10MB free)", StatusMessageType.Warning),
            (null, "A non-fiscal receipt is opened", StatusMessageType.Info),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            // Byte 3, bits from 0 to 6 are SW 1 to 7
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E202", "Error during writing to the fiscal memory", StatusMessageType.Error),
            (null, "EIK is entered", StatusMessageType.Info),
            (null, "FM number has been set", StatusMessageType.Info),
            ("W201", "There is space for not more than 50 entries in the FM", StatusMessageType.Warning),
            ("E201", "Fiscal memory is fully engaged", StatusMessageType.Error),
            ("E299", "FM general error", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E204", "The fiscal memory is in the 'read-only' mode", StatusMessageType.Error),
            (null, "The fiscal memory is formatted", StatusMessageType.Info),
            ("E202", "The last record in the fiscal memory is not successful", StatusMessageType.Error),
            (null, "The printer is in a fiscal mode", StatusMessageType.Info),
            (null, "Tax rates have been entered at least once", StatusMessageType.Info),
            ("E203", "Fiscal memory read error", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved)
        };

        protected override DeviceStatus ParseStatus(byte[]? status)
        {
            var deviceStatus = new DeviceStatus();
            if (status == null)
            {
                return deviceStatus;
            }
            for (var i = 0; i < status.Length; i++)
            {
                byte mask = 0b10000000;
                byte b = status[i];
                // Byte 3 shows the switches SW1 .. SW7 state
                if (i == 3)
                {
                    var switchData = new List<string>();
                    // Skip bit 7
                    for (var j = 0; j < 7; j++)
                    {
                        mask >>= 1;
                        var switchState = ((mask & b) != 0) ? "ON" : "OFF";
                        switchData.Add($"SW{7 - j}={switchState}");
                    }
                    deviceStatus.AddInfo(string.Join(", ", switchData));
                }
                else
                {
                    for (var j = 0; j < 8; j++)
                    {
                        if ((mask & b) != 0)
                        {
                            var (statusBitsCode, statusBitsText, statusBitStringType) = StatusBitsStrings[i * 8 + (7 - j)];
                            deviceStatus.AddMessage(new StatusMessage
                            {
                                Type = statusBitStringType,
                                Code = statusBitsCode,
                                Text = statusBitsText
                            });
                        }
                        mask >>= 1;
                    }
                }
            }
            return deviceStatus;
        }
    }
}
