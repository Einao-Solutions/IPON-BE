using System;
using System.Linq;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignLicenseReceipt : IDocument
    {
        private readonly string nairaSymbol = "\u20A6";
        private readonly Receipt _receipt;
        private readonly Filling _model;
        private readonly string _url;

        public DesignLicenseReceipt(Receipt receipt, string url, Filling model)
        {
            _receipt = receipt;
            _model = model;
            _url = url;
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private static IContainer HeaderElement(IContainer container) => container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .AlignMiddle()
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(1)
            .PaddingLeft(5);

        private static IContainer Block(IContainer container) => container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .PaddingVertical(3)
            .PaddingLeft(5)
            .AlignLeft();

        private void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2)
                        .FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("DESIGN LICENSE RECEIPT")
                        .FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);

                    column.Item().Table(table =>
                    {
                        var date = "-";
                        if (!string.IsNullOrWhiteSpace(_receipt.Date) && DateTime.TryParse(_receipt.Date, out var parsedDate))
                        {
                            date = parsedDate.ToString("dd/MM/yyyy");
                        }

                        var amount = "-";
                        if (!string.IsNullOrWhiteSpace(_receipt.Amount) && decimal.TryParse(_receipt.Amount, out var parsedAmount))
                        {
                            amount = parsedAmount.ToString("N0");
                        }

                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement)
                            .Text("PAYMENT INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(date).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment RRR:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_receipt.rrr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_receipt.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Amount Paid:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text($"{nairaSymbol} {amount}").FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_receipt.PaymentFor).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement)
                            .Text("APPLICANT INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Applicant Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.applicants.FirstOrDefault()?.Name ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.applicants.FirstOrDefault()?.Email ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.applicants.FirstOrDefault()?.Phone ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.applicants.FirstOrDefault()?.country ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Applicant Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.applicants.FirstOrDefault()?.Address ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement)
                            .Text("CORRESPONDENCE INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.Correspondence?.name ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.Correspondence?.address ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.Correspondence?.email ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(_model.Correspondence?.phone ?? "-")
                                .FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    column.Item().Height(40);
                    column.Item().AlignCenter().Text("PLEASE KEEP THIS RECEIPT FOR FUTURE REFERENCE")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                });
        }
    }
}
