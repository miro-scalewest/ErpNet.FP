using System;

namespace ErpNet.FP.Core
{
    using System.Collections.Generic;
    using Configuration;

    /// <summary>
    /// Represents the capabilities of a connected fiscal printer.
    /// </summary>
    public interface IFiscalPrinter
    {
        /// <summary>
        /// Gets information about the connected device.
        /// </summary>
        /// <returns>Device information.</returns>
        DeviceInfo DeviceInfo { get; }

        /// <summary>
        /// Saving driver name for state cache use
        /// </summary>
        string driverName { get; }

        public ServiceOptions ServiceOptions { get; }

        public IDictionary<string, string> Options { get; }

        public IChannel Channel { get; }

        /// <summary>
        /// Checks whether the device is currently ready to accept commands.
        /// </summary>
        DeviceStatusWithDateTime CheckStatus();

        /// <summary>
        /// Gets the amount of cash available
        /// </summary>
        DeviceStatusWithCashAmount Cash(Credentials credentials);

        /// <summary>
        /// Sets the device date and time
        /// </summary>
        DeviceStatus SetDateTime(CurrentDateTime currentDateTime);

        /// <summary>
        /// Prints the specified receipt.
        /// </summary>
        /// <param name="receipt">The receipt to print.</param>
        (ReceiptInfo, DeviceStatus) PrintReceipt(Receipt receipt);

        /// <summary>
        /// Validates the receipt object
        /// </summary>
        /// <param name="receipt"></param>
        /// <returns></returns>
        DeviceStatus ValidateReceipt(Receipt receipt);

        /// <summary>
        /// Prints the specified reversal receipt.
        /// </summary>
        /// <param name="reversalReceipt">The reversal receipt.</param>
        /// <returns></returns>
        (ReceiptInfo, DeviceStatus) PrintReversalReceipt(ReversalReceipt reversalReceipt);

        /// <summary>
        /// Prints a non-fiscal receipt.
        /// </summary>
        /// <param name="nonFiscalReceipt"></param>
        /// <returns></returns>
        DeviceStatus PrintNonFiscalReceipt(NonFiscalReceipt nonFiscalReceipt);

        /// <summary>
        /// Validates the reversal receipt object
        /// </summary>
        /// <param name="reversalReceipt"></param>
        /// <returns></returns>
        DeviceStatus ValidateReversalReceipt(ReversalReceipt reversalReceipt);

        /// <summary>
        /// Prints a deposit money note.
        /// </summary>
        /// <param name="amount">The deposited amount. Should be greater than 0.</param>
        DeviceStatus PrintMoneyDeposit(TransferAmount transferAmount);

        /// <summary>
        /// Prints a withdraw money note.
        /// </summary>
        /// <param name="amount">The withdrawn amount. Should be greater than 0.</param>
        DeviceStatus PrintMoneyWithdraw(TransferAmount transferAmount);

        /// <summary>
        /// Validates transfer amount object
        /// </summary>
        /// <param name="transferAmount"></param>
        /// <returns></returns>
        DeviceStatus ValidateTransferAmount(TransferAmount transferAmount);

        /// <summary>
        /// Prints a zreport.
        /// </summary>
        DeviceStatus PrintZReport(Credentials credentials);

        /// <summary>
        /// Prints a xreport.
        /// </summary>
        DeviceStatus PrintXReport(Credentials credentials);

        /// <summary>
        /// Prints monthly report.
        /// </summary>
        /// <param name="fiscalReport"></param>
        /// <returns></returns>
        DeviceStatus PrintFiscalReport(FiscalReport fiscalReport);

        /// <summary>
        /// Prints duplicate of the last fiscal receipt.
        /// </summary>
        DeviceStatus PrintDuplicate(Credentials credentials);

        /// <summary>
        /// Raw request.
        /// </summary>
        DeviceStatusWithRawResponse RawRequest(RequestFrame requestFrame);

        /// <summary>
        /// Tries to fix the erroneous state of the device to the normal - ready for printing state
        /// </summary>
        DeviceStatusWithDateTime Reset(Credentials credentials);

        void SetDeadLine(DateTime deadLine);

        /// <summary>
        /// Validates input for invoice range setting
        /// </summary>
        /// <param name="invoiceRange"></param>
        /// <returns></returns>
        DeviceStatus ValidateInvoiceRange(InvoiceRange invoiceRange);

        /// <summary>
        /// Tries to set invoice range in fiscal device
        /// </summary>
        /// <param name="invoiceRange"></param>
        /// <returns></returns>
        DeviceStatus SetInvoiceRange(InvoiceRange invoiceRange);

        /// <summary>
        /// Retrieves invoice range from fiscal device
        /// </summary>
        /// <returns></returns>
        DeviceStatusWithInvoiceRange GetInvoiceRange();
    }
}