using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using bank.Data;
using bank.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace bank.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly BankDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(BankDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto request)
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest("A user with this username already exists.");
            
            using var transaction = await _context.Database.BeginTransactionAsync();
            string uniqueAccountNumber = await GenerateUniqueUSAccountNumber();

            try {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Username = request.Username,
                    SocialSecurityNumber = request.SocialSecurityNumber,
                    Address = request.Address,
                    PhoneNumber = request.PhoneNumber,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                
                var account = new Account
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    AccountNumber = uniqueAccountNumber,
                    RoutingNumber = "123456789", 
                    Balance = 0,
                    Currency = "USD",
                    AccountType = "Standard",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Accounts.Add(account);
                
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { 
                    Message = "Registration successful. Your checking account has been initialized.", 
                    RoutingNumber = account.RoutingNumber,
                    AccountNumber = account.AccountNumber,
                    InitialBalance = account.Balance,
                    Currency = account.Currency
                });
            }
            catch (Exception) {
                await transaction.RollbackAsync();
                return StatusCode(500, "An internal error occurred during registration. Please try again later.");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized("Invalid username or password.");
            
            var token = CreateToken(user);
            return Ok(new { 
                Token = token,
                TokenType = "Bearer",
                ExpiresIn = 86400 
            });
        }
        
        [Authorize] 
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            Console.WriteLine($"[SECURITY] User ID: {userId} requested logout.");

            return Ok(new 
            { 
                message = "Successfully logged out.",
                status = "Success"
            });
        }

        private async Task<string> GenerateUniqueUSAccountNumber()
        {
            string accountNumber = "";
            bool isUnique = false;

            while (!isUnique)
            {
                accountNumber = "";
                Random res = new Random();
                for (int i = 0; i < 10; i++)
                {
                    accountNumber += res.Next(0, 10).ToString();
                }
                
                bool exists = await _context.Accounts.AnyAsync(a => a.AccountNumber == accountNumber);
        
                if (!exists)
                {
                    isUnique = true;
                }
            }

            return accountNumber;
        }

        private string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                _configuration.GetSection("JWT_KEY").Value ?? "TajnyKluczBanku1234567890123456"));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(1),
                SigningCredentials = creds
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }
    }
    
    public class UserRegisterDto { 
        public string Username { get; set; } = string.Empty;
        public string SocialSecurityNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public class UserLoginDto { 
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}