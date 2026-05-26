using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using bank.Data;
using bank.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using bank.Services;

namespace bank.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly BankDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;

        public AuthController(BankDbContext context, IConfiguration configuration, IEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        [Authorize(Roles = "Employee")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto request)
        {
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest("A user with this login ID already exists.");

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest("A user with this email address already exists.");

            if (await _context.Users.AnyAsync(u => u.SocialSecurityNumber == request.SocialSecurityNumber))
                return BadRequest("A user with this SSN already exists.");

            if (await _context.Users.AnyAsync(u => u.PhoneNumber == request.PhoneNumber))
                return BadRequest("A user with this phone number already exists.");

            if (request.Password.Length < 8 || 
                !request.Password.Any(char.IsUpper) || 
                !request.Password.Any(char.IsLower) || 
                !request.Password.Any(char.IsDigit) || 
                !request.Password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                return BadRequest("Password must be at least 8 characters long, and contain at least one uppercase letter, one lowercase letter, one digit, and one special character.");
            }
            
            using var transaction = await _context.Database.BeginTransactionAsync();
            string uniqueAccountNumber = await GenerateUniqueUSAccountNumber();

            try {
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Username = request.Username,
                    Email = request.Email,
                    SocialSecurityNumber = request.SocialSecurityNumber,
                    Address = request.Address,
                    PhoneNumber = request.PhoneNumber,
                    Role = "Customer",
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
                    Balance = request.InitialDeposit,
                    Currency = request.Currency,
                    AccountType = request.AccountType,
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
            
            var random = new Random();
            var code = random.Next(100000, 999999).ToString();

            user.TwoFactorCode = code;
            user.TwoFactorExpiry = DateTime.UtcNow.AddMinutes(5);
            await _context.SaveChangesAsync();

            string emailBody = $@"
                <h2>Login Verification</h2>
                <p>Your verification code is: <strong>{code}</strong></p>
                <p>This code will expire in 5 minutes.</p>
            ";
            
            string targetEmail = string.IsNullOrEmpty(user.Email) ? "test@example.com" : user.Email;

            await _emailService.SendEmailAsync(targetEmail, "Your Login Code - American Bank", emailBody);

            return Ok(new { 
                Message = "Valid credentials. A 2FA code has been sent to your email.",
                Requires2FA = true
            });
        }

        [HttpPost("verify-2fa")]
        public async Task<IActionResult> Verify2FA([FromBody] Verify2FaDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
                return Unauthorized("Invalid username.");

            if (user.TwoFactorCode != request.Code || user.TwoFactorExpiry < DateTime.UtcNow)
                return Unauthorized("Invalid or expired 2FA code.");

            user.TwoFactorCode = null;
            user.TwoFactorExpiry = null;
            await _context.SaveChangesAsync();

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
            var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            
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
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("id", user.Id.ToString()),
                new Claim("role", user.Role),
                new Claim("firstName", user.FirstName),
                new Claim("lastName", user.LastName),
                new Claim("email", string.IsNullOrEmpty(user.Email) ? "" : user.Email)
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
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(20, MinimumLength = 4)]
        public string Username { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^\d{3}-\d{2}-\d{4}$")]
        public string SocialSecurityNumber { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(200)]
        public string Address { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.Phone]
        public string PhoneNumber { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string Password { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string AccountType { get; set; } = "Checking";

        [System.ComponentModel.DataAnnotations.Required]
        public string Currency { get; set; } = "USD";

        public decimal InitialDeposit { get; set; }
    }

    public class UserLoginDto { 
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class Verify2FaDto {
        public string Username { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}