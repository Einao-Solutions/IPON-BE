using Microsoft.EntityFrameworkCore.Metadata.Internal;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static System.Net.Mime.MediaTypeNames;

namespace patentdesign.pdfs
{
    public class NewTrademarkCertificate(Filling model, string url, byte[]? imageData = null) : IDocument
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
            container.Column(column =>
            {
                column.Item().Height(30);
                column.Item().Height(70).Row(row =>
                {
                    row.RelativeItem().Width(40);
                    row.RelativeItem().AlignCenter().Image("assets/logo.png").FitArea();
                    row.RelativeItem().AlignRight().Text($"RTM: {model.RtmNumber ?? "N/A"}");
                });

                column.Item().Height(10);
                column.Item().AlignCenter().Text($"NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(13).Bold();
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"Certificate Of Registration Of Trademark")
                    .FontFamily("Certificate").FontSize(30).Bold().FontColor(Colors.Green.Darken3);
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(5);
                column.Item().AlignCenter()
                    .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
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

                column.Item().Height(10);
                column.Item().Text($"The Trade Marks shown above has been registered in Part A (or B) of the register in the name of")
                    .FontFamily(Fonts.TimesNewRoman).Justify();
                column.Item().Height(10);

                var applicantName = model.applicants?.Count > 1
                    ? model.applicants[0]?.Name + " et al."
                    : model.applicants?.FirstOrDefault()?.Name ?? "N/A";
                column.Item().Text(applicantName).SemiBold().FontFamily(Fonts.TimesNewRoman).AlignCenter();

                column.Item().Height(13);
                var applicantAddress = model.applicants?.FirstOrDefault()?.Address ?? "N/A";
                column.Item().Text(applicantAddress).FontFamily(Fonts.TimesNewRoman).AlignCenter();

                column.Item().Height(7);
                var date = model.DateCreated;
                column.Item()
                    .Text($"In class {model.TrademarkClass ?? 0} under No.{model.RtmNumber ?? "N/A"}-{model.ApplicationHistory?.FirstOrDefault()?.CertificatePaymentId ?? "N/A"} as Of {date:MMMM dd, yyyy} in Respect of")
                    .FontFamily(Fonts.TimesNewRoman).AlignCenter();

                column.Item().Height(9);
                column.Item().Text(model.TrademarkClassDescription ?? "N/A").FontSize(12).FontFamily(Fonts.TimesNewRoman).AlignCenter().ClampLines(6);

                //QR Code
                column.Item().AlignCenter().Element(GetQrCode);

            });
        }
        private void ComposeFooter(IContainer container)
        {
            container.Column(c => 
            {
                c.Item().AlignBottom().Row(row =>
                {
                    row.RelativeItem().Text(txt =>
                    {
                        var approvalDate = model.ApplicationHistory?.FirstOrDefault()?.StatusHistory
                            ?.LastOrDefault(x => x.afterStatus == ApplicationStatuses.Active)?.Date ?? DateTime.Now;
                        txt.Span($"Sealed at my direction, this date \n{approvalDate:D}").SemiBold().FontFamily(Fonts.TimesNewRoman);
                        txt.EmptyLine();
                        txt.Span("The Trade Marks Registry,").SemiBold().FontFamily(Fonts.TimesNewRoman);
                        txt.EmptyLine();
                        txt.Span("Federal Ministry of Industry, Trade & Investment").SemiBold().FontFamily(Fonts.TimesNewRoman);
                        txt.EmptyLine();
                        txt.Span("Federal Capital Territory").SemiBold().FontFamily(Fonts.TimesNewRoman);
                        txt.EmptyLine();
                        txt.Span("Abuja, Nigeria").SemiBold().FontFamily(Fonts.TimesNewRoman);
                    });

                    row.Spacing(50);
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Height(35).Image("assets/trademark_registrar_sig.png").FitArea();
                        c.Item().Height(20);
                        c.Item().Text("Shafiu Adamu Yauri").FontFamily(Fonts.TimesNewRoman);
                        c.Item().Text("Registrar Of Trademarks").SemiBold().FontFamily(Fonts.TimesNewRoman);
                    });
                });
                IContainer BlockStyle(IContainer container) =>
                    container.Background(Colors.Green.Darken3).Padding(10);

                c.Item().Element(BlockStyle).Text(ConstantValues.TrademarkPassage1).FontFamily(Fonts.TimesNewRoman).FontColor(Colors.White).AlignCenter();
                c.Item().Height(10);
                c.Item().Text(ConstantValues.TrademarkPassage2).FontFamily(Fonts.TimesNewRoman).AlignCenter();
            });
        }

        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }
    }
}