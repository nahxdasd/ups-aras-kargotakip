using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KargoTakip.Services;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// Register KargoService as a singleton
builder.Services.AddSingleton<KargoService>();

// Register KargoTimerService as a hosted service
builder.Services.AddHostedService<KargoTimerService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
