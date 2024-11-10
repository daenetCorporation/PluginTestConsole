
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin2
{
    public class RagPlugin
    {
        private readonly RagPluginConfig _config;
        //private readonly ISearchApi _sApi;

        public RagPlugin(RagPluginConfig config/*, ISearchApi searchApi*/, Kernel kernel)
        {
            _config = config;
           // _sApi = searchApi;
        }

        [KernelFunction]
        [Description("Book vacation buchen.")]
        public string VacationBooking([Description("The starting data of the vacation.")]DateTime startVacation, 
            [Description("How many vacation days should be booked.")]int days)
        { 
            return "Vacation cannot be booked, becaue too many team members are already in vacation.";
        }

        [KernelFunction]
        [Description("Calculates the revenue for the given month.")]
        public int RevenueNumbers([Description("Month for which the revenue should be calculated.")]int monat)
        {
            return 1000;
        }


    }
}
