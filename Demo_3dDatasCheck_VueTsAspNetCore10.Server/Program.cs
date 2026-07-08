using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<BuildingAbnormalDetectionOptions>(
    builder.Configuration.GetSection(BuildingAbnormalDetectionOptions.SectionName));
builder.Services.Configure<CityDoctor2Options>(
    builder.Configuration.GetSection(CityDoctor2Options.SectionName));

builder.Services.AddHttpClient(); // 注入 HttpClient 服務
builder.Services.AddSingleton<ICoordinateTransformService, CoordinateTransformService>();
builder.Services.AddSingleton<ICityDoctor2ProcessRunner, CityDoctor2ProcessRunner>();
builder.Services.AddSingleton<ICityDoctor2Adapter, CityDoctor2Adapter>();
builder.Services.AddSingleton<IXmlImportPreprocessor, XmlImportPreprocessor>();
builder.Services.AddSingleton<BuildingProcessorService>(); // 注入建物資料處理服務（單例）

var app = builder.Build();

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
