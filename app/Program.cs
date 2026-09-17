using Microsoft.EntityFrameworkCore;
using TradeApi.Data;
using TradeApi.Interfaces;
using TradeApi.Services;
using TradeApi.Configuration;
using TradeApi.Middleware;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using Azure.Identity;
using Azure.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // Required for endpoint discovery
builder.Services.AddSwaggerGen(); 

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITradeService, TradeService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.Configure<SqsOptions>(
    builder.Configuration.GetSection("Sqs"));

builder.Services.AddAWSService<IAmazonSQS>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();   // Serves the generated JSON document
    app.UseSwaggerUI(); // Serves the physical interactive UI web page
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();


app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
