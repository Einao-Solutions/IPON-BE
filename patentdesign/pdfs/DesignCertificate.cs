using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class DesignCertificate : IDocument
    {
        private readonly Filling model;
        private readonly string expiryDate;
        private readonly string qrUrl;

        public DesignCertificate(Filling model, string expiryDate)
        {
            this.model = model;
            this.expiryDate = expiryDate;
            qrUrl = $"https://portal.iponigeria.com/qr?fileId={model.FileId}";
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeContent(IContainer container)
        {
            var title = string.IsNullOrWhiteSpace(model.TitleOfDesign) ? "-" : model.TitleOfDesign;
            var applicantName = model.applicants?.Count > 1
                ? $"{model.applicants[0].Name} et al."
                : model.applicants?.FirstOrDefault()?.Name ?? "-";
            var applicantAddress = model.applicants?.FirstOrDefault()?.Address ?? "-";
            var correspondenceName = model.Correspondence?.name ?? "-";
            var correspondenceAddress = model.Correspondence?.address ?? "-";

            container.Column(column =>
            {
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().Height(10);

                column.Item().AlignCenter().Text("NIGERIA")
                    .FontSize(20).Bold().FontColor(Colors.Black);

                column.Item().Height(8);
                column.Item().AlignCenter().Text("Certificate of Registration Of Design")
                    .FontColor(Colors.Green.Darken4)
                    .FontSize(20).Bold().FontFamily("Certificate");

                column.Item().Height(8);
                column.Item().AlignCenter().Text("PATENT AND DESIGN ACT")
                    .FontSize(15).Bold().FontColor(Colors.Black);
                column.Item().Height(3);
                column.Item().AlignCenter().Text("(CAP 344 Laws Of The Federation of Nigeria 1990)")
                    .FontSize(12).FontColor(Colors.Black).Bold();
                column.Item().Height(8);

                column.Item().Height(8);
                column.Item().AlignCenter().Text("Design Representation").FontSize(11).Bold().FontColor(Colors.Black);
                column.Item().Height(20);

                //column.Item().AlignCenter().Text(ConstantValues.Passage1)
                //    .FontSize(9);


                column.Item().Height(8);
                column.Item().AlignCenter().Text(ConstantValues.DesignCertificate)
                    .FontSize(9).Justify();

                column.Item().Height(20);
                column.Item().AlignCenter().Text(applicantName)
                    .FontSize(11).Bold().FontColor(Colors.Black);
                column.Item().Height(8);
                column.Item().AlignCenter().Text(applicantAddress)
                    .FontSize(10).FontColor(Colors.Black);

                var approvalDate = model.ApplicationHistory?
                   .SelectMany(a => a.StatusHistory ?? Enumerable.Empty<ApplicationHistory>())
                   .FirstOrDefault(s => s.afterStatus == ApplicationStatuses.Active)?.Date;

                column.Item().AlignCenter().Text($"As of the {approvalDate} for {title}").FontSize(11).Bold().FontColor(Colors.Black);


                //column.Item().Height(15);
                //column.Item().AlignCenter().Text($"C/O {correspondenceName}")
                //    .FontSize(10);
                //column.Item().AlignCenter().Text(correspondenceAddress)
                //    .FontSize(10);



                //column.Item().Height(15);
               
                //column.Item().AlignCenter().Text(
                //    approvalDate.HasValue
                //        ? $"Dated this {approvalDate.Value:dd MMMM yyyy}"
                //        : "Dated this -")
                //    .FontSize(10);

                //column.Item().Height(15);
                //column.Item().AlignCenter().Text($"In respect of: {title}")
                //    .FontSize(10);

                column.Item().Height(20);
                column.Item().AlignCenter().Text(
                    $"Copyright in this Design will expire on {expiryDate} and may, on application made in the prescribed manner, be extended for two further periods of five years each.")
                    .FontSize(9).Justify();

                column.Item().Height(30);
                column.Item().Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(200).Column(col =>
                    {
                        col.Item().AlignCenter().Text("Jane Igwe").FontSize(9);
                        col.Item().AlignCenter().Text("Registrar of Patents and Designs").FontSize(9);
                    });
                });

                var sealingDate = approvalDate ?? model.DateCreated;
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Sealed at my direction,").FontSize(8);
                        col.Item().Text(sealingDate != default
                            ? sealingDate.ToString("dd MMMM yyyy")
                            : "-").FontSize(8).Italic();
                        col.Item().Text("The Patent and Design Registry,").FontSize(8);
                        col.Item().Text("Federal Ministry of Industry, Trade and Investment,").FontSize(8);
                        col.Item().Text("Federal Capital Territory").FontSize(8);
                    });
                    row.ConstantItem(200);
                });

                //column.Item().Height(20);
                //column.Item().AlignCenter().Text(
                //    $"Copyright in this Design will expire on {expiryDate} and may, on application made in the prescribed manner, be extended for two further periods of five years each.")
                //    .FontSize(9).Justify();

                //column.Item().Height(20);
                //column.Item().AlignCenter().Element(GetQrCode);
            });
        }

        private void GetQrCode(IContainer container)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeImage = qrCode.GetGraphic(20);
            container.Image(qrCodeImage).FitArea();
        }
    }
}
