using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Infrastructure.Persistence;

class Program
{
    static async Task Main()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=AdaptiveEnglishLearningDb;Uid=root;Pwd=12345;",
            new MySqlServerVersion(new Version(8, 0, 30))
        );

        using var dbContext = new AppDbContext(optionsBuilder.Options);
        
        Console.WriteLine("=== Lessons ===");
        var lessons = await dbContext.Lessons.ToListAsync();
        foreach (var l in lessons)
        {
            Console.WriteLine($"ID: {l.Id}, Title: {l.Title}, Level: {l.Level}");
        }
    }
}
