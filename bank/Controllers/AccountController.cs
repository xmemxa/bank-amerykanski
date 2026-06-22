using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using bank.Data;
using bank.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace bank.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly BankDbContext _context;

        public AccountController(BankDbContext context)
        {
            _context = context;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            var userAccounts = await _context.Accounts
                .Where(a => a.UserId == userId)
                .ToListAsync();

            var userAccountIds = userAccounts.Select(a => a.Id).ToList();

            var juniorAccounts = await _context.Accounts
                .Where(a => a.ParentAccountId != null && userAccountIds.Contains(a.ParentAccountId.Value))
                .ToListAsync();

            var allAccounts = userAccounts.Concat(juniorAccounts).ToList();

            var accounts = allAccounts.Select(a => new {
                    a.Id,
                    a.AccountType,
                    a.AccountNumber,
                    a.RoutingNumber,
                    a.Balance,
                    a.Currency,
                    IsJunior = a.AccountType == "Junior"
                })
                .ToList();

            var accountIds = allAccounts.Select(a => a.Id).ToList();

            var recentTransactions = await _context.Transactions
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Where(t => (t.FromAccountId != null && accountIds.Contains(t.FromAccountId.Value)) || 
                            (t.ToAccountId != null && accountIds.Contains(t.ToAccountId.Value)))
                .OrderByDescending(t => t.Timestamp)
                .Take(5)
                .Select(t => new {
                    t.Id,
                    t.Timestamp,
                    t.Description,
                    t.TransactionType,
                    t.Status,
                    t.Amount,
                    t.Currency,
                    IsCredit = t.ToAccountId != null && accountIds.Contains(t.ToAccountId.Value)
                })
                .ToListAsync();

            var pendingJuniorTransactions = await _context.Transactions
                .Include(t => t.FromAccount)
                .Where(t => t.Status == "Pending_Approval" && t.FromAccount != null && t.FromAccount.ParentAccountId != null && accountIds.Contains(t.FromAccount.ParentAccountId.Value))
                .Select(t => new {
                    t.Id,
                    t.Amount,
                    t.Currency,
                    t.Description,
                    t.Timestamp,
                    JuniorAccountName = t.FromAccount.AccountNumber
                })
                .ToListAsync();

            return Ok(new {
                FirstName = user.FirstName,
                Accounts = accounts,
                RecentTransactions = recentTransactions,
                PendingJuniorTransactions = pendingJuniorTransactions
            });
        }
    }
}
