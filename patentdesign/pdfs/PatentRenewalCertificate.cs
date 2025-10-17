using Microsoft.EntityFrameworkCore.Metadata.Internal;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Linq;

namespace patentdesign
{
    public class PatentRenewalCertificate : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;

        public PatentRenewalCertificate(Filling model, string url, Receipt receipt)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

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
            bool placeholder = text == "N/A";
            cell.Text(text)
                .FontFamily(Fonts.TimesNewRoman)
                .FontSize(12)
                .Italic(placeholder)
                .FontColor(placeholder ? Colors.Black : Colors.Black);
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
                // Header (logo & titles)
                col.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                col.Item().Height(10);

                // Main headings
                col.Item().AlignCenter().Text("NIGERIA")
                    .FontSize(20).Bold().FontColor(Colors.Black);

                col.Item().Height(10);
                col.Item().AlignCenter().Text("Certificate of Renewal").FontColor(Colors.Green.Darken4)
                    .FontSize(35).Bold().FontFamily("Certificate");

                col.Item().Height(10);
                col.Item().AlignCenter().Text("Patent And Design Act")
                    .FontSize(15).Bold().FontColor(Colors.Black).Bold();
                col.Item().Height(3);

                col.Item().AlignCenter().Text("(CAP 344 Laws Of The Federation of Nigeria 1990)")
                    .FontSize(12).FontColor(Colors.Black).Bold();

                col.Item().Height(15);

                // APPLICANT INFORMATION
                col.Item().Element(Header).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (model.applicants != null && model.applicants.Count > 0)
                {
                    foreach (var applicant in model.applicants)
                    {
                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ("Name:",         F(applicant?.Name)),
                            ("Email:",        F(applicant?.Email)),
                            ("Phone number:", F(applicant?.Phone)),
                            ("State:",        F(applicant?.State)),
                            ("Address:",      F(applicant?.Address)),
                            ("Nationality:",  F(applicant?.country))
                        });
                    }
                }
                else
                {
                    TwoColumnSection(col, string.Empty, new[]
                    {
                        ("Name:",         "N/A"),
                        ("Email:",        "N/A"),
                        ("Phone number:", "N/A"),
                        ("State:",        "N/A"),
                        ("Address:",      "N/A"),
                        ("Nationality:",  "N/A")
                    });
                }


                // Determine Renewal Due Date and Next Renewal Date (custom logic)
                string renewalDueDateStr = "N/A";
                string nextRenewalDateStr = "N/A";

                // Get most recent LicenseRenewal year from ApplicationHistory
                var renewalApps = model.ApplicationHistory?
                    .Where(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal)
                    .OrderByDescending(a => a.ApplicationDate)
                    .ToList();

                if (renewalApps != null && renewalApps.Count > 0)
                {
                    int renewalYear = renewalApps.First().ApplicationDate.Year;
                    string monthDay = null;

                    if (model.PatentType is PatentTypes.Conventional or PatentTypes.PCT)
                    {
                        var firstPriorityDateStr = model.FirstPriorityInfo?.FirstOrDefault()?.Date;
                        if (!string.IsNullOrWhiteSpace(firstPriorityDateStr) &&
                            DateTime.TryParse(firstPriorityDateStr, out var firstPriorityDate))
                        {
                            monthDay = $"{firstPriorityDate:MM-dd}";
                        }
                    }
                    else // Non-conventional
                    {
                        if (model.FilingDate.HasValue)
                        {
                            monthDay = $"{model.FilingDate.Value:MM-dd}";
                        }
                        else
                        {
                            monthDay = $"{model.DateCreated:MM-dd}";
                        }
                    }

                    if (monthDay != null)
                    {
                        if (DateTime.TryParse($"{renewalYear}-{monthDay}", out var dueDate))
                        {
                            renewalDueDateStr = dueDate.ToString("dd MMMM, yyyy");
                            var nextRenewalDate = dueDate.AddYears(1);
                            nextRenewalDateStr = nextRenewalDate.ToString("dd MMMM, yyyy");
                        }
                    }
                }
                else
                {
                    // If FirstPriorityInfo is null or no renewal apps, both fields are N/A
                    renewalDueDateStr = "N/A";
                    nextRenewalDateStr = "N/A";
                }

                // RENEWAL INFORMATION
                col.Item().Element(Header).Text("RENEWAL INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Renewal Due Date:", renewalDueDateStr),
                    ("Next Renewal Date:", nextRenewalDateStr)
                });

                // PATENT INFORMATION
                col.Item().Element(Header).Text("PATENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title Of Invention:", F(model.TitleOfInvention));
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Application Type:", F(model.PatentApplicationType)),
                    ("Patent type:",       $"{F(model.PatentType)} - {F(model.FileOrigin)}")
                });
                FullWidthBox(col, "Abstract:", F(model.PatentAbstract));

                col.Item().Height(15);

                col.Item().Row(row =>
                {
                    // Left side: Text block
                    row.RelativeItem().Column(colLeft =>
                    {
                        colLeft.Item().Text("Sealed at my direction,").Bold().FontSize(12);
                        colLeft.Item().Row(r =>
                        {
                            r.AutoItem().Text("Renewal Filing Date").Bold().FontSize(12).FontColor(Colors.Red.Darken2);
                            r.AutoItem().Text($": {model.DateCreated:dd MMMM, yyyy}").FontSize(12);
                        });
                        colLeft.Item().Text("Jane Igwe").Bold().FontSize(12);
                        colLeft.Item().Text("Registrar,").Bold().FontSize(12);
                        colLeft.Item().Text("Patent and Design Registry,").Bold().FontSize(12);
                        colLeft.Item().Text("Federal Ministry of Industry, Trade and Investment").Bold().FontSize(12);
                        colLeft.Item().Text("Federal Capital Territory.").Bold().FontSize(12);
                    });

                    // Right side: QR code and logo
                    row.ConstantItem(70).Column(colRight =>
                    {
                        colRight.Item().AlignCenter().Element(GetQrCode);
                        colRight.Item().Height(5);
                        colRight.Item().AlignCenter().Height(60).Width(60).Image("assets/commeciallawdepartmentlogo.png").FitArea();
                    });
                });
            });
        }

        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            for (int i = 0; i < pairs.Length; i += 2)
            {
                col.Item().Row(row =>
                {
                    // Left cell
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(pairs[i].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });

                    // Right cell (if any)
                    if (i + 1 < pairs.Length)
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(pairs[i + 1].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
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
                    c2.Item().Text(label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                WriteText(c2.Item(), value);
            });
        }
    }
}
