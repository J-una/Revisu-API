using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Revisu.Data;
using Revisu.Infrastructure;
using Revisu.Infrastructure.Services;
using Revisu.Infrastructure.Services.Biblioteca;
using Revisu.Infrastructure.Services.ImportacaoTmdb;
using Revisu.Infrastructure.Services.Quiz;
using Revisu.Recommendation;

var builder = WebApplication.CreateBuilder(args);

// Configurações externas
builder.Services.Configure<TmdbSettings>(builder.Configuration.GetSection("TMDbSettings"));
builder.Services.Configure<RottenSettings>(builder.Configuration.GetSection("RottenSettings"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RottenSettings>>().Value);
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return config.GetSection("TMDbSettings").Get<TmdbSettings>();
});

// Conexão com o banco de dados
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Substituímos AddDbContext por AddDbContextFactory
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Serviços da aplicação
builder.Services.AddScoped<TmdbImportService>();
builder.Services.AddScoped<TmdImportSeriesService>();
builder.Services.AddScoped<AdicionarNomeOriginalTmdbService>();
builder.Services.AddScoped<AtualizarObrasTmdb>();
builder.Services.AddScoped<TmdbAtualizarNotasService>();
builder.Services.AddScoped<RottenTomatoesImportService>();
//builder.Services.AddScoped<RecomendacaoHibridaService>();
builder.Services.AddScoped<AtualizarPopularidadeTmdbService>();
builder.Services.AddScoped<QuizService>();
builder.Services.AddScoped<AtualizarGenerosSeriesService>();
builder.Services.AddScoped<AtualizarElencoTmdbService>();
builder.Services.AddScoped<AtualizarElencoService>();
builder.Services.AddScoped<AtualizarGenerosSeriesTmdbService>();
builder.Services.AddScoped<RecomendacaoLentaService>();
builder.Services.AddScoped<BibliotecaService>();
builder.Services.AddScoped<RecomendacaoServiceOptimizado>();
builder.Services.AddScoped<RecomendacaoHibridaService>();
builder.Services.AddSingleton<GlobalRecommendationCache>();
builder.Services.AddScoped<RecomendacaoService>();
//builder.Services.AddHttpClient();
builder.Services.AddHttpClient("tmdb", (sp, client) =>
{
    var settings = sp.GetRequiredService<TmdbSettings>();

    client.BaseAddress = new Uri("https://api.themoviedb.org");
    client.Timeout = TimeSpan.FromMinutes(5); // <<< AUMENTE O TIMEOUT
    // Caso esteja usando Bearer (v4), descomente:
    // client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// MVC + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
