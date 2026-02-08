using Backend.Controllers;
using Microsoft.OpenApi.Models;

namespace Backend;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ”казываем порт 5000 и прослушивание всех интерфейсов
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(5000);
        });

        // Add services to the container.
        builder.Services.AddSingleton<ChatsManager>();
        builder.Services.AddControllers();

        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Backend", Version = "v1" });
        });
        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Backend v1");
                c.RoutePrefix = string.Empty; // ”казывает, что Swagger UI должен быть в корне (http://localhost:5000/)
            });
        }
        // Configure the HTTP request pipeline.
        app.UseWebSockets();
        app.UseHttpsRedirection();

        //app.UseAuthorization();


        app.MapControllers();

        app.Run();
    }
}
