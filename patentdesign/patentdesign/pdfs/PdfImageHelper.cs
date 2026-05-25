namespace patentdesign.pdfs;

internal static class PdfImageHelper
{
    /// <summary>
    /// Returns <c>true</c> when the supplied bytes can be decoded by QuestPDF as an image.
    /// Used to guard against invalid/corrupt image payloads that would otherwise throw
    /// "Cannot decode the provided image." during PDF generation.
    /// </summary>
    public static bool TryDecodeImage(byte[]? imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return false;

        try
        {
            _ = QuestPDF.Infrastructure.Image.FromBinaryData(imageData);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
