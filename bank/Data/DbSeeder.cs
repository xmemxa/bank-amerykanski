using bank.Models;
using Microsoft.EntityFrameworkCore;

namespace bank.Data
{
    public static class DbSeeder
    {
        public static void Seed(BankDbContext context)
        {
            var jan = context.Users.FirstOrDefault(u => u.Username == "jan");
            Account janAccount = null;
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

                janAccount = new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = jan.Id,
                    AccountNumber = "1111111111",
                    RoutingNumber = "123456789",
                    Balance = 5000.00m,
                    Currency = "USD",
                    AccountType = "Standard"
                };
                context.Accounts.Add(janAccount);
            }
            else if (string.IsNullOrEmpty(jan.Email) || string.IsNullOrEmpty(jan.Role))
            {
                jan.Email = "jan@example.com";
                jan.Role = "Customer";
            }
            
            if (janAccount == null) 
            {
                janAccount = context.Accounts.FirstOrDefault(a => a.UserId == jan.Id && a.AccountType == "Standard");
            }

            var jasiu = context.Users.FirstOrDefault(u => u.Username == "jasiu");
            if (jasiu == null && janAccount != null)
            {
                jasiu = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Janusz",
                    LastName = "Kowalski Jr",
                    Username = "jasiu",
                    Email = "jasiu@example.com",
                    SocialSecurityNumber = "123456789J",
                    PhoneNumber = "111222333J",
                    Address = "New York, 5th Ave",
                    Role = "Customer",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };
                context.Users.Add(jasiu);

                context.Accounts.Add(new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = jasiu.Id,
                    AccountNumber = "1111111112",
                    RoutingNumber = "123456789",
                    Balance = 100.00m,
                    Currency = "USD",
                    AccountType = "Junior",
                    ParentAccountId = janAccount.Id
                });
            }

            var anna = context.Users.FirstOrDefault(u => u.Username == "anna");
            Account annaAccount = null;
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

                annaAccount = new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = anna.Id,
                    AccountNumber = "2222222222",
                    RoutingNumber = "123456789",
                    Balance = 1500.50m,
                    Currency = "USD",
                    AccountType = "Standard"
                };
                context.Accounts.Add(annaAccount);
            }
            else if (string.IsNullOrEmpty(anna.Email) || string.IsNullOrEmpty(anna.Role))
            {
                anna.Email = "anna@example.com";
                anna.Role = "Customer";
            }
            
            if (annaAccount == null)
            {
                annaAccount = context.Accounts.FirstOrDefault(a => a.UserId == anna.Id && a.AccountType == "Standard");
            }

            var ania = context.Users.FirstOrDefault(u => u.Username == "ania");
            if (ania == null && annaAccount != null)
            {
                ania = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Ania",
                    LastName = "Nowak Jr",
                    Username = "ania",
                    Email = "ania@example.com",
                    SocialSecurityNumber = "987654321J",
                    PhoneNumber = "333222111J",
                    Address = "Los Angeles, Sunset Blvd",
                    Role = "Customer",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("haslo123")
                };
                context.Users.Add(ania);

                context.Accounts.Add(new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = ania.Id,
                    AccountNumber = "2222222223",
                    RoutingNumber = "123456789",
                    Balance = 50.00m,
                    Currency = "USD",
                    AccountType = "Junior",
                    ParentAccountId = annaAccount.Id
                });
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
