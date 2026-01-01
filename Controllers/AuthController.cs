using Microsoft.AspNetCore.Mvc;
using EcommerceAPI.Models;
using EcommerceAPI.Data;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using EcommerceAPI.Services;

namespace EcommerceAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly JwtService _jwtService;

        public AuthController(AppDbContext context, IConfiguration configuration, JwtService jwtService)
        {
            _context = context;
            _configuration = configuration;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<User>> Register(User user)
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(new { message = "User registered successfully!" });
        }

        [HttpPost("login")]
        public async Task<ActionResult<string>> Login(User user)
        {
            var dbUser = _context.Users.FirstOrDefault(u => u.Username == user.Username);
            if (dbUser == null || !BCrypt.Net.BCrypt.Verify(user.PasswordHash, dbUser.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            var token = _jwtService.GenerateToken(dbUser.Username);
            return Ok(new { token });
        }

        [Authorize]
        [HttpGet("protected")]
        public IActionResult ProtectedRoute()
        {
            return Ok(new { message = "You have accessed a protected route!" });
        }
    }
}
