using System;

namespace bank.Models
{
    public class KlikPendingAuthorization
    {
        public Guid Id { get; set; }
        public string TransactionId { get; set; } = string.Empty; // KLIK transaction_id
        public string UserId { get; set; } = string.Empty;        // Nasz wewnętrzny client ID (AccountId lub UserId)
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string MerchantName { get; set; } = string.Empty;
        public bool IsOnUs { get; set; }
        public DateTime ExpiryTime { get; set; }
        public string Status { get; set; } = "PENDING";           // "PENDING", "ACCEPTED", "REJECTED", "EXPIRED"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
