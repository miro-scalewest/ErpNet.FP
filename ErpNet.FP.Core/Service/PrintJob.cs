namespace ErpNet.FP.Core.Service
{
    using System;
    using Serilog;

    public enum PrintJobAction
    {
        None,
        Cash,
        RawRequest,
        Receipt,
        ReversalReceipt,
        NonFiscalReceipt,
        Withdraw,
        Deposit,
        XReport,
        ZReport,
        FiscalReport,
        SetDateTime,
        Duplicate,
        Reset,
        SetInvoiceNumberRange,
        GetInvoiceNumberRange,
    }

    public delegate object Run(object document);

    public class PrintJob
    {
        public const int DefaultTimeout = 29000; // 29 seconds

        public DateTime Enqueued = DateTime.Now;
        public DateTime? Started = null;
        public DateTime? Finished = null;
        public DateTime DeadLine = DateTime.MaxValue;

        public PrintJobAction Action = PrintJobAction.None;
        public IFiscalPrinter? Printer;
        public TaskStatus TaskStatus = TaskStatus.Enqueued;  // On creation status must be "Enqueued"!  "Unknown" is for task NOT FOUND in queue.
        public object? Document;
        public object? Result;
        public string? TaskId;
        public int AsyncTimeout = DefaultTimeout;

        public int Timeout { 
            get => timeout; 
            set {
                timeout = value;
                DeadLine = timeout <= 0 ? DateTime.MaxValue : Enqueued.AddMilliseconds(timeout);
            } 
        }

        public void Run()
        {
            if (Printer == null) return;
            
            Started = DateTime.Now;

            if (DeadLine <= Started)
            {                
                Finished = DateTime.Now;
                TaskStatus = TaskStatus.Finished;
                var deviceStatus = new DeviceStatus();
                deviceStatus.AddError("E999", "User timeout occured while sending the request");
                Result = deviceStatus;
                return;
            }

            TaskStatus = TaskStatus.Running;

            Printer.SetDeadLine(DeadLine);
            try
            {
                switch (Action)
                {
                    case PrintJobAction.Cash:
                        Result = Printer.Cash((Credentials)(Document ?? new Credentials()));
                        break;
                    case PrintJobAction.RawRequest:
                        if (Document != null)
                        {
                            Result = Printer.RawRequest((RequestFrame)Document);
                        }
                        break;
                    case PrintJobAction.Receipt:
                        if (Document != null)
                        {
                            var receipt = (Receipt)Document;
                            var validateStatus = Printer.ValidateReceipt(receipt);
                            if (validateStatus.Ok)
                            {
                                var (info, status) = Printer.PrintReceipt(receipt);
                                Result = new DeviceStatusWithReceiptInfo(status, info);
                                if (status.Ok && receipt.PrintDuplicate)
                                {
                                    var duplicateResult = Printer.PrintDuplicate(receipt);
                                    Log.Information("Duplicate " + (duplicateResult.Ok ? "printed" : "failed"));
                                }
                            }
                            else
                            {
                                Result = validateStatus;
                            }
                        }
                        break;
                    case PrintJobAction.ReversalReceipt:
                        if (Document != null)
                        {
                            var reversalReceipt = (ReversalReceipt)Document;
                            var validateStatus = Printer.ValidateReversalReceipt(reversalReceipt);
                            if (validateStatus.Ok)
                            {
                                var (info, status) = Printer.PrintReversalReceipt(reversalReceipt);
                                Result = new DeviceStatusWithReceiptInfo(status, info);
                            }
                            else
                            {
                                Result = validateStatus;
                            }
                        };
                        break;
                    case PrintJobAction.NonFiscalReceipt:
                        if (Document != null)
                        {
                            var nonFiscalReceipt = (NonFiscalReceipt) Document;
                            Result = Printer.PrintNonFiscalReceipt(nonFiscalReceipt);
                        }
                        break;
                    case PrintJobAction.FiscalReport:
                        if (Document != null)
                        {
                            var reportData = (FiscalReport) Document;
                            Result = Printer.PrintFiscalReport(reportData);
                        }
                        break;
                    case PrintJobAction.Withdraw:
                        if (Document != null)
                        {
                            var transferAmount = (TransferAmount)Document;
                            var validateStatus = Printer.ValidateTransferAmount(transferAmount);
                            if (validateStatus.Ok)
                            {
                                Result = Printer.PrintMoneyWithdraw(transferAmount);
                            }
                            else
                            {
                                Result = validateStatus;
                            }
                        }
                        break;
                    case PrintJobAction.Deposit:
                        if (Document != null)
                        {
                            var transferAmount = (TransferAmount)Document;
                            var validateStatus = Printer.ValidateTransferAmount(transferAmount);
                            if (validateStatus.Ok)
                            {
                                Result = Printer.PrintMoneyDeposit((TransferAmount)Document);
                            }
                            else
                            {
                                Result = validateStatus;
                            }
                        }
                        break;
                    case PrintJobAction.XReport:
                        Result = Printer.PrintXReport((Credentials)(Document ?? new Credentials()));
                        break;
                    case PrintJobAction.ZReport:
                        Result = Printer.PrintZReport((Credentials)(Document ?? new Credentials()));
                        break;
                    case PrintJobAction.SetDateTime:
                        if (Document != null)
                        {
                            var dateTimeDocument = (CurrentDateTime)Document;
                            if (dateTimeDocument.DeviceDateTime == System.DateTime.MinValue)
                            {
                                dateTimeDocument.DeviceDateTime = DateTime.Now;
                            }
                            Result = Printer.SetDateTime(dateTimeDocument);
                        }
                        break;
                    case PrintJobAction.Duplicate:
                        Result = Printer.PrintDuplicate((Credentials)(Document ?? new Credentials()));
                        break;
                    case PrintJobAction.Reset:
                        Result = Printer.Reset((Credentials)(Document ?? new Credentials()));
                        break;
                    case PrintJobAction.SetInvoiceNumberRange:
                        if (Document != null)
                        {
                            var invoiceRange = (InvoiceRange)Document;
                            var validateStatus = Printer.ValidateInvoiceRange(invoiceRange);
                            if (validateStatus.Ok)
                            {
                                Result = Printer.SetInvoiceRange(invoiceRange);
                            }
                            else
                            {
                                Result = validateStatus;
                            }
                        }
                        break;
                    case PrintJobAction.GetInvoiceNumberRange:
                        Result = Printer.GetInvoiceRange();
                        break;
                    default:
                        break;
                }
            }
            finally
            {
                Finished = DateTime.Now;
                TaskStatus = TaskStatus.Finished;
                Printer.SetDeadLine(DateTime.MaxValue);
            }
        }

        private int timeout = 0;
    }
}
