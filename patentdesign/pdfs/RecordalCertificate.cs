using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

public class RecordalCertificate(Filling model, byte[] image, string applicationId) : IDocument
{
    private Filling model { get; set; } = model;
    //private string ApplicationId { get; set; } = applicationId;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(35);
            page.Content().Element(ComposeContent);
        });
    }

    static IContainer HeaderElement(IContainer container)
    {
        return container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .AlignMiddle()
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(1)
            .PaddingLeft(5);
    }

    static IContainer Block(IContainer container)
    {
        return container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .PaddingVertical(3)
            .PaddingLeft(5)
            .AlignLeft();
    }

    private void ComposeContent(IContainer container)
    {

        // Query the ApplicationHistory
        var history = model.ApplicationHistory
            .FirstOrDefault(x => x.id == applicationId);
        string recordalType = history.FieldToChange ?? null;

        container
            .PaddingVertical(5)
            .Column(column =>
            {
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().AlignCenter().Text("NIGERIA");
                //column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                //column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                //column.Item().AlignCenter().Text("PATENTS AND DESIGNS ACT CAP 344, LFN 1990");
                column.Item().Height(10);
                column.Item().AlignCenter().Text("Certificate of Recordal").FontFamily("Certificate").FontSize(30)
                    .Bold().FontColor(Colors.Green.Darken3);
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman)
                    .FontSize(14).Bold();
                column.Item().Height(5);
                column.Item().AlignCenter()
                    .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(25);
                //Table
                column.Item().Table(table =>
                {

                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Recordal Information")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Recordal Type:").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text(recordalType).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    

                });

                // Applicant Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Applicant Information")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });

                // Trademark Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Trademark Information")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Product Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TitleOfTradeMark).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });

                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Class of goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TrademarkClass).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Representation:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        if (model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device &&
                            model.Attachments.FirstOrDefault(e => e.name == "representation") != null &&
                            image.Length > 0)
                        {
                            var img = Image.FromBinaryData(image);
                            c.Item().Height(100).AlignCenter().Image(img).FitArea();
                        }
                        else
                            c.Item().Text(model.TrademarkLogo).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Description:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                column.Item().Height(40);
                // Notification Message 
                column.Item().AlignCenter().Text("THIS IS TO NOTIFY YOU THAT YOUR RECORDAL REQUEST HAS BEEN PROCESSED")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);

            });
    }

    // Helper method to extract all properties from JSON
    private string ExtractAllProperties(string json)
    {
        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            var extractedProperties = jsonElement.EnumerateObject()
                .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)) // Exclude "Id"
                .Select(property => $"{property.Name}: {property.Value}")
                .ToArray();
            return string.Join("\n", extractedProperties);
        }
        catch
        {
            return json; // Return the original string if it's not valid JSON
        }
    }
    private bool IsImage(string value)
    {
        // Check if the value is a URL or a base64-encoded image
        return Uri.IsWellFormedUriString(value, UriKind.Absolute) || value.StartsWith("data:image/");
    }

    private byte[]? FetchImage(string value)
    {
        try
        {
            if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
            {
                // Fetch the image from a URL
                using var httpClient = new HttpClient();
                return httpClient.GetByteArrayAsync(value).Result;
            }
            else if (value.StartsWith("data:image/"))
            {
                // Decode base64-encoded image
                var base64Data = value.Substring(value.IndexOf(",") + 1);
                return Convert.FromBase64String(base64Data);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch image: {ex.Message}");
        }

        return null;
    }

}