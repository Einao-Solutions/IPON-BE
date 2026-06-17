using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class TrademarkCtcReceipt(Receipt receipt, string url, Filling model) : IDocument
    {
        string nairaSymbol = "\u20A6";

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private static IContainer HeaderElement(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinHeight(20)
                .AlignMiddle()
                .Background(Colors.Grey.Lighten3)
                .Padding(5);
        }

        private static IContainer Block(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinHeight(20)
                .PaddingLeft(5)
                .PaddingVertical(5)
                .AlignLeft();
        }

        private static string F(object? v) => v switch
        {
            null => "N/A",
            string s when string.IsNullOrWhiteSpace(s) => "N/A",
            DateTime dt when dt == default => "N/A",
            DateTime dt => dt.ToString("dd MMMM, yyyy"),
            _ => v.ToString() ?? "N/A"
        };

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                var date = "-";
                var amountDisplay = "-";
                if (!string.IsNullOrWhiteSpace(receipt.Date) &&
                    DateTime.TryParse(receipt.Date, out var parsedDate))
                {
                    date = parsedDate.ToString("dd/MM/yyyy");
                }

                if (!string.IsNullOrWhiteSpace(receipt.Amount) &&
                    decimal.TryParse(receipt.Amount, out var parsedAmount))
                {
                    amountDisplay = $"{nairaSymbol}{parsedAmount.ToString("N0")}";
                }

                // Header
                col.Item().Height(60).AlignCenter().PaddingBottom(10)
                    .Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10)
                    .Text("FEDERAL REPUBLIC OF NIGERIA")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter()
                    .Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10)
                    .Text("COMMERCIAL LAW DEPARTMENT")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter()
                    .Text("TRADEMARK CTC RECEIPT")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Green.Darken2).ExtraBold();
                col.Item().Height(10);

                // PAYMENT INFORMATION
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PAYMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                    table.Cell().Element(Block).Text("Payment Date:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(date).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Payment RRR:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(receipt.rrr)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("File Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(model.FileId)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Amount Paid:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(amountDisplay).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Fee Title:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        c.Item().Text(F(receipt.PaymentFor)).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                    });

                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Trademark Title:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        c.Item().Text(F(model.TitleOfTradeMark)).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                    });
                });

                col.Item().Height(10);

                // APPLICANT INFORMATION
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                    var applicant = model.applicants?.FirstOrDefault();

                    table.Cell().Element(Block).Text("Applicant Name:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(applicant?.Name)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Email:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(applicant?.Email)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Phone Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(applicant?.Phone)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Nationality:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(applicant?.country)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Applicant Address:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        c.Item().Text(F(applicant?.Address)).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                    });
                });

                col.Item().Height(10);

                // CORRESPONDENCE INFORMATION
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                    table.Cell().Element(Block).Text("Name:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(model.Correspondence?.name)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Address:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        c.Item().Text(F(model.Correspondence?.address)).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                    });

                    table.Cell().Element(Block).Text("Email:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(model.Correspondence?.email)).FontFamily(Fonts.TimesNewRoman).FontSize(12);

                    table.Cell().Element(Block).Text("Phone Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    table.Cell().Element(Block).Text(F(model.Correspondence?.phone)).FontFamily(Fonts.TimesNewRoman).FontSize(12);
                });

                col.Item().Height(10);

                // Footer
                col.Item().AlignCenter().PaddingTop(20)
                    .Text("PLEASE KEEP THIS RECEIPT FOR FUTURE REFERENCE")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken2);
            });
        }
    }
}
