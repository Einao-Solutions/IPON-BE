using System;
using System.Linq;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class AcceptanceModelDesign : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly byte[] signatureUrl;
        private readonly List<byte[]> images;
        private readonly string examinerName;
        private readonly DateTime? approvalDate;

        public AcceptanceModelDesign(
            Filling model,
            string url,
            byte[] signatureUrl,
            List<byte[]> images,
            string examinerName,
            DateTime? approvalDate = null)
        {
            this.model = model;
            this.url = url;
            this.signatureUrl = signatureUrl;
            this.images = images ?? new();
            this.examinerName = examinerName;
            this.approvalDate = approvalDate;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().AlignRight().Height(30).Image("assets/ministry.png").FitArea();
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

        private void ComposeContent(IContainer container)
        {
            var history = model.ApplicationHistory?.FirstOrDefault();
            var acceptanceDate = approvalDate ?? model.DateCreated;

            container.Column(col =>
            {
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN ACCEPTANCE LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "FILE INFORMATION", new[]
                {
                    ("Filing date:",    F(history?.ApplicationDate ?? model.FilingDate ?? model.DateCreated)),
                    ("File number:",    F(model.FileId)),
                    ("Payment ID:",     F(history?.PaymentId))
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
                            ("Nationality:", F(applicant?.country)),
                            ("Address:", F(applicant?.Address)),
                            ("State:", F(applicant?.State))
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
                        ("Nationality:", "N/A"),
                        ("Address:", "N/A"),
                        ("State:", "N/A")
                    });
                }

                col.Item().Element(Header).Text("DESIGN CREATORS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                var creators = model.DesignCreators ?? new();
                if (creators.Count > 0)
                {
                    var idx = 1;
                    foreach (var creator in creators)
                    {
                        RenderCreatorEntry(col, idx++, creator);
                    }
                }
                else
                {
                    RenderCreatorEntry(col, 1, null);
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
                FullWidthBox(col, "Examiners Name:", F(examinerName));
                FullWidthBox(col, "Acceptance Date:", F(acceptanceDate));

                if (images.Count > 0)
                {
                    col.Item().Element(Header).Text("DESIGN REPRESENTATIONS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    foreach (var image in images)
                    {
                        var questImage = Image.FromBinaryData(image);
                        col.Item().Height(120).AlignCenter().Image(questImage).FitArea();
                    }
                }

                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN ACCEPTED AND CERTIFICATE IS BEING PROCESSED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);

                if (signatureUrl?.Length > 0)
                {
                    var signatureImage = Image.FromBinaryData(signatureUrl);
                    col.Item().Height(50).AlignCenter().Image(signatureImage).FitArea();
                }

                if (approvalDate.HasValue)
                {
                    col.Item().AlignCenter().Text($"Approval Date: {approvalDate:dd MMMM, yyyy}")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10);
                }

                col.Item().AlignCenter().Height(80).Element(GetQrCode);
            });
        }

        private static void RenderCreatorEntry(ColumnDescriptor col, int index, ApplicantInfo? creator)
        {
            col.Item().Element(Box).Column(c2 =>
            {
                c2.Item().Text(t =>
                {
                    t.Span($"{index}. Name: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.Name)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
                c2.Item().Text(t =>
                {
                    t.Span("Email: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.Email)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
                c2.Item().Text(t =>
                {
                    t.Span("Phone number: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.Phone)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
                c2.Item().Text(t =>
                {
                    t.Span("Nationality: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.country)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
                c2.Item().Text(t =>
                {
                    t.Span("Address: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.Address)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
                c2.Item().Text(t =>
                {
                    t.Span("State: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                    t.Span(F(creator?.State)).FontFamily(Fonts.TimesNewRoman).FontSize(10);
                });
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

        private void GetQrCode(IContainer container)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeImage = qrCode.GetGraphic(20);
            container.Image(qrCodeImage).FitArea();
        }
    }
}
