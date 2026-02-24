using RestaurantPerformanceApi.Services;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();


builder.Services.AddScoped<DataService>();
builder.Services.AddSingleton<MLService>();
builder.Services.AddScoped<MetricsService>();

builder.Services.AddHealthChecks();           

var app = builder.Build();


app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();


app.MapHealthChecks("/health");               

app.MapControllers();

app.Run();