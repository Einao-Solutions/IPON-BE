using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignLicenseRefusalLetter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly ApplicationInfo application;

        public DesignLicenseRefusalLetter(Filling model, string url, Receipt receipt, ApplicationInfo application)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
            this.application = application;
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
                    .Text("DESIGN LICENSE REFUSAL LETTER")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Red.Darken2).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:",   F(receipt.PaymentFor)),
                });

                var licenseRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Design License Recordal");

                if (licenseRecordal != null)
                {
                    TwoColumnSection(col, "LICENSOR INFORMATION", new[]
                    {
                        ("Name:",        F(licenseRecordal.OldLicensorName)),
                        ("Email:",       F(licenseRecordal.OldLicensorEmail)),
                        ("Phone:",       F(licenseRecordal.OldLicensorPhone)),
                        ("State:",       F(licenseRecordal.OldLicensorState)),
                        ("City:",        F(licenseRecordal.OldLicensorCity)),
                        ("Address:",     F(licenseRecordal.OldLicensorAddress)),
                        ("Nationality:", F(licenseRecordal.OldLicensorNationality))
                    });

                    TwoColumnSection(col, "LICENSEE INFORMATION", new[]
                    {
                        ("Name:",        F(licenseRecordal.Name)),
                        ("Email:",       F(licenseRecordal.Email)),
                        ("Phone:",       F(licenseRecordal.Phone)),
                        ("State:",       F(licenseRecordal.State)),
                        ("City:",        F(licenseRecordal.City)),
                        ("Address:",     F(licenseRecordal.Address)),
                        ("Nationality:", F(licenseRecordal.Nationality))
                    });
                }
                else
                {
                    TwoColumnSection(col, "LICENSOR INFORMATION", new[]
                    {
                        ("Name:",        "N/A"),
                        ("Email:",       "N/A"),
                        ("Phone:",       "N/A"),
                        ("State:",       "N/A"),
                        ("City:",        "N/A"),
                        ("Address:",     "N/A"),
                        ("Nationality:", "N/A")
                    });

                    TwoColumnSection(col, "LICENSEE INFORMATION", new[]
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

                col.Item().Element(Header)
                    .Text("DESIGN INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                FullWidthBox(col, "Title Of Design:", F(model.TitleOfDesign));

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("File Origin:", F(model.FileOrigin)),
                    ("Design type:", F(model.DesignType))
                });

                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));
                FullWidthBox(col, "Application Type:", F(model.ApplicationHistory?.FirstOrDefault()?.ApplicationType));

                col.Item().Element(Header)
                    .Text("REFUSAL INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                var refusalHistory = application.StatusHistory
                    .LastOrDefault(h => h.afterStatus == ApplicationStatuses.Rejected);

                var officerName = refusalHistory?.User ?? "-";
                var reason = refusalHistory?.Message ?? "-";

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Officer's Name:", officerName),
                    ("Reason:",       reason)
                });

                col.Item().AlignCenter().PaddingTop(30)
                    .Text("YOUR APPLICATION HAS BEEN REFUSED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Red.Darken2);
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
