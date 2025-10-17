using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Linq;

namespace patentdesign
{
    public class PatentRenewalAcknowlegementLetter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;

        public PatentRenewalAcknowlegementLetter(Filling model, string url, Receipt receipt)
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
                // Header (logo & titles)
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("PATENT RENEWAL ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                // PAYMENT INFORMATION
                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Payment date:", F(date)),
                    ("Payment ID:",       F(receipt.rrr)),
                    ("File number:",      F(model.FileId)),
                    ("Fee title:",        F(receipt.PaymentFor)),
                });

                //========================================================================
                // RENEWAL INFORMATION

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

                // Only show priority sections if PatentType is NOT Non_Conventional
                if (model.PatentType != PatentTypes.Non_Conventional)
                {
                    // FIRST PRIORITY INFO
                    var firstPriorityList = model.FirstPriorityInfo ?? new();
                    if (firstPriorityList.Count > 0)
                    {
                        col.Item().Element(Header).Text("FIRST PRIORITY INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        for (int i = 0; i < firstPriorityList.Count; i++)
                            RenderPriorityEntry(col, i + 1, firstPriorityList[i]);
                    }
                    // PRIORITY INFO
                    var priorityList = model.PriorityInfo ?? new();
                    if (priorityList.Count > 0)
                    {
                        col.Item().Element(Header).Text("ADDITIONAL PRIORITY INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        for (int i = 0; i < priorityList.Count; i++)
                            RenderPriorityEntry(col, i + 1, priorityList[i]);
                    }
                }

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

                //PATENT INVENTORS
                col.Item().Element(Header).Text("PATENT INVENTORS")
                .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                if (model.Inventors != null && model.Inventors.Count > 0)
                {
                    int inventorCount = 1;
                    foreach (var inv in model.Inventors)
                    {
                        col.Item().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(t =>
                            {
                                t.Span($"{inventorCount}. Name: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                                t.Span(F(inv?.Name)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                            });

                            c2.Item().Text(t =>
                            {
                                t.Span("Email: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                                t.Span(F(inv?.Email)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                            });

                            c2.Item().Text(t =>
                            {
                                t.Span("Phone number: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                                t.Span(F(inv?.Phone)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                            });

                            c2.Item().Text(t =>
                            {
                                t.Span("Nationality: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                                t.Span(F(inv?.country)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                            });

                            c2.Item().Text(t =>
                            {
                                t.Span("Address: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                                t.Span(F(inv?.Address)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                            });
                        });

                        inventorCount++;
                    }
                }
                else
                {
                    col.Item().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(t =>
                        {
                            t.Span("1. Name: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            t.Span("N/A").FontFamily(Fonts.TimesNewRoman).FontSize(10);
                        });

                        c2.Item().Text(t =>
                        {
                            t.Span("Email: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            t.Span("N/A").FontFamily(Fonts.TimesNewRoman).FontSize(10);
                        });

                        c2.Item().Text(t =>
                        {
                            t.Span("Phone number: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            t.Span("N/A").FontFamily(Fonts.TimesNewRoman).FontSize(10);
                        });

                        c2.Item().Text(t =>
                        {
                            t.Span("Nationality: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            t.Span("N/A").FontFamily(Fonts.TimesNewRoman).FontSize(10);
                        });

                        c2.Item().Text(t =>
                        {
                            t.Span("Address: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            t.Span("N/A").FontFamily(Fonts.TimesNewRoman).FontSize(10);
                        });
                    });
                }

                /*???????? CORRESPONDENCE INFORMATION ?????????*/
                TwoColumnSection(col, "CORRESPONDENCE INFORMATION", new[]
                {
                    ("Name:",          F(model.Correspondence.name)),
                    ("Email:",         F(model.Correspondence.email)),
                    ("Phone Number:",  F(model.Correspondence.phone)),
                    ("State:",         F(model.Correspondence.state)),
                    ("Address:",       F(model.Correspondence.address)),
                    ("Nationality:",   F(model.Correspondence.Nationality))
                });

                // Footer note
                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);
            });
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

        private static void RenderPriorityEntry(ColumnDescriptor col, int index, PriorityInfo? p)
        {
            col.Item().Row(r =>
            {
                r.RelativeItem().Element(Box).Column(c2 =>
                {
                    c2.Item().Text($"{index}. Application Number:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    WriteText(c2.Item(), F(p?.number));
                });
                r.RelativeItem().Element(Box).Column(c2 =>
                {
                    c2.Item().Text("Country:").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    WriteText(c2.Item(), F(p?.Country));
                });
            });

            FullWidthBox(col, "Date:", F(p?.Date));
        }
    }
}
