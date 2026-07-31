using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ToolExcel.Api.Data;
using ToolExcel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Nap secret rieng (mat khau DB, JWT key) - file nay .gitignore, khong len repo.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---- Services / DI ----
builder.Services.AddControllers();

// Nen bundle JS/CSS cua frontend: 154 kB -> ~50 kB. Chi khai cac MIME text-based;
// KHONG them .xlsx vi file Excel da la zip, nen lai chi ton CPU ma khong nho hon.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true; // noi bo, va cac file nay khong chua bi mat gi
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = new[]
    {
        "text/html", "text/css", "text/javascript", "text/plain",
        "application/javascript", "application/json", "image/svg+xml"
    };
});
builder.Services.Configure<BrotliCompressionProviderOptions>(
    o => o.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(
    o => o.Level = CompressionLevel.Optimal);
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

// Kiem ngay luc khoi dong. Neu de den luc tao SymmetricSecurityKey duoi day thi loi chi no
// o REQUEST DAU TIEN, dang 'IDX10703: key length is zero' — rat kho doan ra la thieu cau hinh.
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
{
    throw new InvalidOperationException(
        "Thieu hoac qua ngan 'Jwt:Key' (can >= 32 byte cho HS256). " +
        "Khai trong appsettings.Local.json canh appsettings.json, vd: " +
        "{ \"Jwt\": { \"Key\": \"<chuoi ngau nhien >= 32 ky tu>\" } }");
}

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

// Phai dat TRUOC UseStaticFiles, khong thi file tinh da gui xong roi moi den luot nen.
app.UseResponseCompression();

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
    WarnIfFrontendStale(app, indexPath);

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

/// <summary>
/// Canh bao khi ban build trong wwwroot CU HON ma nguon frontend — tuc la vua git pull ma quen
/// chay npm run build. Truong hop nay giao dien cu goi API moi va vo bang loi kho hieu kieu
/// "e.map is not a function"; da gap that. Chi chay khi con thay ..\frontend (may dev/build);
/// tren may chay that chi co wwwroot nen ham nay im lang.
/// </summary>
static void WarnIfFrontendStale(WebApplication app, string indexPath)
{
    try
    {
        var src = Path.Combine(app.Environment.ContentRootPath, "..", "frontend");
        if (!Directory.Exists(Path.Combine(src, "src")))
            return;

        // Chi quet frontend/src + vai file goc — KHONG quet node_modules.
        var files = Directory.EnumerateFiles(Path.Combine(src, "src"), "*", SearchOption.AllDirectories)
            .Concat(new[] { "index.html", "package.json", "vite.config.ts" }
                        .Select(f => Path.Combine(src, f))
                        .Where(File.Exists));

        var newestSource = files.Select(f => File.GetLastWriteTimeUtc(f)).DefaultIfEmpty().Max();
        var built = File.GetLastWriteTimeUtc(indexPath);

        if (newestSource > built)
        {
            app.Logger.LogWarning(
                "Giao dien trong wwwroot CU HON ma nguon frontend ({Built:u} < {Source:u}). " +
                "Chay 'cd frontend && npm run build' roi Ctrl+Shift+R, khong thi giao dien cu " +
                "goi API moi va bao loi kho hieu.",
                built, newestSource);
        }
    }
    catch (Exception ex)
    {
        // Kiem tra tien nghi thoi — khong duoc lam app khong khoi dong duoc.
        app.Logger.LogDebug(ex, "Khong kiem duoc do moi cua ban build frontend");
    }
}
