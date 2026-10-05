using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace NaryadAi.Services;

public class WorkOrderPhotoService(
    AppDbContext db,
    IWebHostEnvironment environment)
{
    private const long MaxUploadSize = 8 * 1024 * 1024;

    public async Task<WorkOrderPhoto> SaveAsync(
        int orderId,
        int authorId,
        string type,
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        if (type is not ("До" or "После"))
            throw new InvalidOperationException("Неизвестный тип фотографии.");

        if (file.Size <= 0 || file.Size > MaxUploadSize)
            throw new InvalidOperationException(
                "Размер исходной фотографии должен быть до 8 МБ.");

        var order = await db.WorkOrders
            .FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Наряд не найден.");

        await using var input = file.OpenReadStream(
            MaxUploadSize,
            cancellationToken);

        using var image = await Image.LoadAsync(input, cancellationToken);

        image.Mutate(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(1600, 1600)
            });
        });

        await using var output = new MemoryStream();

        await image.SaveAsJpegAsync(
            output,
            new JpegEncoder
            {
                Quality = 75
            },
            cancellationToken);

        var bytes = output.ToArray();

        var hash = Convert.ToHexString(
            SHA256.HashData(bytes));

        if (await db.WorkOrderPhotos.AnyAsync(
                p => p.Sha256 == hash,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Это изображение уже прикрепляли к другому наряду.");
        }

        var directory = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "workorder-photos");

        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}.jpg";

        var absolutePath = Path.Combine(
            directory,
            fileName);

        var relativePath = Path.Combine(
                "App_Data",
                "workorder-photos",
                fileName)
            .Replace('\\', '/');

        await File.WriteAllBytesAsync(
            absolutePath,
            bytes,
            cancellationToken);

        try
        {
            var photo = new WorkOrderPhoto
            {
                WorkOrderId = orderId,
                AuthorId = authorId,
                Type = type,
                FilePath = relativePath,
                Sha256 = hash,
                CapturedAt = DateTime.UtcNow
            };

            db.WorkOrderPhotos.Add(photo);

            if (type == "После")
                order.PhotoAfterPath = relativePath;
            else
                order.PhotoBeforePath = relativePath;

            await db.SaveChangesAsync(cancellationToken);

            return photo;
        }
        catch
        {
            try
            {
                File.Delete(absolutePath);
            }
            catch
            {
            }
            throw;
        }
    }
}