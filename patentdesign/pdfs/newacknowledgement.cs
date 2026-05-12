using System;
using System.Collections.Generic;
using System.Linq;
using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using IContainer = QuestPDF.Infrastructure.IContainer;

namespace patentdesign
{
    public class AcknowledgementModelDesign : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly List<byte[]> images;
        private readonly string paymentDate;

        public AcknowledgementModelDesign(Filling model, string url, List<byte[]> images, string paymentDate)
        {
            this.model = model;
            this.url = url;
            this.images = images ?? new List<byte[]>();
            this.paymentDate = paymentDate;
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

            container.Column(col =>
            {
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/ministry.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(paymentDate)),
                    ("Payment RRR:", F(history?.PaymentId)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:", F(history?.ApplicationType))
                });

                col.Item().Element(Header).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title of Industrial Design:", F(model.TitleOfDesign));
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Design type:", F(model.DesignType)),
                    ("Filing origin:", F(model.FileOrigin ?? model.FilingCountry))
                });
                FullWidthBox(col, "Representation:", HasAttachment("designs") ? "Attached" : "Not attached");
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                col.Item().Element(Header).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                RenderApplicants(col);

                col.Item().Element(Header).Text("DESIGN CREATORS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                RenderDesignCreators(col);

                col.Item().Element(Header).Text("DESIGN REPRESENTATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (images.Count > 0)
                {
                    RenderDesignImages(col);
                }
                else
                {
                    FullWidthBox(col, "Design Representation(s):", "Not Attached");
                }

                //col.Item().Element(Header).Text("DOCUMENTS ATTACHED").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                //RenderAttachmentStatus(col, "Priority Document", HasAttachment("pdoc"));
                //RenderAttachmentStatus(col, "Novelty Statement", HasAttachment("nov"));
                //RenderAttachmentStatus(col, "Design Representation(s)", HasAttachment("designs"));
                //RenderAttachmentStatus(col, "Power of Attorney", HasAttachment("form2"));

                //col.Item().Element(Header).Text("ALL ATTACHMENTS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                //RenderAllAttachments(col);

                col.Item().Element(Header).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Name:", F(model.Correspondence?.name)),
                    ("Phone number:", F(model.Correspondence?.phone)),
                    ("Email:", F(model.Correspondence?.email)),
                    ("Nationality:", F(model.Correspondence?.Nationality)),
                    ("Address:", F(model.Correspondence?.address)),
                    ("State:", F(model.Correspondence?.state))
                });


                // Abbreviation mapping for design attachments
                var attachmentAbbr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    {"pdoc", "PD"},
                    {"priorityDocument", "PD"},
                    {"form2", "POA"},
                    {"poa", "POA"},
                    {"nov", "NOV"},
                    {"novelty", "NOV"},
                    {"noveltyStatement", "NOV"},
                    {"statementOfNovelty", "NOV"},
                    {"cs", "CS"},
                    {"any", "OTH"},
                    {"others", "OTH"},
                    {"designs", "DES"},
                    {"design1", "DES"},
                    {"design2", "DES"},
                    {"design3", "DES"},
                    {"design4", "DES"},
                    {"designDrawings", "DES"},
                };

                var attachmentList = new List<string>();
                if (model.Attachments != null)
                {
                    foreach (var att in model.Attachments)
                    {
                        string displayName;
                        if (string.IsNullOrWhiteSpace(att.name))
                        {
                            displayName = "Unknown";
                        }
                        else if (attachmentAbbr.TryGetValue(att.name, out var abbr))
                        {
                            displayName = abbr;
                        }
                        else
                        {
                            displayName = att.name;
                        }
                        attachmentList.Add(displayName);
                    }
                }

                if (attachmentList.Count > 0)
                {
                    col.Item().Element(Header).Text("ATTACHMENTS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    var pairs = new List<(string, string)>();
                    int idx = 1;
                    foreach (var abbr in attachmentList)
                    {
                        pairs.Add(($"Attachment {idx}:", abbr));
                        idx++;
                    }
                    TwoColumnSection(col, string.Empty, pairs);
                }

                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);
              
            });
        }

        private void RenderApplicants(ColumnDescriptor col)
        {
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
        }

        private void RenderDesignCreators(ColumnDescriptor col)
        {
            if (model.DesignCreators != null && model.DesignCreators.Count > 0)
            {
                int idx = 1;
                foreach (var creator in model.DesignCreators)
                {
                    col.Item().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(t =>
                        {
                            t.Span($"{idx}. Name: ").FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
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
                    });
                    idx++;
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
        }

        private void RenderAttachmentStatus(ColumnDescriptor col, string label, bool attached)
        {
            FullWidthBox(col, label + ":", attached ? "Attached" : "Not Attached");
        }

        private void RenderAllAttachments(ColumnDescriptor col)
        {
            if (model.Attachments != null && model.Attachments.Count > 0)
            {
                int count = 1;
                foreach (var att in model.Attachments)
                {
                    string name = !string.IsNullOrWhiteSpace(att.name) ? att.name : $"Attachment {count}";
                    FullWidthBox(col, $"Attachment {count}:", name);
                    count++;
                }
            }
            else
            {
                FullWidthBox(col, "Attachment:", "N/A");
            }
        }

        private void RenderDesignImages(ColumnDescriptor col)
        {
            if (images.Count > 0)
            {
                foreach (var img in images)
                {
                    try
                    {
                        var questImage = Image.FromBinaryData(img);
                        col.Item().Height(120).AlignCenter().Image(questImage).FitArea();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to render design image: {ex.Message}");
                        col.Item().Element(Box).Text("[Image could not be rendered]").FontFamily(Fonts.TimesNewRoman).FontSize(10).Italic();
                    }
                }
            }
            else
            {
                FullWidthBox(col, string.Empty, "No design representations attached.");
            }
        }

        private bool HasAttachment(string key)
        {
            if (model.Attachments == null) return false;

            // Map of old keys to possible attachment names in database
            var nameVariations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "pdoc", new[] { "pdoc", "designPriorityDocument", "priorityDocument", "priority" } },
                { "nov", new[] { "nov", "noveltyStatement", "novelty", "statement" } },
                { "form2", new[] { "form2", "powerOfAttorney", "poa", "attorney" } },
                { "cs", new[] { "cs", "claimsSpecifications", "claims", "specifications" } },
                { "any", new[] { "any", "other", "otherAttachments", "additionalDocuments" } }
            };

            // Check if any variation of the key exists in attachments
            if (nameVariations.TryGetValue(key, out var variations))
            {
                return model.Attachments.Any(a =>
                    variations.Any(v => string.Equals(a.name, v, StringComparison.OrdinalIgnoreCase)));
            }

            // Fallback to exact match
            return model.Attachments.Any(a => string.Equals(a.name, key, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetMatchedAttachmentNames(string key)
        {
            if (model.Attachments == null) return new List<string>();

            var nameVariations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "pdoc", new[] { "pdoc", "designPriorityDocument", "priorityDocument", "priority" } },
                { "nov", new[] { "nov", "noveltyStatement", "novelty", "statement" } },
                { "form2", new[] { "form2", "powerOfAttorney", "poa", "attorney" } },
                { "cs", new[] { "cs", "claimsSpecifications", "claims", "specifications" } },
                { "any", new[] { "any", "other", "otherAttachments", "additionalDocuments" } }
            };

            if (nameVariations.TryGetValue(key, out var variations))
            {
                return model.Attachments
                    .Where(a => a.name != null && variations.Any(v => string.Equals(a.name, v, StringComparison.OrdinalIgnoreCase)))
                    .Select(a => a.name)
                    .ToList();
            }

            return model.Attachments
                .Where(a => string.Equals(a.name, key, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.name)
                .ToList();
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (i + 1 < pairs.Length)
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(pairs[i].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            WriteText(c2.Item(), pairs[i].Value);
                        });

                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(pairs[i + 1].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            WriteText(c2.Item(), pairs[i + 1].Value);
                        });
                    });
                }
                else
                {
                    col.Item().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(pairs[i].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });
                }
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
