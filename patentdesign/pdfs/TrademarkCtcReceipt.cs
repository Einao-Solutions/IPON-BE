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
                    .FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                // Receipt Table
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                    });

                    table.Cell().Element(HeaderElement).Text("Date").Bold();
                    table.Cell().Element(Block).Text(date);

                    table.Cell().Element(HeaderElement).Text("File Number").Bold();
                    table.Cell().Element(Block).Text(F(model.FileId));

                    table.Cell().Element(HeaderElement).Text("Trademark Title").Bold();
                    table.Cell().Element(Block).Text(F(model.TitleOfTradeMark));

                    table.Cell().Element(HeaderElement).Text("Fee Title").Bold();
                    table.Cell().Element(Block).Text(F(receipt.PaymentFor));

                    table.Cell().Element(HeaderElement).Text("Payment RRR").Bold();
                    table.Cell().Element(Block).Text(F(receipt.rrr));

                    table.Cell().Element(HeaderElement).Text("Amount Paid").Bold();
                    table.Cell().Element(Block).Text(amountDisplay).Bold();
                });

                col.Item().PaddingTop(30).AlignCenter()
                    .Text("THANK YOU FOR YOUR PAYMENT")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);

                col.Item().AlignCenter().PaddingTop(10)
                    .Text("For inquiries, please contact us at info@trademarks.gov.ng")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(10);
            });
        }
    }
}
