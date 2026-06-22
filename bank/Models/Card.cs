using System;

namespace bank.Models
{
    public class Card
    {
        public Guid Id { get; set; }
        
        public Guid AccountId { get; set; }
        public Account Account { get; set; } = null!;

        public string CardToken { get; set; } = string.Empty;
        public string MaskedPan { get; set; } = string.Empty;
        
        public string Type { get; set; } = "VIRTUAL"; // VIRTUAL, PHYSICAL, PREPAID
        public string Status { get; set; } = "REQUESTED"; // REQUESTED, PRODUCING, SHIPPED, ACTIVE, BLOCKED
        
        public int ExpiryMonth { get; set; }
        public int ExpiryYear { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
