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

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return Ok(new {
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.Address,
                user.SocialSecurityNumber,
                user.Email
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.PhoneNumber = dto.PhoneNumber;
            user.Address = dto.Address;

            await _context.SaveChangesAsync();
            return Ok(new { Message = "Profile updated successfully." });
        }

        [HttpPut("password")]
        public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordDto dto)
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { Message = "Invalid current password." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Password changed successfully." });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAccountDetails(Guid id)
        {
            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id);
            if (account == null) return NotFound();

            // Allow access if owner OR if it's a junior account of the owner
            if (account.UserId != userId && account.ParentAccountId != null)
            {
                var parentAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == account.ParentAccountId);
                if (parentAccount == null || parentAccount.UserId != userId)
                    return Unauthorized();
            }
            else if (account.UserId != userId)
            {
                return Unauthorized();
            }

            var transactions = await _context.Transactions
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Where(t => t.FromAccountId == id || t.ToAccountId == id)
                .OrderByDescending(t => t.Timestamp)
                .Select(t => new {
                    t.Id,
                    t.Timestamp,
                    t.Description,
                    t.TransactionType,
                    t.Status,
                    t.Amount,
                    t.Currency,
                    IsCredit = t.ToAccountId == id
                })
                .ToListAsync();

            return Ok(new {
                Account = new {
                    account.Id,
                    account.AccountType,
                    account.AccountNumber,
                    account.RoutingNumber,
                    account.Balance,
                    account.Currency,
                    account.CreatedAt
                },
                Transactions = transactions
            });
        }
    }

    public class UpdateProfileDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    public class UpdatePasswordDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
