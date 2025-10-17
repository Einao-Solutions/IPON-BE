using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs;

public class RegisteredUserCert(Filling model, string url, byte[]? imageData, string applicationId): IDocument
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

        private void ComposeContent(IContainer container)
        {
            var regUser = model.RegisteredUsers?.FirstOrDefault(r=>r.Id == applicationId);
            // var app = model.ApplicationHistory?.FirstOrDefault(a=>a.id == applicationId);
            // container.PaddingVertical(5)
            //     .Column(column =>
            //     {
            //         column.Item().AlignCenter().Text("Invalid applicant data").FontSize(16).FontColor(Colors.Red.Medium);
            //     }
            //     );
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
                column.Item().AlignCenter().Text($"Certificate Of Registered User")
                    .FontFamily("Certificate").FontSize(30).Bold().FontColor(Colors.Green.Darken3);
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(5);
                column.Item().AlignCenter()
                    .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(20);
                column.Item().Text("To:").FontFamily(Fonts.TimesNewRoman).FontSize(12);
                column.Item().Height(5);
                column.Item().Text(regUser?.Name).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                column.Item().Height(5);
                column.Item().Text(regUser?.Address).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                column.Item().Height(10);
                column.Item().Height(70).PaddingTop(10).Row(row =>
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
                column.Item().Text($"I hereby certify that your name has been entered into the Register as a registered user of the trademark {model.TitleOfTradeMark}, with file number {model.FileId} and RTM {model.RtmNumber}, in class {model.TrademarkClass}.")
                    .FontFamily(Fonts.TimesNewRoman).Justify();
                column.Item().Height(30);
                var postRegApp = model.PostRegApplications?.FirstOrDefault(a => a.Id == applicationId);
                
                var date = postRegApp?.DateTreated;
                var formattedDate = DateTime.TryParseExact(date, "M/d/yyyy h:mm:ss tt", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, 
                    out var parsedDate) 
                    ? parsedDate.ToString("dd MMMM, yyyy") 
                    : date;
                
                column.Item().Text($"Sealed at my direction, \n{formattedDate}").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Height(35).Image("assets/reg.png").FitArea();
                column.Item().Height(20);
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
                c.Item().LineHorizontal(2).LineColor(Colors.Green.Darken3);
                c.Item().Height(15);
                c.Item().AlignBottom().Row(row =>
                {
                
                    row.RelativeItem().AlignLeft().Column(c => { c.Item().AlignCenter().Element(GetQrCode); });
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Height(100).Image("assets/Commeciallawdepartmentlogo.png").FitArea();
                    });
                });
                c.Item().Text("Scan the QR code to verify the document.").Italic().AlignCenter().FontSize(8);
                IContainer BlockStyle(IContainer container) =>
                    container.Background(Colors.Green.Darken3).Padding(10);
            });
        }
               

        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(10);
                container.Height(100).Width(100).Image(qrCodeImage).FitArea();
            }
        }
}