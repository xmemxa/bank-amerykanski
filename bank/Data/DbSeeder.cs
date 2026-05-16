using bank.Models;
using Microsoft.EntityFrameworkCore;

namespace bank.Data
{
    public static class DbSeeder
    {
        public static void Seed(BankDbContext context)
        {
            if (!context.Users.Any())
            {
                var user1 = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jan",
                    LastName = "Kowalski",
                    Username = "jan",
                    SocialSecurityNumber = "123456789",
                    PhoneNumber = "111222333",
                    Address = "New York, 5th Ave",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };

                var user2 = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Anna",
                    LastName = "Nowak",
                    Username = "anna",
                    SocialSecurityNumber = "987654321",
                    PhoneNumber = "333222111",
                    Address = "Los Angeles, Sunset Blvd",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };

                context.Users.AddRange(user1, user2);
                
                var account1 = new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = user1.Id,
                    AccountNumber = "1111111111",
                    RoutingNumber = "123456789",
                    Balance = 5000.00m,
                    Currency = "USD",
                    AccountType = "Standard"
                };

                var account2 = new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = user2.Id,
                    AccountNumber = "2222222222",
                    RoutingNumber = "123456789",
                    Balance = 1500.50m,
                    Currency = "USD",
                    AccountType = "Standard"
                };

                context.Accounts.AddRange(account1, account2);
                context.SaveChanges();
            }
        }
    }
}
