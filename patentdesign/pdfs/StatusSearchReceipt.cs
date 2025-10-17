using CloudinaryDotNet.Core;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class StatusSearchReceipt(Filling model, ApplicationInfo selectedHistory) : IDocument
    {
        private Filling model { get; set; } = model;
        private ApplicationInfo selectedHistory { get; set; } = selectedHistory;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
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

        void ComposeContent(IContainer container)
        {
            var applicant = model?.applicants?.FirstOrDefault();

            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    // Logo (if you want to add, uncomment below)
                    // column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();

                    // Header
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);

                    // Receipt Title
                    column.Item().AlignCenter().Text("PAYMENT RECEIPT")
                        .FontColor(Colors.Green.Darken2)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(16)
                        .Bold();
                    column.Item().Height(25);

                    // PAYMENT INFORMATION
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PAYMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        // Payment Date / rrr

                        //var selectedHistory = model.ApplicationHistory?.FirstOrDefault();
                        //var date = selectedHistory?.ApplicationDate.ToString("yyyy-MM-dd") ?? "Populate here";
                        //var paymentId = selectedHistory?.PaymentId ?? "Populate here";

                        var date = selectedHistory?. ApplicationDate.ToString("yyyy-MM-dd") ?? "Populate here";
                        var paymentId = selectedHistory?.PaymentId ?? "Populate here";


                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(date ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(paymentId ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        
                        // File number / Amount Paid
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("File number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.FileId ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Amount Paid:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text("9500").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });

                        // Fee Title
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text("Status Search ").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    // APPLICANT INFORMATION
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(applicant?.Name ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(applicant?.Email ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(applicant?.Phone ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(applicant?.country ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(applicant?.Address ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    // CORRESPONDENCE INFORMATION
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.name ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.email ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.phone ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("State:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.state ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.address ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    column.Item().Height(40);

                    // Footer
                    column.Item().AlignCenter().Text("PLEASE KEEP THIS RECEIPT FOR FUTURE REFERENCE")
                        .FontColor(Colors.Green.Darken2)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Bold();
                });
        }
    }
}