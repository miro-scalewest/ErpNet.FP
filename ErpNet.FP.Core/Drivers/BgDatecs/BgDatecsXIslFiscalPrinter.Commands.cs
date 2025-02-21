namespace ErpNet.FP.Core.Drivers.BgDatecs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;

    /// <summary>
    /// Fiscal printer using the ISL implementation of Datecs Bulgaria.
    /// </summary>
    /// <seealso cref="BgIslFiscalPrinter" />
    public partial class BgDatecsXIslFiscalPrinter : BgIslFiscalPrinter
    {
        protected const byte
           DatecsXCommandOpenStornoDocument = 0x2b,
           CommandGetInvoiceRange = 0x67,
           CommandSetInvoiceRange = 0x42,
           CommandEJInfo = 0x7d,
           Programming = 0xff;

        public override IDictionary<PaymentType, string> GetPaymentTypeMappings()
        {
            var paymentTypeMappings = new Dictionary<PaymentType, string> {
                { PaymentType.Cash,          "0" },
                { PaymentType.Check,         "3" },
                { PaymentType.Coupons,       "5" },
                { PaymentType.ExtCoupons,    "4" },
                { PaymentType.Card,          "1" }
            };
            ServiceOptions.RemapPaymentTypes(Info.SerialNumber, paymentTypeMappings);
            return paymentTypeMappings;
        }

        public override string GetTaxGroupText(TaxGroup taxGroup)
        {
            return taxGroup switch
            {
                TaxGroup.TaxGroup1 => "1",
                TaxGroup.TaxGroup2 => "2",
                TaxGroup.TaxGroup3 => "3",
                TaxGroup.TaxGroup4 => "4",
                TaxGroup.TaxGroup5 => "5",
                TaxGroup.TaxGroup6 => "6",
                TaxGroup.TaxGroup7 => "7",
                TaxGroup.TaxGroup8 => "8",
                _ => throw new StandardizedStatusMessageException($"Tax group {taxGroup} unsupported", "E411"),
            };
        }

        public override (string, DeviceStatus) SubtotalChangeAmount(Decimal amount)
        {
            // {Print}<SEP>{Display}<SEP>{DiscountType}<SEP>{DiscountValue}<SEP>
            return Request(CommandSubtotal, string.Join("\t",
                "1",
                "0",
                amount < 0 ? "4" : "3",
                Math.Abs(amount).ToString("F2", CultureInfo.InvariantCulture),
                ""));
        }

        public override (string, DeviceStatus) SetDeviceDateTime(DateTime dateTime)
        {
            return Request(CommandSetDateTime, dateTime.ToString("dd-MM-yy HH:mm:ss\t", CultureInfo.InvariantCulture));
        }

        public override DeviceStatusWithCashAmount Cash(Credentials credentials)
        {
            var (response, status) = Request(CommandMoneyTransfer, "0\t0\t");
            var statusEx = new DeviceStatusWithCashAmount(status);
            var tabFields = response.Split('\t');
            if (tabFields.Length != 5)
            {
                statusEx.AddInfo("Error occured while reading cash amount");
                statusEx.AddError("E409", "Invalid format");
            }
            else
            {
                var amountString = tabFields[1];
                if (amountString.Contains("."))
                {
                    statusEx.Amount = decimal.Parse(amountString, CultureInfo.InvariantCulture);
                }
                else
                {
                    statusEx.Amount = decimal.Parse(amountString, CultureInfo.InvariantCulture) / 100m;
                }
            }
            return statusEx;
        }

        public override (string, DeviceStatus) GetTaxIdentificationNumber()
        {
            var (response, deviceStatus) = Request(CommandGetTaxIdentificationNumber);
            var commaFields = response.Split('\t');
            if (commaFields.Length == 2)
            {
                return (commaFields[1].Trim(), deviceStatus);
            }
            return (string.Empty, deviceStatus);
        }

        public override (BigInteger?, DeviceStatus) GetCurrentInvoiceNumber()
        {
            var (currentNumber, deviceStatus) = Request(Programming, "nInvoice\t\t\t");

            if (!deviceStatus.Ok)
            {
                deviceStatus.AddError("E499", "An error occurred while reading invoice range");
                return (null, deviceStatus);
            }

            try
            { 
                return (BigInteger.Parse(currentNumber.Split("\t")[1]), deviceStatus);
            }
            catch (Exception e)
            {
                deviceStatus.AddInfo($"Error occured while parsing the invoice range");
                deviceStatus.AddError("E409", e.Message);
            }

            return (null, deviceStatus);
        }

        public override DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange)
        {
            var (_, result) =  Request(CommandSetInvoiceRange, $"{invoiceRange.Start}\t{invoiceRange.End}\t");
            if (!result.Ok)
            {
                result.AddError("E499", "An error occurred while setting invoice range");
            }

            return result;
        }

        public override DeviceStatusWithInvoiceRange GetInvoiceRange()
        {
            var (startNumber, startNumberOutput) = Request(Programming, "InvoiceRangeBeg\t\t\t");
            var (endNumber, endNumberOutput) = Request(Programming, "InvoiceRangeEnd\t\t\t");
            var (currentNumber, currentNumberOutput) = Request(Programming, "nInvoice\t\t\t");
            var result = new DeviceStatusWithInvoiceRange(startNumberOutput);

            if (!startNumberOutput.Ok || !endNumberOutput.Ok)
            {
                result.AddError("E499", "An error occurred while reading invoice range");
                return result;
            }

            try
            {
                result.Start = BigInteger.Parse(startNumber.Split("\t")[1]);
                result.End = BigInteger.Parse(endNumber.Split("\t")[1]);
                if (currentNumberOutput.Ok && currentNumber.Split("\t")[0] == "0")
                {
                    result.Current = BigInteger.Parse(currentNumber.Split("\t")[1]);
                }
            }
            catch (Exception e)
            {
                result.AddInfo($"Error occured while parsing the invoice range");
                result.AddError("E409", e.Message);
            }

            return result;
        }

        public override (decimal?, DeviceStatus) GetReceiptAmount()
        {
            decimal? receiptAmount = null;

            var (receiptStatusResponse, deviceStatus) = Request(CommandGetReceiptStatus);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading last receipt status");
                return (null, deviceStatus);
            }

            var fields = receiptStatusResponse.Split('\t');
            if (fields.Length < 5)
            {
                deviceStatus.AddInfo($"Error occured while parsing last receipt status");
                deviceStatus.AddError("E409", "Wrong format of receipt status");
                return (null, deviceStatus);
            }

            try
            {
                var amountString = fields[4];
                if (amountString.Length > 0)
                {
                    receiptAmount = (amountString[0]) switch
                    {
                        '+' => decimal.Parse(amountString.Substring(1), System.Globalization.CultureInfo.InvariantCulture) / 100m,
                        '-' => -decimal.Parse(amountString.Substring(1), System.Globalization.CultureInfo.InvariantCulture) / 100m,
                        _ => decimal.Parse(amountString, System.Globalization.CultureInfo.InvariantCulture),
                    };
                }

            }
            catch (Exception e)
            {
                deviceStatus = new DeviceStatus();
                deviceStatus.AddInfo($"Error occured while parsing the amount of last receipt status");
                deviceStatus.AddError("E409", e.Message);
                return (null, deviceStatus);
            }

            return (receiptAmount, deviceStatus);
        }

        public override (System.DateTime?, DeviceStatus) GetDateTime()
        {
            var (dateTimeResponse, deviceStatus) = Request(CommandGetDateTime);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading current date and time");
                return (null, deviceStatus);
            }

            var fields = dateTimeResponse.Split('\t');
            if (fields.Length < 2)
            {
                deviceStatus.AddInfo($"Error occured while parsing date and time");
                deviceStatus.AddError("E409", "Wrong format of date and time");
                return (null, deviceStatus);
            }

            var fixedDateAndTimeString = fields[1].Replace(" DST", "");

            try
            {
                var dateTime = DateTime.ParseExact(fixedDateAndTimeString,
                    "dd-MM-yy HH:mm:ss",
                    CultureInfo.InvariantCulture);
                return (dateTime, deviceStatus);
            }
            catch
            {
                deviceStatus.AddInfo($"Error occured while parsing current date and time");
                deviceStatus.AddError("E409", $"Wrong format of date and time");
                return (null, deviceStatus);
            }
        }

        public override (string, DeviceStatus) GetLastDocumentNumber(string closeReceiptResponse)
        {
            var deviceStatus = new DeviceStatus();
            var fields = closeReceiptResponse.Split('\t');
            if (fields.Length < 2)
            {
                deviceStatus.AddInfo($"Error occured while parsing close receipt response");
                deviceStatus.AddError("E409", $"Wrong format of close receipt response");
                return (string.Empty, deviceStatus);
            }
            return (fields[1], deviceStatus);
        }

        public override (string, DeviceStatus) MoneyTransfer(decimal amount)
        {
            // Protocol: {Type}<SEP>{Amount}<SEP>
            return Request(CommandMoneyTransfer, string.Join("\t",
                amount < 0 ? "1" : "0",
                Math.Abs(amount).ToString("F2", CultureInfo.InvariantCulture),
                ""));
        }

        public override (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0m,
            decimal priceModifierValue = 0m,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int ItemCode = 999)
        {
            string PriceModifierTypeToProtocolValue()
            {
                return priceModifierType switch
                {
                    PriceModifierType.None => "0",
                    PriceModifierType.DiscountPercent => "2",
                    PriceModifierType.DiscountAmount => "4",
                    PriceModifierType.SurchargePercent => "1",
                    PriceModifierType.SurchargeAmount => "3",
                    _ => "",
                };
            }

            // Protocol: {PluName}<SEP>{TaxCd}<SEP>{Price}<SEP>{Quantity}<SEP>{DiscountType}<SEP>{DiscountValue}<SEP>{Department}<SEP>
            var itemData = string.Join("\t",
                itemText.WithMaxLength(Info.ItemTextMaxLength),
                GetTaxGroupText(taxGroup),
                unitPrice.ToString("F2", CultureInfo.InvariantCulture),
                quantity == 0m ? string.Empty : quantity.ToString(CultureInfo.InvariantCulture),
                PriceModifierTypeToProtocolValue(),
                priceModifierValue.ToString("F2", CultureInfo.InvariantCulture),
                department.ToString(),
                "");
            return Request(CommandFiscalReceiptSale, itemData);
        }

        public override (string, DeviceStatus) AddComment(string text)
        {
            return Request(
                CommandFiscalReceiptComment,
                text.WithMaxLength(Info.CommentTextMaxLength) + "\t"
            );
        }

        public override (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            var address = invoice.ClientAddress.Length > 36
                    ? new string[]
                    {
                        invoice.ClientAddress.Substring(0, 36),
                        invoice.ClientAddress.Substring(36).WithMaxLength(36)
                    }
                    : new string[] {invoice.ClientAddress, ""};
            var clientData = string.Join("\t",
                invoice.SellerName.WithMaxLength(36),
                invoice.ReceiverName.WithMaxLength(36),
                invoice.BuyerName.WithMaxLength(36),
                address[0],
                address[1],
                ((int)invoice.Type).ToString(),
                invoice.UID,
                invoice.VatNumber,
                ""
            );

            return Request(
                CommandSetClientInfo,
                clientData
            );
        }

        public override (string, DeviceStatus) FullPayment()
        {
            return Request(CommandFiscalReceiptTotal, "\t\t\t");
        }

        public override (string, DeviceStatus) AddPayment(decimal amount, PaymentType paymentType)
        {
            var paymentTypeText = GetPaymentTypeText(paymentType);
            if (paymentType == PaymentType.Card && DeviceInfo.SupportPaymentTerminal && DeviceInfo.UsePaymentTerminal)
                paymentTypeText = "2";
            
            // Protocol: {PaidMode}<SEP>{Amount}<SEP>{Type}<SEP>
            var paymentData = string.Join("\t",
                paymentTypeText,
                amount.ToString("F2", CultureInfo.InvariantCulture),
                "1",
                "");

            var (data, status) = Request(CommandFiscalReceiptTotal, paymentData);

            if(data.Length > 0 && paymentType == PaymentType.Card && DeviceInfo.SupportPaymentTerminal &&
                DeviceInfo.UsePaymentTerminal)
            {
                // check response for specific error when using pinpad
                var returnedData = data.Split('\t');
                decimal paysum;
                if (returnedData[0] == DatecsPinpadErrorUnfinishedTransaction && returnedData.Length > 1 &&
                    decimal.TryParse(returnedData[1], out paysum) && paysum == amount)
                {
                    // Recovery from error, suppose it prints receipt for pinpad transaction
                    var (tempData, _) = Request(CommandToPinpad, DatecsFinalizePinpadTransactionAndPrintReceipt);
                    if (tempData.Length > 0 && tempData.Split("\t")[0] == "0")
                    {
                        // If execution of FinalizePinpadTransaction is successful,
                        // then replace error code of previous command (CommandFiscalReceiptTotal) with NOERROR ("0") code
                        returnedData[0] = "0";
                        data = string.Join("\t", returnedData);
                    }
                }
                else if (returnedData[0] == "0")
                {
                    // Print receipt for pinpad transaction
                    (_, _) = Request(CommandToPinpad, DatecsXPinpadPrintReceipt);
                }
                else
                {
                    CheckPinpadResponse(returnedData[0], status);
                }
            }

            return (data, status);
        }

        public override string GetReversalReasonText(ReversalReason reversalReason)
        {
            return reversalReason switch
            {
                ReversalReason.OperatorError => "0",
                ReversalReason.Refund => "1",
                ReversalReason.TaxBaseReduction => "2",
                _ => "0",
            };
        }

        public override (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            bool isInvoice = false)
        {
            string header;
            if (string.IsNullOrEmpty(uniqueSaleNumber))
            {
                header = string.Join("\t",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "0000").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    "1",
                    "",
                    ""
                });

            }
            else
            {
                header = string.Join("\t",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "0000").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    uniqueSaleNumber,
                    "1",
                    isInvoice ? "I" : "",
                    ""
                });

            }
            return Request(CommandOpenFiscalReceipt, header);
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
            // Protocol: {OpCode}<SEP>{OpPwd}<SEP>{TillNmb}<SEP>{Storno}<SEP>{DocNum}<SEP>{DateTime}<SEP>
            //           {FM Number}<SEP>{Invoice}<SEP>{ToInvoice}<SEP>{Reason}<SEP>{NSale}<SEP>
            var headerData = string.Join("\t",
                String.IsNullOrEmpty(operatorId) ?
                    Options.ValueOrDefault("Operator.ID", "1")
                    :
                    operatorId,
                String.IsNullOrEmpty(operatorId) ?
                    Options.ValueOrDefault("Operator.Password", "0000").WithMaxLength(Info.OperatorPasswordMaxLength)
                    :
                    operatorPassword,
                "1",
                GetReversalReasonText(reason),
                receiptNumber,
                receiptDateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture),
                fiscalMemorySerialNumber,
                String.IsNullOrEmpty(invoiceNumber) ? "" : "I",
                invoiceNumber,
                "",
                uniqueSaleNumber,
                "");

            return Request(DatecsXCommandOpenStornoDocument, headerData.ToString());
        }

        public override (string, DeviceStatus) PrintNonFiscalReceiptText(
            string text,
            bool bold = false,
            bool italic = false,
            bool underline = false,
            LineHeight height = LineHeight.OneLine
        )
        {
            // Protocol: {Text}<SEP>{Bold}<SEP>{Italic}<SEP>{Hght}<SEP>{Underline}<SEP>{alignment}<SEP>
            var headerData = string.Join("\t",
                text.WithMaxLength(Info.CommentTextMaxLength),
                bold ? 1 : 0,
                italic ? 1 : 0,
                height == LineHeight.OneLine ? 0 : 1,
                underline ? 1 : 0,
                0,
                null // Must end with a separator
            );

            return Request(CommandNonFiscalReceiptText, headerData.ToString());
        }

        public override (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var startDateString = startDate.ToString("dd-MM-yy", CultureInfo.InvariantCulture);
            var endDateString = endDate.ToString("dd-MM-yy", CultureInfo.InvariantCulture);
            var headerData = string.Join("\t",
                (int) type,
                startDateString,
                endDateString,
                null // Must end with a separator
            );
            Console.WriteLine("Datecs X: " + headerData);

            return Request(CommandPrintReportForDate, headerData.ToString());
        }

        public override (string, DeviceStatus) PrintDailyReport(bool zeroing = true)
        {
            (string response, DeviceStatus status) result;
            if (zeroing)
            {
                result = Request(CommandPrintDailyReport, "Z\t");
            }
            else
            {
                result = Request(CommandPrintDailyReport, "X\t");
            }
            if (!result.status.Ok)
                return result;
            if (DeviceInfo.SupportPaymentTerminal && DeviceInfo.UsePaymentTerminal)
            {
                if (zeroing)
                {
                    result = Request(CommandToPinpad, DatecsXPinpadEndOfDay);
                }
                else
                {
                    result = Request(CommandToPinpad, DatecsXPinpadReportFromPinpad);
                }
                CheckPinpadResponse(result.response.Split('\t')[0], result.status);
            }
            return result;
        }

        public override DeviceStatus PrintFiscalCopy(CopyInfo copyInfo)
        {
            var payload = string.Join("\t", "3", copyInfo.SlipId.ToString(), "0") + "\t";
            
            var (_, result) = Request(CommandEJInfo, payload);

            return result;
        }

        public void CheckPinpadResponse(string response, DeviceStatus status)
        {
            switch (response)
            {
                case "0":
                    break;
                case "-111509":
                    status.AddError("E601", "Payment terminal timeout");
                    break;
                case "-111546":
                case "-111558":
                case "-111560":
                case "-111512":
                case "-111550":
                case "-111555":
                case "-111557":
                case "-111561":
                    status.AddError("E602", "Pаyment terminal communication error");
                    break;
                case "-111518":
                    status.AddError("E603", "Payment transaction cancelled by user");
                    break;
                case "-111514":
                case "-111526":
                    status.AddError("E604", "Invalid PIN");
                    break;
                default:
                    status.AddError("E699", "General error from payment terminal");
                    break;
            }
        }

        // 8 Bytes x 8 bits

        protected static readonly (string?, string, StatusMessageType)[] StatusBitsStrings = new (string?, string, StatusMessageType)[] {
            ("E401", "Syntax error", StatusMessageType.Error),
            ("E402", "Command code is invalid", StatusMessageType.Error),
            ("E103", "The real time clock is not synchronized", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            ("E303", "Failure in printing mechanism", StatusMessageType.Error),
            ("E199", "General error", StatusMessageType.Error),
            ("E302", "Cover is open", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E403", "Overflow during command execution", StatusMessageType.Error),
            ("E404", "Command is not permitted", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E301", "End of paper", StatusMessageType.Error),
            ("W301", "Near paper end", StatusMessageType.Warning),
            ("E206", "EJ is full", StatusMessageType.Error),
            (null, "Fiscal receipt is open", StatusMessageType.Info),
            ("W202", "EJ nearly full", StatusMessageType.Warning),
            (null, "Nonfiscal receipt is open", StatusMessageType.Info),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E203", "Error when trying to access data stored in the FM", StatusMessageType.Error),
            (null, "Tax number is set", StatusMessageType.Info),
            (null, "Serial number and number of FM are set", StatusMessageType.Info),
            ("W201", "There is space for less then 60 reports in Fiscal memory", StatusMessageType.Warning),
            ("E201", "FM full", StatusMessageType.Error),
            ("E299", "FM general error", StatusMessageType.Error),
            ("E205", "Fiscal memory is not found or damaged", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),

            (null, string.Empty, StatusMessageType.Reserved),
            (null, "FM is formatted", StatusMessageType.Info),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, "Device is fiscalized", StatusMessageType.Info),
            (null, "VAT are set at least once", StatusMessageType.Info),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
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
            return deviceStatus;
        }

    }

    
}