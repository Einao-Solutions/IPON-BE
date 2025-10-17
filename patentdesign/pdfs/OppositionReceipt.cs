// using QRCoder;

using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class OppositionReceipt(OppositionReceiptType receipt, string url) : IDocument
    {
        private OppositionReceiptType receipt { get; set; } = receipt;
        private string url { get; set; } = url;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(50);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight(). Image("assets/ministry.png").FitArea();
                });
            });
        }

           static IContainer HeaderElement(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten3)
                .ShowOnce()
                .MinWidth(50)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }
        
        static IContainer Block(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinWidth(50)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }  
        void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(10)
                .Column(column =>
                {
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA");
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                    column.Item().AlignCenter().Text("PATENTS AND DESIGNS ACT CAP 344, LFN 1990");
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("PAYMENT RECEIPT");
                    column.Item().Height(20);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(tableColumns =>
                        {
                            tableColumns.RelativeColumn();
                            tableColumns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Payment Information").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text("Payment For").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(receipt.description);    
                        table.Cell().Element(Block).Text("Payment Date").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(receipt.date.ToString("D"));
                        table.Cell().Element(Block).Text("Remita RRR").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text(receipt.paymentId);
                        table.Cell().Element(Block).Text("Amount Paid").Style(TextStyle.Default.SemiBold());
                        table.Cell().Element(Block).Text($"# {receipt.amount}");
                    });
                    column.Spacing(15);
                });
        }
    }
}