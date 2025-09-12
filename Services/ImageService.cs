using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace EventTicketingSystem.Services
{
    public class ImageService
    {
        private readonly IWebHostEnvironment _env;
        private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

        public ImageService(IWebHostEnvironment env) => _env = env;

        public (string imageWebPath, string thumbWebPath) SaveEventImage(Stream fileStream, string contentType, int eventId)
        {
            // basic content-type allowlist
            if (contentType is null || !(contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                                         contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Only JPEG and PNG images are allowed.");

            // ensure folders exist
            var uploadsRoot = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "events");
            Directory.CreateDirectory(uploadsRoot);

            // always normalize to JPEG on disk (smaller, consistent)
            var stamp = Guid.NewGuid().ToString("N");
            var baseFileName = $"event-{eventId}-{stamp}";
            var fullFile = Path.Combine(uploadsRoot, $"{baseFileName}.jpg");
            var thumbFile = Path.Combine(uploadsRoot, $"{baseFileName}-thumb.jpg");

            // load & validate image actually decodes
            using var image = Image.Load(fileStream);

            // MAIN: max width 1280, keep aspect
            using (var main = image.Clone(ctx =>
            {
                ctx.AutoOrient();
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(1280, 1280) // constrain by width/height
                });
            }))
            {
                main.Save(fullFile, new JpegEncoder { Quality = 80 });
            }

            // THUMB: max width 480
            fileStream.Position = 0; // not needed here since we used 'image' already, but safe
            using (var thumb = image.Clone(ctx =>
            {
                ctx.AutoOrient();
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(480, 480)
                });
            }))
            {
                thumb.Save(thumbFile, new JpegEncoder { Quality = 80 });
            }

            // return web paths (for <img src>)
            var imageWebPath = $"/uploads/events/{baseFileName}.jpg";
            var thumbWebPath = $"/uploads/events/{baseFileName}-thumb.jpg";
            return (imageWebPath, thumbWebPath);
        }

        public void DeleteIfExists(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return;
            var root = _env.WebRootPath ?? "wwwroot";
            var full = Path.Combine(root, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
                File.Delete(full);
        }
    }
}
