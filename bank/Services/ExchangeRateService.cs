using System;
using System.Collections.Generic;

namespace bank.Services
{
    public class ExchangeRateService
    {
        // Simple hardcoded rates against USD for academic project purposes
        // Key is the currency code (e.g. "EUR"), Value is how many USD it costs to buy 1 unit of that currency
        private readonly Dictionary<string, decimal> _ratesToUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            { "USD", 1.0m },
            { "EUR", 1.10m },
            { "GBP", 1.25m },
            { "PLN", 0.25m },
            { "CHF", 1.10m },
            { "CAD", 0.73m },
            { "JPY", 0.0065m }
        };

        public decimal GetRateToUsd(string currency)
        {
            if (_ratesToUsd.TryGetValue(currency, out var rate))
            {
                return rate;
            }
            return 1.0m; // Fallback to 1:1 if unknown
        }

        public (decimal convertedAmount, decimal fee, decimal totalDeduction) ConvertForCardTransaction(
            decimal originalAmount, 
            string originalCurrency, 
            string accountCurrency)
        {
            // First convert to USD
            var rateToUsd = GetRateToUsd(originalCurrency);
            var amountInUsd = originalAmount * rateToUsd;

            // Then convert to account currency
            var accountRateToUsd = GetRateToUsd(accountCurrency);
            var finalAmount = amountInUsd / accountRateToUsd;

            // Add 2% exchange fee if currencies differ
            decimal fee = 0;
            if (!originalCurrency.Equals(accountCurrency, StringComparison.OrdinalIgnoreCase))
            {
                fee = Math.Round(finalAmount * 0.02m, 2);
            }

            return (Math.Round(finalAmount, 2), fee, Math.Round(finalAmount + fee, 2));
        }
    }
}
