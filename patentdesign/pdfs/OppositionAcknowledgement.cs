// using QRCoder;

using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class OppositionAcknowledgement(OppositionAckType model, string url) : IDocument
    {
        private OppositionAckType model { get; set; } = model;
        private string url { get; set; } = url;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight().Image("assets/ministry.png").FitArea();
                });
            });
        }

        static IContainer HeaderElement(IContainer container) => container
            .Border(1)
            .Background(Colors.Grey.Lighten3)
            .ShowOnce()
            .MinHeight(25)
            .PaddingVertical(3)
            .PaddingLeft(5)
            .AlignLeft();

        static IContainer Block(IContainer container) => container
            .Border(1)
            .ShowOnce()
            .MinHeight(25)
            .PaddingVertical(3)
            .PaddingLeft(5)
            .AlignLeft();

        static string F(string? v) => string.IsNullOrWhiteSpace(v) ? "N/A" : v;

        void ComposeContent(IContainer container)
        {
            var file = model.file;
            var title = file?.Type switch
            {
                FileTypes.Design => file.TitleOfDesign,
                FileTypes.Patent => file.TitleOfInvention,
                _ => file?.TitleOfTradeMark
            };
            var applicantName = file?.applicants?.FirstOrDefault()?.Name;

            container.PaddingVertical(10).Column(column =>
            {
                // Header
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(10);
                column.Item().AlignCenter().Text("NEW OPPOSITION ACKNOWLEDGEMENT LETTER").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                column.Item().Height(15);

                // Opposition Information
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("OPPOSITION INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(13).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Opposition Filing Date:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(model.date.ToString("dd MMMM, yyyy")).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Payment RRR:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.paymentId)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Opposition ID:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.oppositionId)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                });

                column.Item().Height(8);

                // Opposer Information
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("OPPOSER INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(13).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Name:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.name)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Email:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.email)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Address:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.address)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Phone Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.number)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().ColumnSpan(1).Element(Block).Column(c => { });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Reason for Opposition:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(model.reason)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                });

                column.Item().Height(8);

                // Trademark Information
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("TRADEMARK INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(13).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("File Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(file?.FileId)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Title:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(title)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Product Class:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(file?.TrademarkClass?.ToString() ?? "N/A").FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Representation of Trademark:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(file?.TrademarkLogo?.ToString())).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                    {
                        c.Item().Text("Applicant Name:").FontFamily(Fonts.TimesNewRoman).FontSize(10).SemiBold();
                        c.Item().Text(F(applicantName)).FontFamily(Fonts.TimesNewRoman).FontSize(11);
                    });
                });

                column.Item().Height(20);
                column.Item().AlignCenter().Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken2);
            });
        }
    }
}
