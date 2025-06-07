using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin1
{
    public class MyPlugin
    {
        private readonly MyPluginConfig _cfg;

        public MyPlugin(MyPluginConfig cfg)
        {
            _cfg = cfg;
        }


        [KernelFunction]
        [Description("Provides the list of names of processes")]
        public string GetProcessInfo([Description("If set in true, it provides the detaile process information.")] bool provideDetailedInfo = false)
        {
            StringBuilder sb = new StringBuilder();

            foreach (Process proc in Process.GetProcesses().ToList())
            {
                sb.AppendLine($"Proce name: {proc.ProcessName}");
                if (provideDetailedInfo)
                {
                    sb.AppendLine($"Proces ID: {proc.Id}"); ;
                    sb.AppendLine($"Proces working set: {proc.WorkingSet64}");
                    //sb.Append($"Working directory or local path: {proc.StartInfo.WorkingDirectory}");
                    sb.AppendLine("---------------------------------------------");
                }
            }

            return sb.ToString();
        }

        [KernelFunction]
        [Description("Provides the count of running processes.")]
        public int GetProcessCount()
        {
            return Process.GetProcesses().ToList().Count;
        }

        [KernelFunction]
        [Description("Kills the process with the given name or process id.")]
        public string KillProcess(
            [Description("The name of the process to be killed.")] string? processName,
            [Description("The ID of the process to be killed.")] int? processId)
        {
            try
            {
                var processes = Process.GetProcesses().ToList();

                if (processId.HasValue && processId > 0)
                {
                    var targetProcess = processes.FirstOrDefault(p => p.Id == processId);
                    if (targetProcess != null)
                    {
                        targetProcess.Kill();
                    }
                }
                else if (processName != null)
                {
                    var targetProcess = processes.FirstOrDefault(p => p.ProcessName == processName);
                    if (targetProcess != null)
                        targetProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                return $"The process cannot be terminated. Error: {ex.Message}";
            }

            return "Process has been killed!";
        }

        [KernelFunction]
        [Description("Gets the local IP addres of the machine.")]

        public string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "No network adapters with an IPv4 address in the system!";
        }

        [KernelFunction]
        [Description("Gets the name of the local machine.")]
        public string GetMachineName()
        {
            return Environment.MachineName;
        }

        [KernelFunction]
        [Description("Provide the information about writing daenet plugins.")]
        public string GetInfoAboutPlugins()
        {
            return "The plugin is a class that contains methods that are decorated with the KernelFunction attribute. The plugin class can have a constructor that accepts the configuration object as a parameter. The configuration object is a class that contains the properties that are used to configure the plugin. The configuration object is passed to the plugin class constructor. Dont forget to dring a glass of water.";
        }

        [KernelFunction]
        [Description("Performs the search operation related to Invoices.")]
        public string SearchInvoices(
            [Description("The user's ask or intent")] string intent,
         [Description("The customer number or Kundennummer. If not specified use let the system use the default one.")] string? customerNumber,
         [Description("True if the user's intent is assotiated to the single document or invoice.")] bool? isSingleInvoiceRequested,
        // [Description("Specifies if the user's intent requires ascending or descending sorting direction.")] SortOrder? sortingDirection,
         [Description("Invoices created after the given time.")] DateTime? fromTime,
         [Description("Invoices created before the given time.")] DateTime? toTime,
         //[Description("The status of the invoice.")] InvoiceState? status,
         [Description("The docment/invoice number of the invoice.")] string? documentNo,
         [Description("The docment/invoice description of the invoice.")] string? documentDescription,
         [Description("The category of the document or invoice")] string? documentCategory,
         [Description("The date of the document or invoice")] DateTime? documentDate,
         [Description("The end date or expiration date of the document or invoice")] DateTime? documentEndDate,
         [Description("The invoice number from the externl system.")] string? externalDocumetNumber,
         [Description("The email of the orderer.")] string? ordererEmail,
         [Description("The phone of the orderer.")] string? ordererPhone,
        [Description("The currency of the document or invoice.")] string? documentCurrency,
        [Description("The net value.")] Decimal? valueNet,
       // [Description("The operator for net value. User can ask value greather, equal or less than.")] QueryOperator? valueNetOperator,
        [Description("The gross value.")] Decimal? valueGross,
       // [Description("The operator for gross value. User can ask value greather, equal or less than.")] QueryOperator? valueGrossOperator,
        [Description("The description of the document or ivoice.")] string? description,
        [Description("The name of the orderer of the document or invoice")] string? orderer,
        //[Description("The status of the document or invoice.")] string? documentStatus,
        [Description("The position of the document or invoice.")] decimal? docPosition,
        [Description("The document o invoice line.")] string? docLine,
        [Description("The article number of the document or invoice.")] string? articleNumber,
        [Description("The article description of the document or invoice.")] string? articleDescription,
        [Description("The ordered quantity.")] decimal? quantity,
        [Description("The unit of measure.")] string? unit,//todo fehlt
        [Description("The serial number of the ordered article or product.")] string? serialNo,
        [Description("Number at the customer address.")] string? addressNoCustomer,
        [Description("The recipient of the order.")] string? recipient,
        [Description("The delivery street of the order.")] string? deliveryStreet,
        [Description("The delivery city of the order.")] string? deliveryCity,
        [Description("The delivery postal code of the order.")] string? deliveryPostalCode,
        [Description("The delivery country of the order.")] string? deliveryCountry,

        [Description("Number of the goods address.")] string? addressNoGoods, //TODO 
        [Description("The recipient of the goods.")] string? goodsAddressRecipient,
        [Description("The street address of the goods.")] string? goodsAddressStreet,
        [Description("The city of the goods.")] string? goodsAddressCity,
        [Description("The postal/zip code of the goods.")] string? goodsAddressPostalCode,
        [Description("The country of the goods.")] string? goodsAddressCountry,

        [Description("The house number in the street of the invoice.")] string? invoiceStreetNumber,
        [Description("The street of the invoice.")] string? invoiceStreet,
        [Description("The city of the invoice.")] string? invoiceCity,
        [Description("The postal code of the invoice.")] string? invoicePostalCode,
        [Description("The country of the invoice.")] string? invoiceCountry,
        [Description("How many invoices should be retrieved.")] int? records,

        [Description("If the user asks to calculate the net average value.")] bool? isGrossAverage,
        [Description("If the user asks to calculate the net maximum, highest or the top value.")] bool? isGrossMax,
        [Description("If the user asks to calculate the net minimum, or lowest value.")] bool? isGrossMin,
        [Description("If the user asks to calculate the net sum value.")] bool? isGrossSum,

        [Description("If the user asks to calculate the net average value.")] bool? isNetAverage,
        [Description("If the user asks to calculate the net maximum, highest or the top value.")] bool? isNetMax,
        [Description("If the user asks to calculate the net minimum, or lowest value.")] bool? isNetMin,
        [Description("If the user asks to calculate the net sum value.")] bool? isNetSum,

        [Description("Grouped BY Fields requested by one of agreggated operations.")] string[]? groupBy

            )
        {
            return $"From:{fromTime}, To:{toTime}";
        }
    }
}
