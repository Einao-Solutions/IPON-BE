using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs;

public class AssignmentCert(Filling model, string url, byte[]? imageData, string applicationId): IDocument
{
     private Filling model { get; set; } = model;
        private string url { get; set; } = url;
        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeContent(IContainer container)
        {
            var app = model.PostRegApplications?.FirstOrDefault(r=>r.Id == applicationId);
            var firstApplicant = model.ApplicationHistory[0].Applicants.FirstOrDefault();
            var postRegApp = model.PostRegApplications?.FirstOrDefault(a => a.Id == applicationId);
            var date = postRegApp?.DateTreated;
            var formattedDate = DateTime.TryParseExact(date, "M/d/yyyy h:mm:ss tt",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedDate)
                ? parsedDate.ToString("dd MMMM, yyyy")
                : date;

            var assignee = model.Assignees.FirstOrDefault(a => a.Id == applicationId);
            var assignor = model.ApplicationHistory[0].Applicants[0]; 
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
                column.Item().AlignCenter().Text($"Certificate Of Assignment")
                    .FontFamily("Certificate").FontSize(30).Bold().FontColor(Colors.Green.Darken3);
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(5);
                column.Item().AlignCenter()
                    .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(10);
                
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

                column.Item().Height(5);
                column.Item().Text($"I hereby certify that your name has been entered into the Register as a proprietor(s) of the trademark {model.TitleOfTradeMark}, with file number {model.FileId} and RTM {model.RtmNumber}, in class {model.TrademarkClass}, in respect of Abstract.")
                    .FontFamily(Fonts.TimesNewRoman).Justify();
                column.Item().Text($"Pursuant to the Deed of Assignment dated {(DateTime.TryParse(app?.FilingDate, out var d) ? d.ToString("dd MM yyyy") : app?.FilingDate)}")
                    .FontFamily(Fonts.TimesNewRoman);
                column.Item().Height(10);

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
                        .Text("Assignor")
                        .Bold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text("Assignee")
                        .Bold()
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== NAME =====
                    table.Cell().Element(Cell)
                        .Text("Name")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.AssignorName ?? assignor.Name)
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.Name)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== ADDRESS =====
                    table.Cell().Element(Cell)
                        .Text("Address")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.AssignorAddress ?? assignor.Address)
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.Address)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== PHONE =====
                    table.Cell().Element(Cell)
                        .Text("Phone")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.AssignorPhone ?? assignor.Phone)
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.Phone)
                        .FontFamily(Fonts.TimesNewRoman);

                    // ===== EMAIL =====
                    table.Cell().Element(Cell)
                        .Text("Email")
                        .SemiBold()
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.AssignorEmail ?? assignor.Email)
                        .FontFamily(Fonts.TimesNewRoman);

                    table.Cell().Element(Cell)
                        .Text(assignee.Email)
                        .FontFamily(Fonts.TimesNewRoman);
                });

                column.Item().Height(40);
                column.Item().Text($"Sealed at my direction, \n{formattedDate}").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Height(30).Image("assets/reg.png").FitArea();
                column.Item().Height(10);
                column.Item().Text("Abubakar Abdullahi").FontFamily(Fonts.TimesNewRoman);
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
                c.Item().Height(10);
                c.Item().AlignBottom().Row(row =>
                {
                
                    row.RelativeItem().AlignLeft().Column(c => { c.Item().AlignCenter().Element(GetQrCode); });
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Height(50).Image("assets/Commeciallawdepartmentlogo.png").FitArea();
                    });
                });
                c.Item().Text("Scan the QR code to verify the document.").Italic().AlignCenter().FontSize(8);
                // IContainer BlockStyle(IContainer container) =>
                //     container.Background(Colors.Green.Darken3).Padding(10);
            });
        }

        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(10);
                container.Height(50).Width(100).Image(qrCodeImage).FitArea();
            }
        }
}