using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("app")]
public class AppController : ControllerBase
{
    private const string VERSION = "1.0.0";
    private const string FILE_NAME = "latest.7z";
    private const string FILE_PATH = "Storage/" + FILE_NAME;
    private const string CHECKSUM_PATH = FILE_PATH + ".checksum"; // Storage/latest.7z.checksum

    [HttpGet("version")]
    public IActionResult GetVersion() => Ok(VERSION);

    [HttpGet("latest")]
    public IActionResult DownloadLatest()
    {
        var fullPath = GetFullPath(FILE_PATH);

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Файл обновления не найден.");

        var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers.TryAdd("X-App-Version", VERSION);

        return File(fs, "application/x-7z-compressed", FILE_NAME, enableRangeProcessing: true);
    }

    [HttpGet("checksum")]
    public async Task<IActionResult> GetChecksum()
    {
        var fullPath = GetFullPath(FILE_PATH);
        var checksumPath = GetFullPath(CHECKSUM_PATH);

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Файл для вычисления хеша не найден.");

        // 1. Если файл хеша уже есть, просто читаем его
        if (System.IO.File.Exists(checksumPath))
        {
            var cachedHash = await System.IO.File.ReadAllTextAsync(checksumPath);
            return Ok(cachedHash);
        }

        // 2. Если хеша нет — вычисляем
        string hashString;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hashBytes = await sha256.ComputeHashAsync(fs);
            hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // 3. Сохраняем рядом с архивом для следующих запросов
        try
        {
            await System.IO.File.WriteAllTextAsync(checksumPath, hashString);
        }
        catch (IOException)
        {
            // Если возникла ошибка записи (например, права доступа), 
            // мы всё равно вернем результат клиенту
        }

        return Ok(hashString);
    }

    private string GetFullPath(string relativePath) =>
        Path.Combine(Directory.GetCurrentDirectory(), relativePath);
}
