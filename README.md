# 🛒 Modern E-Commerce API

[![.NET Build & Test](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Adiie0001/Ecommerce-API/actions/workflows/dotnet.yml)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-8.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![JWT](https://img.shields.io/badge/JWT-Authentication-000000?style=flat-square&logo=jsonwebtokens&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-8.0-512BD4?style=flat-square&logo=.net&logoColor=white)
![Swagger](https://img.shields.io/badge/Swagger-UI-85EA2D?style=flat-square&logo=swagger&logoColor=black)

A production-quality **RESTful Web API** built with **ASP.NET Core 8**, featuring JWT authentication, BCrypt password hashing, Entity Framework Core, and full Swagger documentation.

> 🔐 **Security:** Product mutations (POST/PUT/DELETE) require JWT Bearer token. GET endpoints are public.

---

## 🚀 Features

- ✅ **JWT Authentication** — Register → Login → Bearer token
- ✅ **BCrypt Password Hashing** — Secure credential storage
- ✅ **Role-Based Access** — Protected product mutations (`[Authorize]`)
- ✅ **Full CRUD API** — Products with proper HTTP status codes
- ✅ **Swagger UI** — Interactive API docs with JWT auth button
- ✅ **EF Core + SQL Server** — Code-first database with seed data
- ✅ **Auto Seed Data** — 2 demo users + 12 products on first run
- ✅ **CI/CD** — GitHub Actions build & test pipeline

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 8 Web API |
| Auth | JWT Bearer + BCrypt.Net |
| ORM | Entity Framework Core 8 |
| Database | SQL Server / LocalDB |
| Docs | Swagger / OpenAPI 3 |
| CI/CD | GitHub Actions |

---

## ⚡ Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server or LocalDB (comes with Visual Studio)

### Run Locally

```bash
git clone https://github.com/Adiie0001/Ecommerce-API.git
cd Ecommerce-API
dotnet restore
dotnet run
```

Open browser: **http://localhost:5xxx/swagger**

> 💡 Database is auto-created and seeded on first run — no migrations needed!

---

## 🔑 Demo Credentials

| Username | Email | Password | Role |
|----------|-------|----------|------|
| admin | admin@test.com | Admin@123 | Admin |
| testuser | user@test.com | User@123 | User |

---

## 📋 API Endpoints

### Auth

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/register` | ❌ Public | Register a new user |
| POST | `/api/auth/login` | ❌ Public | Login & get JWT token |
| GET | `/api/auth/protected` | ✅ Required | Test protected route |

### Products

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/product` | ❌ Public | Get all products |
| GET | `/api/product/{id}` | ❌ Public | Get product by ID |
| POST | `/api/product` | ✅ Required | Create product |
| PUT | `/api/product/{id}` | ✅ Required | Update product |
| DELETE | `/api/product/{id}` | ✅ Required | Delete product |

---

## 🔐 Auth Flow

```
1. POST /api/auth/register  →  { username, email, passwordHash }
2. POST /api/auth/login     →  Returns: { token: "eyJ..." }
3. Copy token
4. In Swagger: Click 🔒 Authorize → Enter: Bearer eyJ...
5. Now POST/PUT/DELETE product endpoints work!
```

---

## 🗂️ Project Structure

```
Ecommerce-API/
├── Controllers/
│   ├── AuthController.cs       # Register, Login, Protected
│   └── ProductController.cs    # CRUD with [Authorize]
├── Data/
│   └── AppDbContext.cs         # EF Core DbContext
├── Models/
│   ├── Product.cs              # Product entity
│   └── User.cs                 # User entity
├── Services/
│   ├── JwtService.cs           # JWT token generation
│   └── PasswordHasher.cs       # BCrypt wrapper
├── SeedData.cs                 # Demo data seeder
├── Program.cs                  # App configuration
└── appsettings.json            # Connection strings & JWT config
```

---

## 🔮 Roadmap

- [ ] Shopping cart & order management
- [ ] Admin vs Customer role claims in JWT
- [ ] Product categories & search/filter
- [ ] Pagination on GET /product
- [ ] Refresh token mechanism
- [ ] Rate limiting middleware
- [ ] Docker Compose support
- [ ] Payment gateway integration

---

## 👨‍💻 Author

**Aditya Maisuriya** — ASP.NET Core Developer
- 🌐 Portfolio: [adityamaisuriya.pages.dev](https://adityamaisuriya.pages.dev)
- 💼 LinkedIn: [linkedin.com/in/aditya-maisuriya-39a540202](https://linkedin.com/in/aditya-maisuriya-39a540202)
- 📧 Email: adiiimaisuriya94@gmail.com
