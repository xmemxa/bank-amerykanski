using System;

namespace bank.Models
{
    public class Transaction
    {
        public Guid Id { get; set; }
        
        public Guid? FromAccountId { get; set; }
        public Account? FromAccount { get; set; }

        public Guid? ToAccountId { get; set; }
        public Account? ToAccount { get; set; }

        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD"; // Waluta transakcji
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public string Status { get; set; } = "Completed"; // np. "Pending", "Completed", "Failed", "Pending_Approval" (dla junior)
        
        // Pola opcjonalne pod przelewy zewnętrzne
        public string? ExternalSourceAccount { get; set; }
        public string? ExternalDestinationAccount { get; set; }
        public string? TransactionType { get; set; } // np. "Internal", "ACH", "FedNow", "SWIFT"
        
        // FedNow/RTP correlation fields
        public string? EndToEndId { get; set; } // ISO 20022 EndToEndId for pacs.008 <-> pacs.002 matching
        public string? ExternalStatus { get; set; } // Status from external system: ACCP, RJCT, PDNG, ACTC
    }
}
