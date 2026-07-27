using System.Text.Json.Serialization;
using HanieTo.Api.Data;
using HanieTo.Api.Publishing;
using HanieTo.Api.Publishing.Publishers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=HanieTo.db"));

builder.Services.AddHttpClient();
builder.Services.AddScoped<IChannelPublisher, InstagramPublisher>();
builder.Services.AddScoped<IChannelPublisher, TwitterPublisher>();
builder.Services.AddScoped<IChannelPublisher, TelegramPublisher>();
builder.Services.AddScoped<ChannelPublisherResolver>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Skipped in Development: when accessed through an ngrok tunnel (for testing
// webhook-style calls like Instagram fetching a hosted photo), a forced redirect
// to https://localhost:... would send external callers to an address only this
// machine can reach.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
