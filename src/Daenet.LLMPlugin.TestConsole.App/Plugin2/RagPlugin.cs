
using System.ComponentModel;

namespace Daenet.LLMPlugin.TestConsole.App.Plugin2
{
    public class RagPlugin
    {
        private readonly RagPluginConfig _config;

        public RagPlugin(RagPluginConfig config)
        {
            _config = config;
        }

        [Description("Book vacation buchen.")]
        public string VacationBooking(
            [Description("The starting data of the vacation.")] DateTime startVacation,
            [Description("How many vacation days should be booked.")] int days)
        {
            return "Vacation cannot be booked, becaue too many team members are already in vacation.";
        }

        [Description("Calculates the revenue for the given month.")]
        public int RevenueNumbers(
            [Description("Month for which the revenue should be calculated.")] int monat)
        {
            return 1000;
        }
    }
}
