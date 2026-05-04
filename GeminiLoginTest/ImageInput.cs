namespace GeminiLoginTest;

sealed record ImageInput(
    string Path,
    string MimeType,
    byte[] Bytes)
{
    const string TestImageFileName = "test_image.jpg";
    const string TestImageMimeType = "image/jpeg";

    public static async Task<ImageInput> LoadTestImage()
    {
        var imagePath = ResolveImagePath(TestImageFileName);
        if (imagePath is null)
        {
            throw new FileNotFoundException(
                $"Image file not found: {TestImageFileName}. Current directory: {Directory.GetCurrentDirectory()}",
                TestImageFileName);
        }

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        return new ImageInput(imagePath, TestImageMimeType, imageBytes);
    }

    static string? ResolveImagePath(string fileName)
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), fileName),
            System.IO.Path.Combine(AppContext.BaseDirectory, fileName),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", fileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
