using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignMergerAcknowledgementletter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;

        public DesignMergerAcknowledgementletter(Filling model, string url, Receipt receipt)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private static IContainer Box(IContainer c) => c
            .Border(1)
            .Padding(5)
            .AlignLeft();

        private static IContainer Header(IContainer c) => c
            .Border(1)
            .Background(Colors.Grey.Lighten1);

        private static void WriteText(IContainer cell, string text)
        {
            var placeholder = text == "N/A";
            cell.Text(text)
                .FontFamily(Fonts.TimesNewRoman)
                .FontSize(12)
                .Italic(placeholder)
                .FontColor(Colors.Black);
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
                if (!string.IsNullOrWhiteSpace(receipt.Date) &&
                    DateTime.TryParse(receipt.Date, out var parsedDate))
                {
                    date = parsedDate.ToString("dd/MM/yyyy");
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
                    .Text("DESIGN MERGER ACKNOWLEDGEMENT LETTER")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                // PAYMENT INFORMATION
                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:",   F(receipt.PaymentFor)),
                });

                // APPLICANT INFORMATION (original applicant from file)
                var applicant = model.applicants?.FirstOrDefault();
                TwoColumnSection(col, "APPLICANT INFORMATION", new[]
                {
                    ("Name:",        F(applicant?.Name)),
                    ("Email:",       F(applicant?.Email)),
                    ("Phone number:", F(applicant?.Phone)),
                    ("Nationality:", F(applicant?.country)),
                });
                FullWidthBox(col, "Address:", F(applicant?.Address));

                // Get corresponding post-registration merger app
                var mergerRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Design Merger Recordal");

                // MERGER INFORMATION (new merged entity)
                if (mergerRecordal != null)
                {
                    TwoColumnSection(col, "MERGER INFORMATION", new[]
                    {
                        ("Name:",        F(mergerRecordal.Name)),
                        ("Email:",       F(mergerRecordal.Email)),
                        ("Phone number:", F(mergerRecordal.Phone)),
                        ("Nationality:", F(mergerRecordal.Nationality)),
                    });
                    FullWidthBox(col, "Address:", F(mergerRecordal.Address));
                }
                else
                {
                    TwoColumnSection(col, "MERGER INFORMATION", new[]
                    {
                        ("Name:",        "N/A"),
                        ("Email:",       "N/A"),
                        ("Phone number:", "N/A"),
                        ("Nationality:", "N/A"),
                    });
                    FullWidthBox(col, "Address:", "N/A");
                }

                // DESIGN INFORMATION
                col.Item().Element(Header)
                    .Text("DESIGN INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                FullWidthBox(col, "Title of Industrial Design:", F(model.TitleOfDesign));
                FullWidthBox(col, "Design Type:", F(model.DesignType));
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                col.Item().AlignCenter().PaddingTop(30)
                    .Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);
            });
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                col.Item().Element(Header)
                    .Text(title)
                    .FontFamily(Fonts.TimesNewRoman)
                    .FontSize(14)
                    .Bold();
            }

            for (var i = 0; i < pairs.Length; i += 2)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item()
                            .Text(pairs[i].Label)
                            .FontFamily(Fonts.TimesNewRoman)
                            .FontSize(10)
                            .Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });

                    if (i + 1 < pairs.Length)
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item()
                                .Text(pairs[i + 1].Label)
                                .FontFamily(Fonts.TimesNewRoman)
                                .FontSize(10)
                                .Bold();
                            WriteText(c2.Item(), pairs[i + 1].Value);
                        });
                    }
                    else
                    {
                        row.RelativeItem();
                    }
                });
            }
        }

        private static void FullWidthBox(ColumnDescriptor col, string label, string value)
        {
            col.Item().Element(Box).Column(c2 =>
            {
                if (!string.IsNullOrEmpty(label))
                {
                    c2.Item()
                        .Text(label)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(10)
                        .Bold();
                }

                WriteText(c2.Item(), value);
            });
        }
    }
}
