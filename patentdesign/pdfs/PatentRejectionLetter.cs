using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Linq;

namespace patentdesign
{
    /// <summary>
    /// Generates the Patent Rejection Letter PDF in the same style as the acceptance letter.
    /// </summary>
    public class RejectionModelPatent : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly byte[] signatureUrl;
        private readonly string examinerName;

        public RejectionModelPatent(
            Filling model,
            string url,
            byte[] signatureUrl,
            string examinerName)
        {
            this.model = model;
            this.url = url;
            this.signatureUrl = signatureUrl;
            this.examinerName = examinerName;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

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

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                // Header (logo & titles)
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("PATENT REFUSAL LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken2).ExtraBold();
                col.Item().Height(10);

                // FILE INFORMATION
                TwoColumnSection(col, "FILE INFORMATION", new[]
                {
                    ("Filing date:",      F(model.ApplicationHistory?.FirstOrDefault()?.ApplicationDate)),
                    ("File number:",      F(model.FileId)),
                    ("Payment date:",     "N/A"),
                    ("Payment ID:",       model.ApplicationHistory != null && model.ApplicationHistory.Count > 0 ? F(model.ApplicationHistory[0].PaymentId) : "N/A")
                });


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

                // PATENT INFORMATION
                col.Item().Element(Header).Text("PATENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title Of Invention:", F(model.TitleOfInvention));
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Application Type:", F(model.PatentApplicationType)),
                    ("Patent type:",       $"{F(model.PatentType)} - {F(model.FileOrigin)}")
                });
                FullWidthBox(col, "Abstract:", F(model.PatentAbstract));


                // Abstract full width
                FullWidthBox(col, "Abstract:", F(model.PatentAbstract));

                // Only show priority sections if PatentType is NOT Non_Conventional
                if (model.PatentType != PatentTypes.Non_Conventional)
                {
                    // FIRST PRIORITY INFO (only render if not empty)
                    var firstPriorityList = model.FirstPriorityInfo ?? new();
                    if (firstPriorityList.Count > 0)
                    {
                        col.Item().Element(Header).Text("FIRST PRIORITY INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        for (int i = 0; i < firstPriorityList.Count; i++)
                            RenderPriorityEntry(col, i + 1, firstPriorityList[i]);
                    }

                    // PRIORITY INFO (only render if not empty)
                    var priorityList = model.PriorityInfo ?? new();
                    if (priorityList.Count > 0)
                    {
                        col.Item().Element(Header).Text("PRIORITY INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        for (int i = 0; i < priorityList.Count; i++)
                            RenderPriorityEntry(col, i + 1, priorityList[i]);
                    }
                }


                // PATENT INVENTORS
                col.Item().Element(Header).Text("PATENT INVENTORS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                if (model.Inventors != null && model.Inventors.Count > 0)
                {
                    int inventorCount = 1;
                    foreach (var inv in model.Inventors)
                    {
                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ($"{inventorCount}. Name:", F(inv?.Name)),
                            ("Email:",        F(inv?.Email)),
                            ("Phone number:", F(inv?.Phone)),
                            ("Nationality:",  F(inv?.country))
                        });
                        FullWidthBox(col, "Address:", F(inv?.Address));
                        inventorCount++;
                    }
                }
                else
                {
                    TwoColumnSection(col, string.Empty, new[]
                    {
                        ("Name:",         "N/A"),
                        ("Email:",        "N/A"),
                        ("Phone number:", "N/A"),
                        ("Nationality:",  "N/A"),
                        ("Address:",      "N/A"),
                        ("", "")
                    });
                }

                // DRAWING
                if (model.Attachments != null && model.Attachments.Any(a => a.name == "patentDrawing"))
                {
                    col.Item().Element(Header).Text("DRAWING")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                    FullWidthBox(col, "Drawing:", "Attached");
                }

                // ATTACHMENTS
                col.Item().Element(Header).Text("ATTACHMENTS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                if (model.Attachments != null && model.Attachments.Count > 0)
                {
                    int attCount = 1;
                    foreach (var att in model.Attachments)
                    {
                        string name = !string.IsNullOrWhiteSpace(att.name) ? att.name : $"Attachment {attCount}";
                        FullWidthBox(col, "Name:", name);
                        attCount++;
                    }
                }
                else
                {
                    FullWidthBox(col, "Name:", "N/A");
                }

                // PROCESS INFORMATION (new section)
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    // Header row
                    table.Cell().ColumnSpan(2)
                        .Element(Header)
                        .Text("PROCESS INFORMATION")
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Bold();

                    // Find the first status history entry with afterStatus == ApplicationStatuses.Rejected
                    string refusalDate = "N/A";
                    string reasonForRefusal = "N/A";
                    string examinerName = "N/A";

                    if (model.ApplicationHistory != null)
                    {
                        var rejectedStatus = model.ApplicationHistory
                            .SelectMany(appInfo => appInfo.StatusHistory ?? Enumerable.Empty<ApplicationHistory>())
                            .FirstOrDefault(status =>
                                status.afterStatus == ApplicationStatuses.Rejected
                            );

                        if (rejectedStatus != null)
                        {
                            reasonForRefusal = rejectedStatus.Message ?? "N/A";
                            examinerName = rejectedStatus.User ?? "N/A";
                            refusalDate = rejectedStatus.Date != default
                                ? rejectedStatus.Date.ToString("dd MMMM, yyyy")
                                : "N/A";
                        }
                    }

                    // Refusal Date row
                    table.Cell().Element(Box)
                        .Text("REFUSAL DATE")
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12);
                    table.Cell().Element(Box)
                        .Text(refusalDate)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Italic(refusalDate == "N/A")
                        .FontColor(Colors.Black);

                    // Reason for Refusal row
                    table.Cell().Element(Box)
                        .Text("REASON FOR REFUSAL")
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12);
                    table.Cell().Element(Box)
                        .Text(reasonForRefusal)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Italic(reasonForRefusal == "N/A")
                        .FontColor(Colors.Black);

                    // Examiner Name row
                    table.Cell().Element(Box)
                        .Text("EXAMINER NAME")
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12);
                    table.Cell().Element(Box)
                        .Text(examinerName)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(12)
                        .Italic(examinerName == "N/A")
                        .FontColor(Colors.Black);
                });

                // Footer note
                col.Item().AlignCenter().PaddingTop(30).PaddingBottom(30).Text("YOUR APPLICATION HAS BEEN REJECTED")
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