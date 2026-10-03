using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public class WorkOrderPhotoService(AppDbContext db, IWebHostEnvironment environment)
{
    private const long MaxFileSize = 8 * 1024 * 1024;

    public async Task<WorkOrderPhoto> SaveAsync(int orderId, int authorId, string type, IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        if (type is not ("До" or "После")) throw new InvalidOperationException("Неизвестный тип фотографии.");
        if (file.Size is <= 0 or > MaxFileSize) throw new InvalidOperationException("Размер фотографии должен быть до 8 МБ.");

        var bytes = new byte[checked((int)file.Size)];
        await using (var stream = file.OpenReadStream(MaxFileSize, cancellationToken))
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        var extension = DetectImageExtension(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (await db.WorkOrderPhotos.AnyAsync(p => p.Sha256 == hash, cancellationToken))
            throw new InvalidOperationException("Это изображение уже прикрепляли к другому наряду.");

        var order = await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "workorder-photos");
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(directory, fileName);
        var relativePath = Path.Combine("App_Data", "workorder-photos", fileName).Replace('\\', '/');
        await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);

        try
        {
            var photo = new WorkOrderPhoto
            {
                WorkOrderId = orderId, AuthorId = authorId, Type = type,
                FilePath = relativePath, Sha256 = hash,
                CapturedAt = file.LastModified.UtcDateTime <= DateTime.UtcNow.AddMinutes(5)
                    ? file.LastModified.UtcDateTime : DateTime.UtcNow
            };
            db.WorkOrderPhotos.Add(photo);
            if (type == "После") order.PhotoAfterPath = relativePath;
            else order.PhotoBeforePath = relativePath;
            await db.SaveChangesAsync(cancellationToken);
            return photo;
        }
        catch
        {
            File.Delete(absolutePath);
            throw;
        }
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ".jpg";
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        throw new InvalidOperationException("Поддерживаются только изображения JPEG, PNG или WebP.");
    }
}
