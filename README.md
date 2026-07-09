# Modern E-Commerce API

[![.NET Build & Test](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-8.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![JWT](https://img.shields.io/badge/JWT-Authentication-000000?style=flat-square&logo=jsonwebtokens&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-8.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![Swagger](https://img.shields.io/badge/Swagger-UI-85EA2D?style=flat-square&logo=swagger&logoColor=black)

A production-quality **RESTful Web API / Web Service** built with **ASP.NET Core 8 (.Net Core)**, featuring JWT authentication, BCrypt password hashing, Entity Framework Core (EF Core), LINQ queries, OOP design patterns, and full Swagger/OpenAPI documentation.

> **Security:** Product mutations (POST/PUT/DELETE) require JWT Bearer token. GET endpoints are public.

---

## Features

- [x] **JWT Authentication** â€” Register â†’ Login â†’ Bearer token
- [x] **BCrypt Password Hashing** â€” Secure credential storage
- [x] **Role-Based Access** â€” Protected product mutations (`[Authorize]`)
- [x] **Full CRUD API** â€” Products with proper HTTP status codes
- [x] **AI Product Recommendations** â€” Heuristic scoring based on simulated user behavior
- [x] **Swagger UI** â€” Interactive API docs with JWT auth button
- [x] **EF Core + SQL Server** â€” Code-first database with seed data
- [x] **Auto Seed Data** â€” 2 demo users + 12 products on first run
- [x] **CI/CD** â€” GitHub Actions build & test pipeline

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 8 Web API |
| Auth | JWT Bearer + BCrypt.Net |
| ORM | Entity Framework Core 8 |
| Database | SQLite |
| Docs | Swagger / OpenAPI 3 |
| CI/CD | GitHub Actions |

---

## Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- No DB installation required (SQLite auto-creates)

### Run Locally

```bash
git clone https://github.com/Adiie0001/Ecommerce-API.git
cd Ecommerce-API
dotnet restore
dotnet run
```

Open browser: **http://localhost:5xxx/swagger**

> Database is auto-created and seeded on first run â€” no migrations needed!

---

## Demo Credentials

| Username | Email | Password | Role |
|----------|-------|----------|------|
| admin | admin@test.com | Admin@123 | Admin |
| testuser | user@test.com | User@123 | User |

---

## API Endpoints

### Auth

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/register` | [ ] Public | Register a new user |
| POST | `/api/auth/login` | [ ] Public | Login & get JWT token |
| GET | `/api/auth/protected` | [x] Required | Test protected route |

### Products

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/product` | [ ] Public | Get all products |
| GET | `/api/product/{id}` | [ ] Public | Get product by ID |
| POST | `/api/product` | [x] Required | Create product |
| PUT | `/api/product/{id}` | [x] Required | Update product |
| DELETE | `/api/product/{id}` | [x] Required | Delete product |
| GET | `/api/product/recommend` | [ ] Public | AI Product Recommendations |

---

## Auth Flow

```
1. POST /api/auth/register â†’ { username, email, passwordHash }
2. POST /api/auth/login â†’ Returns: { token: "eyJ..." }
3. Copy token
4. In Swagger: Click Authorize â†’ Enter: Bearer eyJ...
5. Now POST/PUT/DELETE product endpoints work!
```

---

## Project Structure

```
Ecommerce-API/
â”œâ”€â”€ Controllers/
â”‚ â”œâ”€â”€ AuthController.cs # Register, Login, Protected route
â”‚ â””â”€â”€ ProductController.cs # Full CRUD with [Authorize]
â”œâ”€â”€ Data/
â”‚ â”œâ”€â”€ AppDbContext.cs # EF Core DbContext
â”‚ â””â”€â”€ SeedData.cs # Demo data: 2 users + 12 products
â”œâ”€â”€ Models/
â”‚ â”œâ”€â”€ Product.cs # Product entity
â”‚ â””â”€â”€ User.cs # User entity with Role
â”œâ”€â”€ Services/
â”‚ â”œâ”€â”€ JwtService.cs # JWT token generation
â”‚ â”œâ”€â”€ PasswordHasher.cs # BCrypt wrapper
â”‚ â””â”€â”€ SimulatedAiRecommendationService.cs # AI heuristic scoring
â”œâ”€â”€ EcommerceAPI.Tests/
â”‚ â””â”€â”€ ProductControllerTests.cs # xUnit: 5 tests (all passing)
â”œâ”€â”€ Program.cs # App config: JWT + Swagger + SeedData
â””â”€â”€ appsettings.json # Connection strings & JWT config
```

---

## Roadmap

- [ ] Shopping cart & order management
- [ ] Admin vs Customer role claims in JWT
- [ ] Product categories & search/filter
- [ ] Pagination on GET /product
- [ ] Refresh token mechanism
- [ ] Rate limiting middleware
- [ ] Docker Compose support
- [ ] Payment gateway integration

---

## Author

**Aditya Maisuriya** - Full Stack Developer | ASP.NET Core | .NET | C# | AI Integration
- Portfolio: [adityamaisuriya.pages.dev](https://adityamaisuriya.pages.dev)
- LinkedIn: [linkedin.com/in/aditya-maisuriya-39a540202](https://linkedin.com/in/aditya-maisuriya-39a540202)
- Email: adiiimaisuriya94@gmail.com
