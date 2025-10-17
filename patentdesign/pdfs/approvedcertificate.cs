
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class ApprovedCertificate(Filling model, string url) : IDocument
    {
        private Filling model { get; set; } = model;
        private string url { get; set; } = url;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        void ComposeContent(IContainer container)
        {
            var title = model.Type == FileTypes.Design
                ? model.TitleOfDesign
                : model.Type == FileTypes.Patent
                    ? model.TitleOfInvention
                    : model.TitleOfTradeMark;

            var applicantName = model.applicants.Count > 1
                ? model.applicants[0].Name + " et al."
                : model.applicants[0].Name;

            var applicantAddress = model.applicants[0].Address;

            container.Column(column =>
            {
                // Coat of arms
                // Header
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().Height(10);

                // Main headings
                column.Item().AlignCenter().Text("NIGERIA")
                    .FontSize(20).Bold().FontColor(Colors.Black);

                column.Item().Height(10);
                column.Item().AlignCenter().Text("Certificate of Registration Of Patent").FontColor(Colors.Green.Darken4)
                    .FontSize(20).Bold().FontFamily("Certificate");

                column.Item().Height(10);
                column.Item().AlignCenter().Text("PATENT AND DESIGN ACT")
                    .FontSize(15).Bold().FontColor(Colors.Black).Bold();
                column.Item().Height(3);

                column.Item().AlignCenter().Text("(CAP 344 Laws Of The Federation of Nigeria 1990)")
                    .FontSize(12).FontColor(Colors.Black).Bold();

                column.Item().Height(15);

                // Key details
                column.Item().Text(text =>
                {
                    text.Span("RP: ").Bold().FontSize(10);
                    text.Span(model.FileId ?? "-").FontSize(9);
                });
                column.Item().Text(text =>
                {
                    text.Span("Filing Date: ").Bold().FontSize(10);
                    text.Span(model.DateCreated.ToString("dd MMMM yyyy")).FontSize(9);
                });
                //column.Item().Text(text =>
                //{
                //    text.Span("Sealing Date: ").Bold().FontSize(10);
                //    // If you have a sealing date, use it; else, show "-"
                //    text.Span(model.LastRequestDate != default ? model.LastRequestDate.ToString("dd MMMM yyyy") : "-").FontSize(9);
                //});

                column.Item().Height(20);

                // Title of invention
                column.Item().AlignCenter().Text(model.TitleOfInvention)
                    .FontSize(11).Bold().FontColor(Colors.Black);
                column.Item().Height(20);

                column.Item().AlignCenter().Text(ConstantValues.Passage1)
                    .FontSize(9);

                column.Item().Height(20);

                // Applicant name and address
                column.Item().AlignCenter().Text(applicantName)
                    .FontSize(11).Bold().FontColor(Colors.Black);

                column.Item().Height(12);

                column.Item().AlignCenter().Text(applicantAddress)
                    .FontSize(10).FontColor(Colors.Black);

                column.Item().Height(15);

                column.Item().Height(8);
                column.Item().Text(ConstantValues.Passage8).FontSize(9).Justify();
                column.Item().Height(8).AlignCenter();
                column.Item().Text(ConstantValues.Passage11).FontSize(9).Justify();
                column.Item().Height(8).AlignCenter();
                column.Item().Text(ConstantValues.Passage9).FontSize(9).Justify();
                column.Item().Height(8).AlignCenter();
                column.Item().Text(ConstantValues.Passage10).FontSize(9).Justify();

                column.Item().Height(30);
                column.Item().Row(row =>
                {
                    row.ConstantItem(400).AlignRight().Column(col =>
                    {
                        col.Item().Text("Jane Igwe").FontSize(9);
                        col.Item().Text("Registrar of Patent and Des").FontSize(9);
                    });
                });

                 column.Item().AlignCenter().Element(GetQrCode);

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
