using System.ComponentModel.DataAnnotations;

namespace bank.Models
{
    public class TransferRequestDto
    {
        [Required]
        [StringLength(10, MinimumLength = 10, ErrorMessage = "Source account number must be exactly 10 digits.")]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Source account number must contain only digits.")]
        public string FromAccount { get; set; } = string.Empty;

        public string? ToAccount { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public string? Description { get; set; }

        // External transfer fields
        public string TransactionType { get; set; } = "Internal"; // Internal, ACH, FedNow, RTP
        public string? TargetRoutingNumber { get; set; }
        public string? ExternalAccountNumber { get; set; }
    }
}
