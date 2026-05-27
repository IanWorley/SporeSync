using Scalar.AspNetCore;
using SftpSync.Business;
using SftpSync.Infrastructure;
using SftpSync.Web;

var builder = WebApplication.CreateBuilder(args);

var testcontainerDatabase = await TestcontainerDatabase.StartIfEnabledAsync(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.RegisterBusinessLogic();
builder.Services.RegisterInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapSwagger("/openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.WithTitle("SftpSync API"));
}

app.UseHttpsRedirection();

app.MapControllers();

try
{
    await app.RunAsync();
}
finally
{
    if (testcontainerDatabase is not null)
    {
        await testcontainerDatabase.DisposeAsync();
    }
}
