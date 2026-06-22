using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using bank.Models;
using bank.Data;

namespace bank.Services
{
    public enum AmlAction
    {
        Approve,
        Review,
        Block
    }

    public class AmlResult
    {
        public int Score { get; set; }
        public AmlAction Action { get; set; }
        public string Details { get; set; } = "";
    }

    public class AmlService
    {
        private readonly BankDbContext _context;

        public AmlService(BankDbContext context)
        {
            _context = context;
        }

        public async Task<AmlResult> EvaluateTransactionAsync(Guid sourceAccountId, TransferRequestDto request)
        {
            int score = 0;
            var details = new System.Collections.Generic.List<string>();

            // 1. Amount rules
            if (request.Amount > 50000)
            {
                score += 50;
                details.Add("Amount > 50,000 (+50)");
            }
            else if (request.Amount > 10000)
            {
                score += 30;
                details.Add("Amount > 10,000 (+30)");
            }

            // 2. Transaction Type (Foreign transfer)
            if (request.TransactionType == "SWIFT")
            {
                score += 20;
                details.Add("Foreign transfer (SWIFT) (+20)");
            }

            // 3. Number of transfers today
            var today = DateTime.UtcNow.Date;
            var transfersToday = await _context.Transactions
                .CountAsync(t => t.FromAccountId == sourceAccountId && t.Timestamp >= today);
            
            if (transfersToday >= 10)
            {
                score += 20;
                details.Add("More than 10 transfers today (+20)");
            }

            // 4. New receiver check
            if (!string.IsNullOrEmpty(request.ToAccount))
            {
                var previousTransfers = await _context.Transactions
                    .Where(t => t.FromAccountId == sourceAccountId)
                    .Select(t => t.TransferRequestJson)
                    .ToListAsync();
                    
                bool isNew = !previousTransfers.Any(json => json != null && json.Contains(request.ToAccount));
                    
                if (isNew)
                {
                    score += 10;
                    details.Add("New receiver (+10)");
                }
            }

            // 5. Keywords in description
            if (!string.IsNullOrEmpty(request.Description))
            {
                var lowerDesc = request.Description.ToLower();
                if (lowerDesc.Contains("aml") || lowerDesc.Contains("krypto") || lowerDesc.Contains("crypto") || lowerDesc.Contains("laund"))
                {
                    score += 30;
                    details.Add("Suspicious keywords (+30)");
                }
            }

            // Evaluate action
            AmlAction action;
            if (score >= 80) action = AmlAction.Block;
            else if (score >= 40) action = AmlAction.Review;
            else action = AmlAction.Approve;

            return new AmlResult
            {
                Score = score,
                Action = action,
                Details = string.Join(", ", details)
            };
        }
    }
}
