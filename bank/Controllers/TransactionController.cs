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
    public class TransactionController : ControllerBase
    {
        private readonly BankDbContext _context;

        public TransactionController(BankDbContext context)
        {
            _context = context;
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> InternalTransfer([FromBody] TransferRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (request.Amount <= 0)
                return BadRequest(new { Message = "Transfer amount must be greater than zero." });

            if (request.FromAccount == request.ToAccount)
                return BadRequest(new { Message = "Source and destination accounts cannot be the same." });

            var userIdStr = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized(new { Message = "Invalid user token." });

            var sourceAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.FromAccount);
            if (sourceAccount == null)
                return NotFound(new { Message = "Source account not found." });

            if (sourceAccount.UserId != userId)
                return StatusCode(403, new { Message = "You do not have permission to transfer from this account." });

            var destinationAccount = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccount);
            if (destinationAccount == null)
                return NotFound(new { Message = "Destination account does not exist in our bank." });

            if (sourceAccount.Balance < request.Amount)
                return BadRequest(new { Message = "Insufficient funds in the source account." });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                sourceAccount.Balance -= request.Amount;
                destinationAccount.Balance += request.Amount;

                var transactionRecord = new Transaction
                {
                    Id = Guid.NewGuid(),
                    FromAccountId = sourceAccount.Id,
                    ToAccountId = destinationAccount.Id,
                    Amount = request.Amount,
                    Currency = sourceAccount.Currency,
                    Description = !string.IsNullOrEmpty(request.Description) ? request.Description : "Internal Transfer",
                    Timestamp = DateTime.UtcNow,
                    Status = "Completed",
                    TransactionType = "Internal"
                };

                _context.Transactions.Add(transactionRecord);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    Message = "Transfer successful.",
                    NewBalance = sourceAccount.Balance,
                    Currency = sourceAccount.Currency,
                    TransactionId = transactionRecord.Id
                });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { Message = "An internal error occurred during transfer." });
            }
        }
    }
}
