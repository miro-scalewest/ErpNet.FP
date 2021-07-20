﻿namespace ErpNet.FP.Core.Drivers.BgDatecs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;
    using System.Text;


    /// <summary>
    /// Fiscal printer using the ISL implementation of Datecs Bulgaria.
    /// </summary>
    /// <seealso cref="BgIslFiscalPrinter" />
    public partial class BgDatecsCIslFiscalPrinter : BgIslFiscalPrinter
    {
        protected const byte
            CommandPrintBriefReportForDate = 0x4F,
            CommandPrintDetailedReportForDate = 0x5E,
            CommandDatecsOpenReversalReceipt = 0x2e,
            CommandGetInvoiceRange = 0x42,
            CommandSetInvoiceRange = 0x42;

        public override (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword,
            bool isInvoice = false)
        {
            string header;

            if (string.IsNullOrEmpty(uniqueSaleNumber))
            {
                header = string.Join(",",
                    new string[]
                    {
                        String.IsNullOrEmpty(operatorId)
                            ? Options.ValueOrDefault("Operator.ID", "1")
                            : operatorId,
                        String.IsNullOrEmpty(operatorId)
                            ? Options.ValueOrDefault("Operator.Password", "1")
                                .WithMaxLength(Info.OperatorPasswordMaxLength)
                            : operatorPassword,
                        "1"
                    });
            }
            else
            {
                header = string.Join(",",
                    new string[]
                    {
                        String.IsNullOrEmpty(operatorId)
                            ? Options.ValueOrDefault("Operator.ID", "1")
                            : operatorId,
                        String.IsNullOrEmpty(operatorId)
                            ? Options.ValueOrDefault("Operator.Password", "1")
                                .WithMaxLength(Info.OperatorPasswordMaxLength)
                            : operatorPassword,
                        "1"
                    });
            }

            if (isInvoice)
            {
                header += ",I";
            }

            return Request(CommandOpenFiscalReceipt, header);
        }

        public override (string, DeviceStatus) GetTaxIdentificationNumber()
        {
            var (response, deviceStatus) = Request(CommandGetTaxIdentificationNumber);
            var commaFields = response.Split(',');
            if (commaFields.Length == 2)
            {
                return (commaFields[0].Trim(), deviceStatus);
            }
            return (string.Empty, deviceStatus);
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

        public override DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange)
        {
            var (_, result) =  Request(CommandSetInvoiceRange, invoiceRange.Start + "," + invoiceRange.End);
            if (!result.Ok)
            {
                result.AddError("E499", "An error occurred while setting invoice range");
            }

            return result;
        }

        public override DeviceStatusWithInvoiceRange GetInvoiceRange()
        {
            var (data, output) = Request(CommandGetInvoiceRange);
            var result = new DeviceStatusWithInvoiceRange(output);

            if (!output.Ok)
            {
                result.AddError("E499", "An error occurred while reading invoice range");
                return result;
            }

            try
            {
                var split = data.Split(",");
                result.Start = BigInteger.Parse(split[0]);
                result.End = BigInteger.Parse(split[1]);
            }
            catch (Exception e)
            {
                result.AddInfo($"Error occured while parsing the invoice range");
                result.AddError("E409", e.Message);
            }

            return result;
        }

        public override (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0,
            decimal priceModifierValue = 0,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int ItemCode = 999)
        // Protocol [<L1>][<Lf><L2>]<Tab><TaxCd><[Sign]Price>[*<Qwan>][,Perc|;Abs]
        {
            var itemData = new StringBuilder();
            if (department <= 0)
            {
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
                        ? ',' : ';')
                    .Append((
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.DiscountAmount
                        ? -priceModifierValue : priceModifierValue).ToString("F2", CultureInfo.InvariantCulture));
            }
            return Request(CommandFiscalReceiptSale, itemData.ToString());
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
            // Protocol: <OpCode>,<OpPwd>,<NSale>,<TillNmb>,<DocType>,<DocNumber>,<DocDateTime>,< FMNumber >[,< Invoice >,< InvNumber >,< Reason >]
            var header = string.Join(",",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "1").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    uniqueSaleNumber,
                    "1",
                    GetReversalReasonText(reason),
                    receiptNumber,
                    receiptDateTime.ToString("ddMMyyHHmmss", CultureInfo.InvariantCulture),
                    fiscalMemorySerialNumber
                });

            try
            {
                if (invoiceNumber.Length > 0 && int.Parse(invoiceNumber) > 0)
                {
                    header += ",I," + invoiceNumber + "," + reason; // TODO: better handle reason?
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return Request(CommandDatecsOpenReversalReceipt, header);
        }

        public override IDictionary<PaymentType, string> GetPaymentTypeMappings()
        {
            var paymentTypeMappings = new Dictionary<PaymentType, string> {
                { PaymentType.Cash,          "P" },
                { PaymentType.Coupons,       "J" },
                { PaymentType.ExtCoupons,    "I" },
                { PaymentType.Card,          "C" },
                { PaymentType.Reserved1,     "D" }
            };
            ServiceOptions.RemapPaymentTypes(Info.SerialNumber, paymentTypeMappings);
            return paymentTypeMappings;
        }

        // 6 Bytes x 8 bits
        protected static readonly (string?, string, StatusMessageType)[] StatusBitsStrings = new (string?, string, StatusMessageType)[] {
            // 0.0 # Получените данни имат синктактична грешка.
            ("E401", "Syntax error in the received data", StatusMessageType.Error),
            // 0.1 # Кодът на получената команда е невалиден.
            ("E402", "Invalid command code received", StatusMessageType.Error),
            // 0.2 Не е сверен часовника.
            ("E103", "The clock is not set", StatusMessageType.Error),
            // 0.3 Не е свързан клиентски дисплей.
            (null, "No customer display is connected", StatusMessageType.Info),
            // 0.4 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 0.5 Обща грешка - това е OR на всички грешки, маркирани с ‘#’.
            ("E199", "General error", StatusMessageType.Error),
            // 0.6 Резервиран – винаги е 0.
            ("E302", "The printer cover is open", StatusMessageType.Error),
            // 0.7 Резервиран – винаги е 1.
            (null, string.Empty, StatusMessageType.Reserved),
            
            // 1.0 При изпълнение на командата се е получило препълване на някои полета от сумите. 
            ("E403", "The command resulted in an overflow of some amount fields", StatusMessageType.Error),
            // 1.1 # Изпълнението на командата не е позволено в текущия фискален режим.
            ("E404", "The command is not allowed in the current fiscal mode", StatusMessageType.Error),
            // 1.2 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 1.3 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 1.4 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 1.5 Има неизпратени документи за повече от настроеното време за предупреждение
            (null, "There are unsent documents for more than the set warning time", StatusMessageType.Info),
            // 1.6 Вграденият данъчен терминал не отговаря.
            ("E599", "The built-in tax terminal is not responding", StatusMessageType.Error),
            // 1.7 Резервиран – винаги е 1.
            (null, string.Empty, StatusMessageType.Reserved),

            // 2.0 # Свършила е хартията. Ако се вдигне този флаг по време на команда, свързана с печат, то командата е отхвърлена и
            // не е променила състоянието на ФУ.
            ("E301", "No paper", StatusMessageType.Error),
            // 2.1 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 2.2 Край на КЛЕН (по-малко от 1 MB от КЛЕН свободни).
            ("E206", "End of the EJ", StatusMessageType.Error),
            // 2.3 Отворен е фискален бон.
            (null, "A fiscal receipt is open", StatusMessageType.Info),
            // 2.4 Близък край на КЛЕН (по-малко от 10 MB от КЛЕН свободни).
            ("W202", "The end of the EJ is near", StatusMessageType.Warning),
            // 2.5 Отворен е служебен бон.
            (null, "A service receipt is open", StatusMessageType.Info),
            // 2.6 Не се използува.
            (null, string.Empty, StatusMessageType.Reserved),
            // 2.7 Резервиран – винаги е 1.
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
            
            // 4.0 * Грешка при запис във фискалната памет.
            ("E202", "Fiscal memory store error", StatusMessageType.Error),
            // 4.1 Зададен е ЕИК.
            (null, "BULSTAT UIC is set", StatusMessageType.Info),
            // 4.2 Зададени са индивидуален номер на ФУ и номер на ФП.
            (null, "Unique Printer ID and Fiscal Memory ID are set", StatusMessageType.Info),
            // 4.3 Има място за по-малко от 50 записа във ФП.
            ("W201", "There is space for less than 50 records remaining in the FP", StatusMessageType.Warning),
            // 4.4 * ФП е пълна.
            ("E201", "The fiscal memory is full", StatusMessageType.Error),
            // 4.5 OR на всички грешки, маркирани с ‘*’ от байтове 4 и 5.
            ("E299", "FM general error", StatusMessageType.Error),
            // 4.6 Не се използува.
            ("E304", "The printing head is overheated", StatusMessageType.Error),
            // 4.7 Резервиран – винаги е 1.
            (null, string.Empty, StatusMessageType.Reserved),

            // 5.0 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 5.1 ФП е форматирана.
            (null, "The fiscal memory is formatted", StatusMessageType.Info),
            // 5.2 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 5.3 ФУ е във фискален режим.
            (null, "The printer is in fiscal mode", StatusMessageType.Info),
            // 5.4 Зададени са поне веднъж данъчните ставки.
            (null, "The tax rates are set at least once", StatusMessageType.Info),
            // 5.5 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 5.6 Резервиран – винаги е 0.
            (null, string.Empty, StatusMessageType.Reserved),
            // 5.7 Резервиран – винаги е 1.
            (null, string.Empty, StatusMessageType.Reserved)
        };

        public override (string, DeviceStatus) PrintNonFiscalReceiptText(
            string text,
            bool bold = false,
            bool italic = false,
            bool underline = false,
            LineHeight height = LineHeight.OneLine
        )
        {
            // Protocol: <Height><tab>[<Data>]
            var headerData = string.Join("\t",
                height == LineHeight.OneLine ? 1 : 2,
                text.WithMaxLength(Info.CommentTextMaxLength)
            );

            return Request(CommandNonFiscalReceiptText, headerData.ToString());
        }

        public override (string, DeviceStatus) PrintReportForDate(DateTime startDate, DateTime endDate, ReportType type)
        {
            var startDateString = startDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var endDateString = endDate.ToString("ddMMyy", CultureInfo.InvariantCulture);
            var headerData = string.Join(",", startDateString, endDateString);
            Console.WriteLine("Datecs C: " + headerData);

            return Request(
                type == ReportType.Brief
                    ? CommandPrintBriefReportForDate
                    : CommandPrintDetailedReportForDate,
                headerData
            );
        }

        public override (string, DeviceStatus) SetInvoice(Invoice invoice)
        {
            var clientData = string.Join("\t",
                invoice.UID,
                ((int)invoice.Type).ToString(),
                invoice.SellerName,
                invoice.ReceiverName,
                invoice.BuyerName,
                invoice.VatNumber,
                invoice.ClientAddress
            );

            return Request(
                CommandSetClientInfo,
                clientData
            );
        }

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
