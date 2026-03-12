using System;
using System.Collections.Generic;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class RejectionModelDesign : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly byte[] signatureUrl;
        private readonly List<byte[]> images;
        private readonly string examinerName;

        public RejectionModelDesign(
            Filling model,
            string url,
            byte[] signatureUrl,
            List<byte[]> images,
            string examinerName)
        {
            this.model = model;
            this.url = url;
            this.signatureUrl = signatureUrl ?? Array.Empty<byte>();
            this.images = images ?? new();
            this.examinerName = examinerName;
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

        private void ComposeContent(IContainer container)
        {
            var filingHistory = model.ApplicationHistory?.FirstOrDefault();
            var filingDate = filingHistory?.ApplicationDate ?? model.FilingDate ?? model.DateCreated;
            var paymentId = filingHistory?.PaymentId ?? model.ApplicationHistory?.FirstOrDefault()?.PaymentId;

            container.Column(col =>
            {
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN REJECTION LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken2).ExtraBold();
                col.Item().Height(10);

                var rejectionStatus = model.ApplicationHistory?
                    .SelectMany(app => app.StatusHistory ?? Enumerable.Empty<ApplicationHistory>())
                    .FirstOrDefault(status => status.afterStatus == ApplicationStatuses.Rejected);
                var rejectionDate = rejectionStatus?.Date ?? model.ApplicationHistory?.FirstOrDefault()?.StatusHistory?.FirstOrDefault()?.Date ?? filingDate;

                TwoColumnSection(col, "FILE INFORMATION", new[]
                {
                    ("Filing date:", F(filingDate)),
                    ("File number:", F(model.FileId)),
                    ("Rejection Date:", F(rejectionDate)),
                    ("Payment ID:", F(paymentId))
                });

                col.Item().Element(Header).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Title of Industrial Design:", F(model.TitleOfDesign)),
                    ("File Origin:", F(model.FileOrigin ?? model.FilingCountry)),
                    ("Design type:", F(model.DesignType)),
                    ("Representation:", images.Count > 0 ? "Attached" : "Nil"),
                });
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                col.Item().Element(Header).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (model.applicants != null && model.applicants.Count > 0)
                {
                    foreach (var applicant in model.applicants)
                    {
                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ("Name:", F(applicant?.Name)),
                            ("Email:", F(applicant?.Email)),
                            ("Phone number:", F(applicant?.Phone)),
                            ("State:", F(applicant?.State)),
                            ("Address:", F(applicant?.Address)),
                            ("Nationality:", F(applicant?.country))
                        });
                    }
                }
                else
                {
                    TwoColumnSection(col, string.Empty, new[]
                    {
                        ("Name:", "N/A"),
                        ("Email:", "N/A"),
                        ("Phone number:", "N/A"),
                        ("State:", "N/A"),
                        ("Address:", "N/A"),
                        ("Nationality:", "N/A")
                    });
                }

                col.Item().Element(Header).Text("DESIGN CREATORS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (model.DesignCreators != null && model.DesignCreators.Count > 0)
                {
                    int creatorIndex = 1;
                    foreach (var creator in model.DesignCreators)
                    {
                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ($"{creatorIndex}. Name:", F(creator?.Name)),
                            ("Email:", F(creator?.Email)),
                            ("Phone number:", F(creator?.Phone)),
                            ("Nationality:", F(creator?.country)),
                            ("Address:", F(creator?.Address)),
                            ("State:", F(creator?.State))
                        });
                        creatorIndex++;
                    }
                }
                else
                {
                    TwoColumnSection(col, string.Empty, new[]
                    {
                        ("Name:", "N/A"),
                        ("Email:", "N/A"),
                        ("Phone number:", "N/A"),
                        ("Nationality:", "N/A"),
                        ("Address:", "N/A"),
                        ("State:", "N/A")
                    });
                }

                col.Item().Element(Header).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Name:", F(model.Correspondence?.name)),
                    ("Email:", F(model.Correspondence?.email)),
                    ("Phone number:", F(model.Correspondence?.phone)),
                    ("Nationality:", F(model.Correspondence?.Nationality)),
                    ("Address:", F(model.Correspondence?.address)),
                    ("State:", F(model.Correspondence?.state))
                });

                col.Item().Element(Header).Text("PROCESS INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                var processDetails = model.ApplicationHistory?
                    .SelectMany(app => app.StatusHistory ?? Enumerable.Empty<ApplicationHistory>())
                    .FirstOrDefault(status => status.afterStatus == ApplicationStatuses.Rejected);

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Examiners Name:", F(processDetails?.User ?? examinerName)),
                    ("Reason for Rejection:", F(processDetails?.Message))
                });

                col.Item().AlignCenter().PaddingTop(30).PaddingBottom(20)
                    .Text("YOUR APPLICATION HAS BEEN REJECTED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Red.Darken2);

                //if (signatureUrl.Length > 0)
                //{
                //    var signatureImage = Image.FromBinaryData(signatureUrl);
                //    col.Item().Height(50).AlignCenter().Image(signatureImage).FitArea();
                //}

                //if (!string.IsNullOrWhiteSpace(examinerName))
                //{
                //    col.Item().AlignCenter().Text(examinerName).FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold();
                //}
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

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            for (int i = 0; i < pairs.Length; i += 2)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(pairs[i].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });

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
