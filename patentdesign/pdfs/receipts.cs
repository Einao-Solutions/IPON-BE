// using QRCoder;

using System.Globalization;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class ReceiptModel(Receipt receipt, string url, Filling model) : IDocument
    {
        string nairaSymbol = "\u20A6";
        private Receipt receipt { get; set; } = receipt;
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
        void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    // Header with coat of arms and ministry information
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("PAYMENT RECEIPT").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);

                    //Payment Information Section
                    column.Item().Table(table =>
                    {
                        string date = "-";
                        if (!string.IsNullOrWhiteSpace(receipt.Date))
                        {
                            if (DateTime.TryParse(receipt.Date, out var parsedDate))
                                date = parsedDate.ToString("dd/MM/yyyy");
                        }
                        string amount = "-";
                        if (!string.IsNullOrWhiteSpace(receipt.Amount))
                        {
                            if (long.TryParse(receipt.Amount, out var parsedAmount))
                                amount = parsedAmount.ToString("N0");
                        }
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PAYMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(date).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(receipt.rrr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(receipt.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Amount Paid:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text($"{nairaSymbol} {amount}").FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                            c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(receipt.PaymentFor).FontSize(12).FontFamily(Fonts.TimesNewRoman);
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
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Applicant Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.applicants[0].Name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.applicants[0].Email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.applicants[0].Phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.applicants[0].country).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                            c.Item().Text("Applicant Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.applicants[0].Address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });
                    // Correspondence Information Section
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.Correspondence.name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.Correspondence.address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.Correspondence.email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.Correspondence.phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });
                    column.Item().Height(40);

                    // Notification Message 
                    column.Item().AlignCenter().Text("PLEASE KEEP THIS RECEIPT FOR FUTURE REFERENCE")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);

                });
        }
        
        // private void GetQrCode(IContainer container)
        // {
        //     using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        //     using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
        //     using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
        //     {
        //         byte[] qrCodeImage = qrCode.GetGraphic(20);
        //         container.Image(qrCodeImage).FitArea();
        //     }
        // }
    }
}