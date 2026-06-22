using System;
using System.Collections.Generic;

namespace bank.Models
{
    public class Account
    {
        public Guid Id { get; set; }
        
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
        
        public string RoutingNumber { get; set; } = "123456789"; // Routing Number banku
        public string AccountNumber { get; set; } = string.Empty;
        public decimal Balance { get; set; } = 0;
        public decimal HeldBalance { get; set; } = 0; // Zablokowane środki (authorization hold)
        public string Currency { get; set; } = "USD"; // Domyślna waluta
        
        public string AccountType { get; set; } = "Standard"; // np. "Standard", "Junior"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Konto Junior jest podpięte do konta rodzica
        public Guid? ParentAccountId { get; set; }
        public Account? ParentAccount { get; set; }
        
        // Konta podrzędne (np. konta Junior)
        public ICollection<Account> ChildAccounts { get; set; } = new List<Account>();

        // Navigation properties dla transakcji
        public ICollection<Transaction> SentTransactions { get; set; } = new List<Transaction>();
        public ICollection<Transaction> ReceivedTransactions { get; set; } = new List<Transaction>();
        
        // Navigation property dla kart p,atniczych
        public ICollection<Card> Cards { get; set; } = new List<Card>();
    }
}
