using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class PatentMortgageAcknowledgementletter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;

        public PatentMortgageAcknowledgementletter(Filling model, string url, Receipt receipt)
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
                    .Text("PATENT MORTGAGE ACKNOWLEDGEMENT LETTER")
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

                // Get the corresponding post-registration mortgage app
                var mortgageRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Patent Mortgage Recordal");

                // MORTGAGOR INFORMATION (old mortgagor / previous patent holder)
                if (mortgageRecordal != null)
                {
                    TwoColumnSection(col, "MORTGAGOR INFORMATION", new[]
                    {
                        ("Name:",        F(mortgageRecordal.OldMortgagorName)),
                        ("Email:",       F(mortgageRecordal.OldMortgagorEmail)),
                        ("Phone:",       F(mortgageRecordal.OldMortgagorPhone)),
                        ("State:",       F(mortgageRecordal.OldMortgagorState)),
                        ("City:",        F(mortgageRecordal.OldMortgagorCity)),
                        ("Address:",     F(mortgageRecordal.OldMortgagorAddress)),
                        ("Nationality:", F(mortgageRecordal.OldMortgagorNationality))
                    });

                    // MORTGAGEE INFORMATION (new mortgage holder – reuse Name/Email/etc on recordal)
                    TwoColumnSection(col, "MORTGAGEE INFORMATION", new[]
                    {
                        ("Name:",        F(mortgageRecordal.Name)),
                        ("Email:",       F(mortgageRecordal.Email)),
                        ("Phone:",       F(mortgageRecordal.Phone)),
                        ("State:",       F(mortgageRecordal.State)),
                        ("City:",        F(mortgageRecordal.City)),
                        ("Address:",     F(mortgageRecordal.Address)),
                        ("Nationality:", F(mortgageRecordal.Nationality))
                    });
                }
                else
                {
                    TwoColumnSection(col, "MORTGAGOR INFORMATION", new[]
                    {
                        ("Name:",        "N/A"),
                        ("Email:",       "N/A"),
                        ("Phone:",       "N/A"),
                        ("State:",       "N/A"),
                        ("City:",        "N/A"),
                        ("Address:",     "N/A"),
                        ("Nationality:", "N/A")
                    });

                    TwoColumnSection(col, "MORTGAGEE INFORMATION", new[]
                    {
                        ("Name:",        "N/A"),
                        ("Email:",       "N/A"),
                        ("Phone:",       "N/A"),
                        ("State:",       "N/A"),
                        ("City:",        "N/A"),
                        ("Address:",     "N/A"),
                        ("Nationality:", "N/A")
                    });
                }

                // PATENT INFORMATION
                col.Item().Element(Header)
                    .Text("PATENT INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                FullWidthBox(col, "Title Of Invention:", F(model.TitleOfInvention));

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("File Origin:", F(model.FileOrigin)),
                    ("Patent type:",      $"{F(model.PatentType)} - {F(model.FileOrigin)}")
                });

                FullWidthBox(col, "Application Type:", F(model.PatentApplicationType));

                // Footer
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

        private static void RenderPriorityEntry(ColumnDescriptor col, int index, PriorityInfo? p)
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Element(Box).Column(c2 =>
                {
                    c2.Item().Text($"{index}. Application Number:")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    WriteText(c2.Item(), F(p?.number));
                });
                r.RelativeItem().Element(Box).Column(c2 =>
                {
                    c2.Item().Text("Country:")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    WriteText(c2.Item(), F(p?.Country));
                });
            });

            FullWidthBox(col, "Date:", F(p?.Date));
        }
    }
}