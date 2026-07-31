using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ToolExcel.Api.Data;
using ToolExcel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Nap secret rieng (mat khau DB, JWT key) - file nay .gitignore, khong len repo.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---- Services / DI ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ToolExcel API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhap: Bearer {token}"
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Cau hinh ket noi Oracle (nhieu nguon theo connId) + auth + jwt
builder.Services.Configure<OracleConnectionOptions>(builder.Configuration.GetSection("Oracle"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddSingleton<IOracleConnectionFactory, OracleConnectionFactory>();
builder.Services.AddScoped<IBieuMauConfigService, BieuMauConfigService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();
builder.Services.AddScoped<IUserAuthService, UserAuthService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IUserScopeService, UserScopeService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// ---- Xac thuc JWT Bearer ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // giu nguyen claim 'sub' va 'roles'
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "sub",
            RoleClaimType = "roles",
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // 401 tron: khong tra header WWW-Authenticate de browser khong bung popup dang nhap.
        o.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                return ctx.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
            }
        };
    });

// Mac dinh: MOI endpoint deu can dang nhap (tru khi [AllowAnonymous]).
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Frontend React da build (npm run build -> wwwroot). Static file la middleware chay truoc
// routing nen KHONG bi FallbackPolicy doi token — dung vay, vi man dang nhap phai tai duoc
// khi chua co token. Du lieu van an toan: moi endpoint /api/* deu can Bearer.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check: theo yeu cau bao ve toan bo -> cung can token.
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// wwwroot la OUTPUT BUILD cua frontend (.gitignore) nen ban clone moi se khong co.
// Bao ro thay vi tra 404 trang tron — nguoi deploy hay quen buoc npm run build.
var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
if (File.Exists(indexPath))
{
    // SPA: duong dan la (vd /users khi F5) tra ve index.html cho React tu dinh tuyen.
    // AllowAnonymous vi day la endpoint (khac static file o tren) nen se bi FallbackPolicy chan.
    app.MapFallbackToFile("index.html").AllowAnonymous();
}
else
{
    app.MapFallback(() => Results.Text(
        "Chua build frontend. Chay: cd frontend && npm ci && npm run build\n" +
        "API van dung binh thuong: /swagger (Development) va /api/*.\n",
        "text/plain; charset=utf-8")).AllowAnonymous();

    app.Logger.LogWarning(
        "Khong thay {Path} — frontend chua build. Chay 'cd frontend && npm ci && npm run build'.",
        indexPath);
}

app.Run();
