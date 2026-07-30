using ToolExcel.Api.Data;
using ToolExcel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Services / DI ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ToolExcel API", Version = "v1" });
});

// Cau hinh ket noi Oracle (nhieu nguon theo connId - mo phong PT_CONNECTION)
builder.Services.Configure<OracleConnectionOptions>(
    builder.Configuration.GetSection("Oracle"));

builder.Services.AddSingleton<IOracleConnectionFactory, OracleConnectionFactory>();
builder.Services.AddScoped<IBieuMauConfigService, BieuMauConfigService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();
builder.Services.AddScoped<IExcelImportService, ExcelImportService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

// Health check don gian: smoke-test khi deploy (khong dung DB)
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
