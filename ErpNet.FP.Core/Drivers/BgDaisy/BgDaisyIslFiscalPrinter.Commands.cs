namespace ErpNet.FP.Core.Drivers.BgDaisy
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;
    using System.Text;

    /// <summary>
    /// Fiscal printer using the ISL implementation of Daisy Bulgaria.
    /// </summary>
    /// <seealso cref="BgIslFiscalPrinter" />
    public partial class BgDaisyIslFiscalPrinter : BgIslFiscalPrinter
    {
        protected const byte
            DaisyCommandGetDeviceConstants          = 0x80,
            DaisyCommandAbortFiscalReceipt          = 0x82,
            CommandPrintBriefReportForDate          = 0x4F,
            CommandPrintDetailedReportForDate       = 0x5E,
            DaisyCommandFiscalReceiptSaleDepartment = 0x8A,
            DaisyCommandSetParameter                = 0x96,
            DaisyCommandGetParameter                = 0x96;

        public override (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0,
            decimal priceModifierValue = 0,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int ItemCode = 999)
        {
            if (department <= 0) 
            {
                return base.AddItem(department, itemText, unitPrice, taxGroup, quantity, priceModifierValue,
                  priceModifierType);
            }

            var itemData = new StringBuilder()
                .Append(department).Append("@")
                .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture));

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
            return Request(DaisyCommandFiscalReceiptSaleDepartment, itemData.ToString());
        }

        public override (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            var clientData = (new StringBuilder()).Append(invoice.UID);

            if (invoice.VatNumber.Length > 0)
            {
                clientData.Append('\t').Append(invoice.VatNumber);

                if (invoice.SellerName.Length > 0)
                {
                    clientData.Append('\t').Append(invoice.SellerName);

                    if (invoice.ReceiverName.Length > 0)
                    {
                        clientData.Append('\t').Append(invoice.ReceiverName);

                        if (invoice.BuyerName.Length > 0)
                        {
                            clientData.Append('\t').Append(invoice.BuyerName);

                            if (invoice.ClientAddress.Length > 0)
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

        public override (string, DeviceStatus) AbortReceipt()
        {
            return Request(DaisyCommandAbortFiscalReceipt);
        }

        public (string, DeviceStatus) GetRawDeviceConstants()
        {
            return Request(DaisyCommandGetDeviceConstants);
        }

        public override (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var startDateString = startDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var endDateString = endDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var headerData = string.Join(",", startDateString, endDateString);
            Console.WriteLine("Daisy: " + headerData);

            return Request(
                type == ReportType.Brief
                    ? CommandPrintBriefReportForDate
                    : CommandPrintDetailedReportForDate,
                headerData
            );
        }

        public override DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange)
        {
            var (_, setInvoiceRangeStartResult) = Request(DaisyCommandSetParameter, "P18," + invoiceRange.Start);
            if (!setInvoiceRangeStartResult.Ok)
            {
                setInvoiceRangeStartResult.AddInfo("Error occurred while updating invoice start range");
                return setInvoiceRangeStartResult;
            }

            var (_, setInvoiceRangeEndResult) = Request(DaisyCommandSetParameter, "P19," + invoiceRange.End);
            if (!setInvoiceRangeEndResult.Ok)
            {
                setInvoiceRangeEndResult.AddInfo("Error occurred while updating invoice end range");
                return setInvoiceRangeEndResult;
            }

            return setInvoiceRangeEndResult;
        }

        public override (bool, DeviceStatus) InvoiceRangeCheck()
        {
            // Currently won't support any daisy range retrieval
            var status = new DeviceStatus();
            status.AddInfo("Daisy invoice range not supported yet");
            return (true, status);
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

                invoiceNumber = invoiceNumberTemp - 1;
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

        public override (string, DeviceStatus) OpenReversalReceipt(
            ReversalReason reason,
            string receiptNumber,
            DateTime receiptDateTime,
            string fiscalMemorySerialNumber,
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            string invoiceNumber)
        {
            if (!String.IsNullOrEmpty(invoiceNumber))
            {
                return this.OpenCreditNoteReceipt(
                    reason,
                    receiptNumber,
                    receiptDateTime,
                    fiscalMemorySerialNumber,
                    uniqueSaleNumber,
                    operatorId,
                    operatorPassword,
                    invoiceNumber
                );
            }

            return base.OpenReversalReceipt(reason, receiptNumber, receiptDateTime, fiscalMemorySerialNumber, uniqueSaleNumber, operatorId, operatorPassword, invoiceNumber);
        }

        public (string, DeviceStatus) OpenCreditNoteReceipt(
            ReversalReason reason,
            string receiptNumber,
            DateTime receiptDateTime,
            string fiscalMemorySerialNumber,
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            string invoiceNumber)
        {
            // Protocol: {ClerkNum},{Password},{UnicSaleNum}{Tab}{Credit}{InvLivk},{Reason},{DocLink},{DocLinkDT}{Tab}{FiskMem}]

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
                .Append('C')
                .Append(invoiceNumber)
                .Append(',')
                .Append(GetReversalReasonText(reason))
                .Append(',')
                .Append(receiptNumber)
                .Append(',')
                .Append(receiptDateTime.ToString("dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(fiscalMemorySerialNumber);

            return Request(CommandOpenFiscalReceipt, headerData.ToString());
        }

        public override (ReceiptInfo, DeviceStatus) PrintReversalReceipt(ReversalReceipt reversalReceipt)
        {
            var receiptInfo = new ReceiptInfo();
            BigInteger? invoiceNumber = null;

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

            if (reversalReceipt.Invoice != null)
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

                invoiceNumber = invoiceNumberTemp - 1;
            }

            (receiptInfo, deviceStatus) = PrintReceiptBody(reversalReceipt);
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

        public override (string, DeviceStatus) GetTaxIdentificationNumber()
        {
            var (response, deviceStatus) = Request(CommandGetTaxIdentificationNumber);
            return (response.Trim().Trim(new Char[] { '-' }), deviceStatus);
        }

        public override IDictionary<PaymentType, string> GetPaymentTypeMappings()
        {
            var paymentTypeMappings = new Dictionary<PaymentType, string> {
                { PaymentType.Cash,       "P" },
                { PaymentType.Card,       "C" },
                { PaymentType.Check,      "N" },
                { PaymentType.Reserved1,  "D" }
            };
            ServiceOptions.RemapPaymentTypes(Info.SerialNumber, paymentTypeMappings);
            return paymentTypeMappings;
        }

        // 6 Bytes x 8 bits

        protected static readonly (string?, string, StatusMessageType)[] StatusBitsStrings = new (string?, string, StatusMessageType)[] {
            ("E401", "Syntax error", StatusMessageType.Error),
            ("E402", "Invalid command", StatusMessageType.Error),
            ("E103", "Date and time are not set", StatusMessageType.Error),
            (null, "No external display", StatusMessageType.Info),
            ("E303", "Error in printing device", StatusMessageType.Error),
            ("E199", "General error", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E403", "Number field overflow", StatusMessageType.Error),
            ("E404", "Command not allowed in this mode", StatusMessageType.Error),
            ("E104", "Zeroed RAM", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            ("E306", "Error in cutter", StatusMessageType.Error),
            ("E408", "Wrong password", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E301", "No paper", StatusMessageType.Error),
            ("W301", "Near end of paper", StatusMessageType.Warning),
            ("E206", "No control paper", StatusMessageType.Error),
            (null, "Opened Fiscal Receipt", StatusMessageType.Info),
            ("W202", "Control paper almost full", StatusMessageType.Warning),
            (null, "Opened Non-fiscal Receipt", StatusMessageType.Info),
            (null, "Printing allowed", StatusMessageType.Info),
            (null, string.Empty, StatusMessageType.Reserved),

            // Byte 3 is special in Daisy, it contains error code, from bit 0 to bit 6
            // bit 7 is reserved
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E202", "Error while writing to FM", StatusMessageType.Error),
            ("E599", "No task from NRA", StatusMessageType.Error),
            ("E203", "Wrong record in FM", StatusMessageType.Error),
            ("W201", "FM almost full", StatusMessageType.Warning),
            ("E201", "FM full", StatusMessageType.Error),
            ("E299", "FM general error", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),

            ("E201", "FM overflow", StatusMessageType.Error),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, string.Empty, StatusMessageType.Reserved),
            (null, "VAT groups are set", StatusMessageType.Info),
            (null, "Device S/N and FM S/N are set", StatusMessageType.Info),
            (null, "FM ready", StatusMessageType.Info),
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
                // Byte 3 is special in Daisy, it contains error code, from bit 0 to bit 6
                // bit 7 is reserved, so we will clear it from errorCode.
                if (i == 3)
                {
                    byte errorCode = (byte)(status[i] & 0b01111111);
                    if (errorCode > 0)
                    {
                        deviceStatus.AddError("E999", $"Error code: {errorCode}, see Daisy Manual");
                    }
                    continue;
                }
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
