namespace ErpNet.FP.Core.Drivers.BgEltrade
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;
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

        public override (ReceiptInfo, DeviceStatus) PrintReceipt(Receipt receipt)
        {
            var receiptInfo = new ReceiptInfo();
            BigInteger? invoiceNumber = null;

            // Abort all unfinished or erroneous receipts
            AbortReceipt();

            // Opening receipt
            var (_, deviceStatus) = OpenReceipt(
                receipt.UniqueSaleNumber,
                receipt.Operator,
                receipt.OperatorPassword,
                receipt.Invoice != null
            );
            if (!deviceStatus.Ok)
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occured while opening new fiscal receipt");
                return (receiptInfo, deviceStatus);
            }

            if (receipt.Invoice != null)
            {
                var (isValid, rangeCheckResult) = InvoiceRangeCheck();

                if (!rangeCheckResult.Ok || !isValid)
                {
                    return (receiptInfo, rangeCheckResult);
                }
                
                var (invoiceNumberTemp, deviceStatusInv) = GetCurrentInvoiceNumber();
                if (!invoiceNumberTemp.HasValue || !deviceStatusInv.Ok)
                {
                    return (receiptInfo, deviceStatusInv);
                }

                invoiceNumber = invoiceNumberTemp;
            }

            // Printing receipt's body
            (receiptInfo, deviceStatus) = PrintReceiptBody(receipt);
            if (!deviceStatus.Ok)
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occured while printing receipt items");
            }
            
            if (invoiceNumber.HasValue)
            {
                receiptInfo.InvoiceNumber = invoiceNumber;
            }

            return (receiptInfo, deviceStatus);
        }

        public override (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            var addressLines = invoice.ClientAddress.Substring(0, 36);
            if (invoice.ClientAddress.Length > 36)
            {
                addressLines += "\n" + invoice.ClientAddress.Substring(36, 36);
            }

            var clientData = (new StringBuilder()).Append(invoice.UID)
                .Append('\t').Append(!String.IsNullOrEmpty(invoice.SellerName) ? invoice.SellerName.WithMaxLength(26) : "")
                .Append('\t').Append(!String.IsNullOrEmpty(invoice.ReceiverName) ? invoice.ReceiverName.WithMaxLength(26) : "")
                .Append('\t').Append(!String.IsNullOrEmpty(invoice.BuyerName) ? invoice.BuyerName.WithMaxLength(26) : "")
                .Append('\t').Append(!String.IsNullOrEmpty(invoice.VatNumber) ? invoice.VatNumber : "")
                .Append('\t').Append(!String.IsNullOrEmpty(addressLines) ? addressLines : "")
            ;


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
                response.Start = BigInteger.Parse(split[0]);
                response.End = BigInteger.Parse(split[1]);
            }
            catch (Exception e)
            {
                response.AddError("E409", "Error occurred while parsing invoice range data");
                response.AddInfo(e.Message);
            }

            return response;
        }

        public (BigInteger?, DeviceStatus) GetCurrentInvoiceNumber(bool creditNote = false)
        {
            var (data, result) =  Request(CommandGetInvoiceRange, creditNote ? "S" : null);
            BigInteger? current = null;

            if (!result.Ok)
            {
                result.AddError("E499", "An error occurred while setting invoice range [2]");
                return (null, result);
            }

            var split = data.Split(",");
            try
            {
                current = BigInteger.Parse(split[2]);
            }
            catch (Exception e)
            {
                result.AddError("E409", "Error occurred while parsing invoice range data [2]");
                result.AddInfo(e.Message);
            }

            return (current, result);
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
            BigInteger? creditNoteNumber = null;

            if (reversalReceipt.Invoice != null)
            {
               var (creditNumber, response) = GetCurrentInvoiceNumber(true);
               if (!response.Ok)
               {
                   return (receiptInfo, response);
               }

               creditNoteNumber = creditNumber;
            }

            // Abort all unfinished or erroneous receipts
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

            if (creditNoteNumber.HasValue)
            {
                receiptInfo.InvoiceNumber = creditNoteNumber;
            }

            return (receiptInfo, deviceStatus);
        }

        public override (ReceiptInfo, DeviceStatus) PrintReceiptBody(Receipt receipt)
        {
            var receiptInfo = new ReceiptInfo();

            var (fiscalMemorySerialNumber, deviceStatus) = GetFiscalMemorySerialNumber();
            if (!deviceStatus.Ok)
            {
                return (receiptInfo, deviceStatus);
            }

            receiptInfo.FiscalMemorySerialNumber = fiscalMemorySerialNumber;

            uint itemNumber = 0;
            // Receipt items
            if (receipt.Items != null) foreach (var item in receipt.Items)
            {
                itemNumber++;
                if (item.Type == ItemType.Comment)
                {
                    (_, deviceStatus) = AddComment(item.Text);
                }
                else if (item.Type == ItemType.Sale)
                {
                    try
                    {
                        (_, deviceStatus) = AddItem(
                            item.Department,
                            item.Text,
                            item.UnitPrice,
                            item.TaxGroup,
                            item.Quantity,
                            item.PriceModifierValue,
                            item.PriceModifierType);
                    }
                    catch (StandardizedStatusMessageException e)
                    {
                        deviceStatus = new DeviceStatus();
                        deviceStatus.AddError(e.Code, e.Message);
                        break;
                    }
                }
                else if (item.Type == ItemType.SurchargeAmount)
                {
                    (_, deviceStatus) = SubtotalChangeAmount(item.Amount);
                }
                else if (item.Type == ItemType.DiscountAmount)
                {                        
                    (_, deviceStatus) = SubtotalChangeAmount(-item.Amount);
                }
                if (!deviceStatus.Ok)
                {
                    deviceStatus.AddInfo($"Error occurred in Item {itemNumber}");
                    return (receiptInfo, deviceStatus);
                }
            }

            // Receipt payments
            if (receipt.Payments == null || receipt.Payments.Count == 0)
            {
                (_, deviceStatus) = FullPayment();
                if (!deviceStatus.Ok)
                {
                    deviceStatus.AddInfo($"Error occurred while making full payment in cash");
                    return (receiptInfo, deviceStatus);
                }
            }
            else
            {
                uint paymentNumber = 0;
                foreach (var payment in receipt.Payments)
                {
                    paymentNumber++;

                    if (payment.PaymentType == PaymentType.Change)
                    {
                        // PaymentType.Change is abstract payment, 
                        // used only for computing the total sum of the payments.
                        // So we will skip it.
                        continue;
                    }

                    try
                    {
                        (_, deviceStatus) = AddPayment(payment.Amount, payment.PaymentType);
                    }
                    catch (StandardizedStatusMessageException e)
                    {
                        deviceStatus = new DeviceStatus();
                        deviceStatus.AddError(e.Code, e.Message);
                    }

                    if (!deviceStatus.Ok)
                    {
                        deviceStatus.AddInfo($"Error occurred in Payment {paymentNumber}");
                        return (receiptInfo, deviceStatus);
                    }
                }
            }

            if (receipt.Invoice != null)
            {
                (_, deviceStatus) = SetInvoice(receipt.Invoice);
            }

            itemNumber = 0;
            if (receipt.Items != null) foreach (var item in receipt.Items)
            {
                itemNumber++;
                if (item.Type == ItemType.FooterComment)
                {
                    (_, deviceStatus) = AddComment(item.Text);
                    if (!deviceStatus.Ok)
                    {
                        deviceStatus.AddInfo($"Error occurred in Item {itemNumber}");
                        return (receiptInfo, deviceStatus);
                    }
                }
            }

            // Get the receipt date and time (current fiscal device date and time)
            DateTime? dateTime;
            (dateTime, deviceStatus) = GetDateTime();
            if (!dateTime.HasValue || !deviceStatus.Ok)
            {
                AbortReceipt();
                return (receiptInfo, deviceStatus);
            }
            receiptInfo.ReceiptDateTime = dateTime.Value;

            // Get receipt amount
            decimal? receiptAmount;
            (receiptAmount, deviceStatus) = GetReceiptAmount();
            if (!receiptAmount.HasValue || !deviceStatus.Ok)
            {
                AbortReceipt();
                return (receiptInfo, deviceStatus);
            }
            receiptInfo.ReceiptAmount = receiptAmount.Value;

            // Closing receipt
            string closeReceiptResponse;
            (closeReceiptResponse, deviceStatus) = CloseReceipt();
            if (!deviceStatus.Ok)
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occurred while closing the receipt");
                return (receiptInfo, deviceStatus);
            }

            // Get receipt number
            string lastDocumentNumberResponse;
            (lastDocumentNumberResponse, deviceStatus) = GetLastDocumentNumber(closeReceiptResponse);
            if (!deviceStatus.Ok || String.IsNullOrWhiteSpace(lastDocumentNumberResponse))
            {
                AbortReceipt();
                deviceStatus.AddInfo($"Error occurred while reading last receipt number");
                deviceStatus.AddError("E409", $"Last receipt number is empty");
                return (receiptInfo, deviceStatus);
            }
            receiptInfo.ReceiptNumber = lastDocumentNumberResponse;

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
