using awardco.Data;
using awardco.Model;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<EmployeeRepository>(); // lets the controller receive a repository
builder.Services.AddScoped<TransactionRepository>();

var app = builder.Build();

Database.CreateTables(); // Create the database tables if they don't exist

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    DatabaseSeeder.Seed(); // Fill an empty database with fake data for testing
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
