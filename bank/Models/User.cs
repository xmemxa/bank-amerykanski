using System;
using System.Collections.Generic;

namespace bank.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty; // User ID nadany przez pracownika
        public string SocialSecurityNumber { get; set; } = string.Empty; // SSN
        public string PhoneNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public ICollection<Account> Accounts { get; set; } = new List<Account>();
    }
}
