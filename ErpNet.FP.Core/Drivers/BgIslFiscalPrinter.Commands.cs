namespace ErpNet.FP.Core.Drivers
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Numerics;
    using System.Text;
    using Serilog;

    public abstract partial class BgIslFiscalPrinter : BgFiscalPrinter
    {
        protected const byte
            CommandGetStatus = 0x4a,
            CommandGetDeviceInfo = 0x5a,
            CommandMoneyTransfer = 0x46,
            CommandOpenFiscalReceipt = 0x30,
            CommandCloseFiscalReceipt = 0x38,
            CommandAbortFiscalReceipt = 0x3c,
            CommandOpenNonFiscalReceipt = 0x26,
            CommandNonFiscalReceiptText = 0x2a,
            CommandCloseNonFiscalReceipt = 0x27,
            CommandSetClientInfo = 0x39,
            CommandFiscalReceiptTotal = 0x35,
            CommandFiscalReceiptComment = 0x36,
            CommandFiscalReceiptSale = 0x31,
            CommandPrintDailyReport = 0x45,
            CommandGetDateTime = 0x3e,
            CommandSetDateTime = 0x3d,
            CommandGetReceiptStatus = 0x4c,
            CommandGetReceiptInfo = 0x67,
            CommandGetLastDocumentNumber = 0x71,
            CommandGetTaxIdentificationNumber = 0x63,
            CommandPrintLastReceiptDuplicate = 0x6D,
            CommandSubtotal = 0x33,
            CommandPrintReportForDate = 0x5E,
            CommandReadLastReceiptQRCodeData = 0x74,
            CommandGetInvoiceRange = 0x11,
            CommandSetInvoiceRange = 0x11,
            CommandToPinpad = 0x37;

        // Error for payment with pinpad when transaction may be successful in pinpad, but unsuccessful in fiscal device
        protected const string DatecsPinpadErrorUnfinishedTransaction = "-111560";
        // Pinpad commands – option ‘13’ - After error (-111560) by "CommandFiscalReceiptTotal" and do "Print pinpad receipt"
        protected const string DatecsFinalizePinpadTransactionAndPrintReceipt = "13\t1\t";
        // Pinpad commands – option ‘15’ - Print receipt for pinpad after successful transaction
        protected const string DatecsXPinpadPrintReceipt = "15\t";
        // Pinpad commands – option ‘5’ - End of day from pinpad
        protected const string DatecsXPinpadEndOfDay = "5\t";
        // Pinpad commands – option ‘6’ - Report from pinpad
        protected const string DatecsXPinpadReportFromPinpad = "6\t";


        public override string GetReversalReasonText(ReversalReason reversalReason)
        {
            return reversalReason switch
            {
                ReversalReason.OperatorError => "1",
                ReversalReason.Refund => "0",
                ReversalReason.TaxBaseReduction => "2",
                _ => "1",
            };
        }

        public virtual (string, DeviceStatus) GetStatus()
        {
            return Request(CommandGetStatus);
        }

        public virtual (string, DeviceStatus) GetTaxIdentificationNumber()
        {
            return Request(CommandGetTaxIdentificationNumber);
        }

        public virtual (string, DeviceStatus) GetLastDocumentNumber(string closeReceiptResponse)
        {
            return Request(CommandGetLastDocumentNumber);
        }
        public virtual (string, DeviceStatus) SubtotalChangeAmount(Decimal amount)
        {
            return Request(CommandSubtotal, $"10;{amount.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        public virtual (BigInteger?, DeviceStatus) GetCurrentInvoiceNumber()
        {
            var (receiptInfoResponse, deviceStatus) = Request(CommandGetReceiptInfo);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo("Error occurred while reading current receipt info");
                return (null, deviceStatus);
            }

            var fields = receiptInfoResponse.Split(',');
            if (fields.Length < 11)
            {
                deviceStatus.AddInfo($"Error occured while parsing current receipt info");
                deviceStatus.AddError("E409", "Wrong format of receipt status");
                return (null, deviceStatus);
            }

            try
            {
                return (BigInteger.Parse(fields[10]), deviceStatus);
            }
            catch (Exception e)
            {
                deviceStatus = new DeviceStatus();
                deviceStatus.AddInfo($"Error occured while parsing the current invoice number");
                deviceStatus.AddError("E409", e.Message);
                return (null, deviceStatus);
            }
        }

        public virtual (decimal?, DeviceStatus) GetReceiptAmount()
        {
            decimal? receiptAmount = null;

            var (receiptStatusResponse, deviceStatus) = Request(CommandGetReceiptStatus, "T");
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading last receipt status");
                return (null, deviceStatus);
            }

            var fields = receiptStatusResponse.Split(',');
            if (fields.Length < 3)
            {
                deviceStatus.AddInfo($"Error occured while parsing last receipt status");
                deviceStatus.AddError("E409", "Wrong format of receipt status");
                return (null, deviceStatus);
            }

            try
            {
                var amountString = fields[2];
                if (amountString.Length > 0)
                {
                    switch (amountString[0])
                    {
                        case '+':
                            receiptAmount = decimal.Parse(amountString.Substring(1), CultureInfo.InvariantCulture) / 100m;
                            break;
                        case '-':
                            receiptAmount = -decimal.Parse(amountString.Substring(1), CultureInfo.InvariantCulture) / 100m;
                            break;
                        default:
                            if (amountString.Contains("."))
                            {
                                receiptAmount = decimal.Parse(amountString, CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                receiptAmount = decimal.Parse(amountString, CultureInfo.InvariantCulture) / 100m;
                            }
                            break;
                    }
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


        public virtual (string, DeviceStatus) MoneyTransfer(decimal amount)
        {
            return Request(CommandMoneyTransfer, amount.ToString("F2", CultureInfo.InvariantCulture));
        }

        public virtual (string, DeviceStatus) SetDeviceDateTime(DateTime dateTime)
        {
            return Request(CommandSetDateTime, dateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture));
        }

        public virtual (string, DeviceStatus) GetFiscalMemorySerialNumber()
        {
            var (rawDeviceInfo, deviceStatus) = GetRawDeviceInfo();
            var fields = rawDeviceInfo.Split(',');
            if (fields != null && fields.Length > 0)
            {
                return (fields[^1], deviceStatus);
            }
            else
            {
                deviceStatus.AddInfo($"Error occured while reading device info");
                deviceStatus.AddError("E409", $"Wrong number of fields");
                return (string.Empty, deviceStatus);
            }
        }

        public virtual (System.DateTime?, DeviceStatus) GetDateTime()
        {
            var (dateTimeResponse, deviceStatus) = Request(CommandGetDateTime);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo($"Error occured while reading current date and time");
                return (null, deviceStatus);
            }


            if (DateTime.TryParseExact(dateTimeResponse,
                "dd-MM-yy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime1))
            {
                return (dateTime1, deviceStatus);
            }
            else if (DateTime.TryParseExact(dateTimeResponse,
                "dd.MM.yy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime2))
            {
                return (dateTime2, deviceStatus);
            }
            else
            {
                deviceStatus.AddInfo($"Error occured while parsing current date and time");
                deviceStatus.AddError("E409", $"Wrong format of date and time");
                return (null, deviceStatus);
            }
        }

        public virtual (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            bool isInvoice = false)
        {
            var header = string.Join(",",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "0000").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    uniqueSaleNumber
                });

            if (isInvoice)
            {
                header += "\tI";
            }

            return Request(CommandOpenFiscalReceipt, header);
        }

        public virtual (string, DeviceStatus) OpenReversalReceipt(ReversalReason reason,
            string receiptNumber,
            DateTime receiptDateTime,
            string fiscalMemorySerialNumber,
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            string invoiceNumber)
        {
            // Protocol: {ClerkNum},{Password},{UnicSaleNum}[{Tab}{Refund}{Reason},{DocLink},{DocLinkDT}{Tab}{FiskMem}
            //           |{Credit}|{InvLivk},{Reason},{DocLink},{DocLinkDT}{Tab}{FiskMem}]
            // TODO: debug?
            var headerData = new StringBuilder()
                .Append(
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Administrator.ID", "20")
                        :
                        operatorId
                )
                .Append(',')
                .Append(
                    String.IsNullOrEmpty(operatorPassword) ?
                        Options.ValueOrDefault("Administrator.Password", "9999").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword
                )
                .Append(',')
                .Append(uniqueSaleNumber)
                .Append('\t')
                .Append('R')
                .Append(GetReversalReasonText(reason))
                .Append(',')
                .Append(receiptNumber)
                .Append(',')
                .Append(receiptDateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(fiscalMemorySerialNumber);

            return Request(CommandOpenFiscalReceipt, headerData.ToString());
        }

        public virtual (string, DeviceStatus) OpenNonFiscalReceipt()
        {
            return Request(CommandOpenNonFiscalReceipt);
        }

        public virtual (string, DeviceStatus) PrintNonFiscalReceiptText(
            string text,
            bool bold = false,
            bool italic = false,
            bool underline = false,
            LineHeight height = LineHeight.OneLine
        )
        {
            return Request(
                CommandNonFiscalReceiptText,
                text.WithMaxLength(Info.CommentTextMaxLength)
            );
        }

        public virtual (string, DeviceStatus) CloseNonFiscalReceipt()
        {
            return Request(CommandCloseNonFiscalReceipt);
        }

        public virtual (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0,
            decimal priceModifierValue = 0,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int ItemCode = 999)
        {
            var itemData = new StringBuilder();
            if (department <= 0) {
                itemData
                    .Append(itemText.WithMaxLength(Info.ItemTextMaxLength))
                    .Append('\t').Append(GetTaxGroupText(taxGroup))
                    .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture));
            }
            else
            {
                itemData
                    .Append(itemText.WithMaxLength(Info.ItemTextMaxLength))
                    .Append('\t').Append(department).Append('\t')
                    .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture));
            }
            if (quantity != 0)
            {
                itemData
                    .Append('*')
                    .Append(quantity.ToString(CultureInfo.InvariantCulture));
            }
            if (priceModifierType != PriceModifierType.None)
            {
                itemData
                    .Append(
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.SurchargePercent
                        ? ',' : '$')
                    .Append((
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.DiscountAmount
                        ? -priceModifierValue : priceModifierValue).ToString("F2", CultureInfo.InvariantCulture));
            }
            return Request(CommandFiscalReceiptSale, itemData.ToString());
        }

        public virtual (string, DeviceStatus) AddComment(string text)
        {
            return Request(
                CommandFiscalReceiptComment,
                text.WithMaxLength(Info.CommentTextMaxLength)
            );
        }

        public virtual (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            string uid = "";

            switch (invoice.Type)
            {
                case UIDType.PersonalID:
                    uid = "#";
                    break;
                case UIDType.PersonalIDForeigner:
                    uid = "*";
                    break;
                case UIDType.ServiceNumber:
                    uid = "^";
                    break;
            }

            uid += invoice.UID;
            var addressLines = invoice.ClientAddress.Substring(0, 28);
            if (invoice.ClientAddress.Length > 28)
            {
                addressLines += "\n" + invoice.ClientAddress.Substring(28, 34);
            }

            var clientData = (new StringBuilder())
                .Append(uid)
                .Append('\t')
                .Append(invoice.SellerName.WithMaxLength(26))
                .Append('\t')
                .Append(invoice.ReceiverName.WithMaxLength(26))
                .Append('\t')
                .Append(invoice.BuyerName.WithMaxLength(26))
                .Append('\t')
                .Append(invoice.VatNumber)
                .Append('\t')
                .Append(addressLines)
            ;

            return Request(
                CommandSetClientInfo,
                clientData.ToString()
            );
        }

        public virtual (string, DeviceStatus) CloseReceipt()
        {
            return Request(CommandCloseFiscalReceipt);
        }

        public virtual (string, DeviceStatus) AbortReceipt()
        {
            return Request(CommandAbortFiscalReceipt);
        }

        public virtual (string, DeviceStatus) FullPayment()
        {
            return Request(CommandFiscalReceiptTotal, "\t");
        }

        public virtual (string, DeviceStatus) AddPayment(decimal amount, PaymentType paymentType)
        {
            var paymentData = new StringBuilder()
                .Append('\t')
                .Append(GetPaymentTypeText(paymentType))
                .Append(amount.ToString("F2", CultureInfo.InvariantCulture));
            return Request(CommandFiscalReceiptTotal, paymentData.ToString());
        }

        public virtual (string, DeviceStatus) PrintDailyReport(bool zeroing = true)
        {
            if (zeroing)
            {
                return Request(CommandPrintDailyReport);
            }
            else
            {
                return Request(CommandPrintDailyReport, "2");
            }
        }

        public virtual (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var deviceStatus = new DeviceStatus();
            deviceStatus.AddError("0", "Monthly report not supported for this device");
            return (String.Empty, deviceStatus);
        }

        public virtual (string, DeviceStatus) GetLastReceiptQRCodeData()
        {
            return Request(CommandReadLastReceiptQRCodeData);
        }

        public virtual (string, DeviceStatus) GetRawDeviceInfo()
        {
            return Request(CommandGetDeviceInfo, "1");
        }

        public override DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange)
        {
            var (_, result) =  Request(CommandSetInvoiceRange, invoiceRange.Start + ";" + invoiceRange.End);
            return result;
        }

        public override DeviceStatusWithInvoiceRange GetInvoiceRange()
        {
            var (invoiceRangeResponse, status) = Request(CommandGetInvoiceRange);
            var deviceStatus = new DeviceStatusWithInvoiceRange(status);
            if (!deviceStatus.Ok)
            {
                deviceStatus.AddInfo("Error occurred while reading invoice range");
                return deviceStatus;
            }

            var fields = invoiceRangeResponse.Split(';');
            if (fields.Length < 2)
            {
                deviceStatus.AddInfo($"Error occured while parsing invoice range info");
                deviceStatus.AddError("E409", "Wrong format of invoice range");
                return deviceStatus;
            }

            try
            {
                deviceStatus.Start = BigInteger.Parse(fields[0]);
                deviceStatus.End = BigInteger.Parse(fields[1]);
            }
            catch (Exception e)
            {
                deviceStatus.AddInfo($"Error occured while parsing invoice range info");
                deviceStatus.AddError("E409", e.Message);
            }

            return deviceStatus;
        }
    }
}