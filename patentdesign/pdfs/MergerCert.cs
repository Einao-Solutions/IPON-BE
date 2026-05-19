using CloudinaryDotNet.Actions;
using patentdesign.Dtos.Response;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs;

public class MergerCert(Filling model, string url, byte[]? imageData, string applicationId, Signatory signature) : IDocument
{
    private Filling model { get; set; } = model;
    private string url { get; set; } = url;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(35);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
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
        var regUser = model.RegisteredUsers?.FirstOrDefault(r => r.Id == applicationId);
        var postRegApp = model.PostRegApplications?.FirstOrDefault(a => a.Id == applicationId);
        var appHistory = model.ApplicationHistory?.FirstOrDefault(h => h.id == applicationId);
        var applicants = model.applicants.FirstOrDefault();
        container.PaddingVertical(5).Column(column =>
        {
            column.Item().Height(30);
            column.Item().Height(70).Row(row =>
            {
                row.RelativeItem().Width(40);
                row.RelativeItem().AlignCenter().Image("assets/logo.png").FitArea();
                row.RelativeItem().AlignRight().Text($"RTM: {model.RtmNumber ?? ""}");
            });

            column.Item().Height(10);
            column.Item().AlignCenter().Text($"NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(13).Bold();
            column.Item().Height(10);
            column.Item().AlignCenter().Text($"Certificate Of Merger")
                .FontFamily("Certificate").FontSize(30).Bold().FontColor(Colors.Green.Darken3);
            column.Item().Height(10);
            column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
            column.Item().Height(5);
            column.Item().AlignCenter()
                .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
            column.Item().Height(20);
            column.Item().Height(60).PaddingTop(10).Row(row =>
            {
                if (model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device &&
                    model.Attachments?.FirstOrDefault(e => e.name == "representation") != null &&
                    imageData?.Length > 0)
                {
                    row.RelativeItem().AlignCenter().Image(imageData).FitArea();
                }
                else
                {
                    row.RelativeItem().AlignCenter().Text(model.TitleOfTradeMark ?? "N/A")
                        .FontSize(18).FontFamily(Fonts.TimesNewRoman);
                }
            });

            column.Item()
                .Text(
                    $"I hereby certify that your name {postRegApp?.Name} has been entered into the Register as a proprietor of the trademark {model.TitleOfTradeMark}, with file number {model.FileId} and RTM {model.RtmNumber}, in class {model.TrademarkClass}, in respect of Abstract.")
                .FontFamily(Fonts.TimesNewRoman).Justify();
            column.Item().Height(5);

            var date = postRegApp?.DateTreated;
            var formattedDate = DateTime.TryParseExact(date, "M/d/yyyy h:mm:ss tt", 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, 
                out var parsedDate) 
                ? parsedDate.ToString("dd MMMM, yyyy") 
                : date;
            
                column.Item().Text("Recordal Information").FontSize(12).SemiBold().FontFamily(Fonts.TimesNewRoman).LineHeight(2);

                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(80);   // Field name
                        columns.RelativeColumn();     // Assignor
                        columns.RelativeColumn();     // Assignee
                    });

                    IContainer Cell(IContainer c) =>
                        c.Border(0.5f)
                         .BorderColor(Colors.Grey.Lighten2)
                         .PaddingVertical(4)
                         .PaddingHorizontal(6);

                    // ===== HEADER ROW =====
                    table.Cell().Element(Cell)
                        .Text(" ")
                        .Bold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text("From:")
                        .Bold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text("To:")
                        .Bold()
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== NAME =====
                    table.Cell().Element(Cell)
                        .Text("Name")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(!string.IsNullOrWhiteSpace(postRegApp.OldName) ? postRegApp.OldName : (applicants?.Name ?? "N/A"))
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(postRegApp.Name)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== ADDRESS =====
                    table.Cell().Element(Cell)
                        .Text("Address")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(!string.IsNullOrWhiteSpace(postRegApp.OldAddress) ? applicants.Address : (applicants?.Address ?? "N/A"))
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(postRegApp.Address)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== PHONE =====
                    table.Cell().Element(Cell)
                        .Text("Phone")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(!string.IsNullOrWhiteSpace(postRegApp.OldPhone) ? applicants.Phone : (applicants?.Phone ?? "N/A"))
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(postRegApp.Phone)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== EMAIL =====
                    table.Cell().Element(Cell)
                        .Text("Email")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(!string.IsNullOrWhiteSpace(postRegApp.OldEmail) ? applicants.Email : (applicants?.Email ?? "N/A"))
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(postRegApp.Email)
                        .FontFamily(Fonts.TimesNewRoman);
                });

            column.Item().Height(40);
            column.Item().Text($"Sealed at my direction, \n{formattedDate}").SemiBold().FontFamily(Fonts.TimesNewRoman);
            column.Item().Height(5);
            if (signature != null)
            {
                column.Item().Height(35).Image(signature.Signature).FitArea();
            }
            else
            {
                column.Item().Height(35).Image("assets/reg.png").FitArea();
            }
            column.Item().Height(5);
            column.Item().Text(signature?.Name ?? "Abubakar Abdullahi").FontFamily(Fonts.TimesNewRoman);
            column.Item().Text("For Registrar,").SemiBold().FontFamily(Fonts.TimesNewRoman);
            column.Item().Text("Trade Marks Registry,").SemiBold().FontFamily(Fonts.TimesNewRoman);
            column.Item().Text("Federal Ministry of Industry, Trade and Investment.").SemiBold()
                    .FontFamily(Fonts.TimesNewRoman);
            column.Item().Text("Federal Capital Territory").SemiBold().FontFamily(Fonts.TimesNewRoman);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().BorderTop(2).BorderColor(Colors.Green.Darken3);
            c.Item().Height(15);
            c.Item().AlignBottom().Row(row =>
            {
                
                row.RelativeItem().AlignLeft().Column(c => { c.Item().AlignCenter().Element(GetQrCode); });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Height(50).Image("assets/Commeciallawdepartmentlogo.png").FitArea();
                });
            });
            c.Item().Text("Scan the QR code to verify the document.").Italic().AlignCenter().FontSize(8);
            
        });
    }

    private void GetQrCode(IContainer container)
    {
        using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
        using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
        {
            byte[] qrCodeImage = qrCode.GetGraphic(10);
            container.Height(50).Width(50).Image(qrCodeImage).FitArea();
        }
    }
}