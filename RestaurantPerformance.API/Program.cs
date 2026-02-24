using RestaurantPerformanceApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();  // For third-party API calls
// Add your custom services here later (e.g., DataService, MLService)

builder.Services.AddScoped<DataService>();
builder.Services.AddSingleton<MLService>();  // Or Scoped if needed
builder.Services.AddScoped<MetricsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();