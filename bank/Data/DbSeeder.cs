using bank.Models;
using Microsoft.EntityFrameworkCore;

namespace bank.Data
{
    public static class DbSeeder
    {
        public static void Seed(BankDbContext context)
        {
            var jan = context.Users.FirstOrDefault(u => u.Username == "jan");
            if (jan == null)
            {
                jan = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Jan",
                    LastName = "Kowalski",
                    Username = "jan",
                    Email = "jan@example.com",
                    SocialSecurityNumber = "123456789",
                    PhoneNumber = "111222333",
                    Address = "New York, 5th Ave",
                    Role = "Customer",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };
                context.Users.Add(jan);

                context.Accounts.Add(new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = jan.Id,
                    AccountNumber = "1111111111",
                    RoutingNumber = "123456789",
                    Balance = 5000.00m,
                    Currency = "USD",
                    AccountType = "Standard"
                });
            }
            else if (string.IsNullOrEmpty(jan.Email) || string.IsNullOrEmpty(jan.Role))
            {
                jan.Email = "jan@example.com";
                jan.Role = "Customer";
            }

            var anna = context.Users.FirstOrDefault(u => u.Username == "anna");
            if (anna == null)
            {
                anna = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Anna",
                    LastName = "Nowak",
                    Username = "anna",
                    Email = "anna@example.com",
                    SocialSecurityNumber = "987654321",
                    PhoneNumber = "333222111",
                    Address = "Los Angeles, Sunset Blvd",
                    Role = "Customer",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };
                context.Users.Add(anna);

                context.Accounts.Add(new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = anna.Id,
                    AccountNumber = "2222222222",
                    RoutingNumber = "123456789",
                    Balance = 1500.50m,
                    Currency = "USD",
                    AccountType = "Standard"
                });
            }
            else if (string.IsNullOrEmpty(anna.Email) || string.IsNullOrEmpty(anna.Role))
            {
                anna.Email = "anna@example.com";
                anna.Role = "Customer";
            }

            var admin = context.Users.FirstOrDefault(u => u.Username == "admin");
            if (admin == null)
            {
                admin = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Adam",
                    LastName = "Pracownik",
                    Username = "admin",
                    Email = "admin@bank.com",
                    SocialSecurityNumber = "000000000",
                    PhoneNumber = "999888777",
                    Address = "Bank HQ",
                    Role = "Employee",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123")
                };
                context.Users.Add(admin);
            }

            var swiftUser = context.Users.FirstOrDefault(u => u.Username == "swift_system");
            if (swiftUser == null)
            {
                swiftUser = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "SWIFT",
                    LastName = "Correspondent",
                    Username = "swift_system",
                    Email = "swift@bank.com",
                    SocialSecurityNumber = "SWIFT-SYS",
                    PhoneNumber = "SWIFT-SYS",
                    Address = "SWIFT Network",
                    Role = "System",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("swift123")
                };
                context.Users.Add(swiftUser);


                string[] correspondentBics = { "PLBKPL01XXX", "PLBKPL02XXX", "UKBKGB01XXX", "UKBKGB02XXX", "DEBKDE01XXX", "EUBKFR01XXX", "BANKDEXX", "BANKDEXXXXX" };
                foreach (var bic in correspondentBics)
                {
                    context.Accounts.Add(new Account
                    {
                        Id = Guid.NewGuid(),
                        UserId = swiftUser.Id,
                        AccountNumber = $"CORR-{bic}",
                        RoutingNumber = "SWIFT",
                        Balance = 1000000.00m,
                        Currency = "USD",
                        AccountType = "Correspondent"
                    });
                }
            }

            context.SaveChanges();
        }
    }
}
