using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignCtcAcknowledgement : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly string appId;

        public DesignCtcAcknowledgement(Filling model, string url, Receipt receipt, string appId)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
            this.appId = appId;
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
                    .Text("DESIGN CTC ACKNOWLEDGEMENT LETTER")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:",   F(receipt.PaymentFor)),
                });

                // CTC post-reg data
                var ctcRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Design CTC Recordal" && p.Id == appId);

                if (ctcRecordal != null)
                {
                    DisplayCtcInformation(col, ctcRecordal);
                }
                else
                {
                    TwoColumnSection(col, "DOCUMENT INFORMATION", new[]
                    {
                        ("Application Type:", "Certified True Copy"),
                        ("Status:", "Pending document verification"),
                    });
                }

                // DESIGN INFORMATION
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

                col.Item().AlignCenter().PaddingTop(30)
                    .Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken3);
            });
        }

        private void DisplayCtcInformation(ColumnDescriptor col, PostRegistrationApp ctcRecordal)
        {
            col.Item().Element(Header).Text("DOCUMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            // Show requested attachments/documents
            if (ctcRecordal.RequestedAttachments != null && ctcRecordal.RequestedAttachments.Any())
            {
                var documentTypes = ctcRecordal.RequestedAttachments.Select(GetDocumentTypeName);
                FullWidthBox(col, "Application Type:", "Certified True Copy");
                FullWidthBox(col, "Document Type", string.Join(", ", documentTypes));
            }
            else
            {
                FullWidthBox(col, "Requested Documents:", "All available design documents");
                FullWidthBox(col, "Document Type:", "Complete design file certification");
            }

        }

        private string GetDocumentTypeName(string attachmentId)
        {
            // Convert attachment IDs to readable document names
            return attachmentId?.ToLower() switch
            {
                "design_drawings" => "Design Drawings",
                "design_specification" => "Design Specification",
                "statement_of_novelty" => "Statement of Novelty",
                "priority_documents" => "Priority Documents",
                "assignment_deed" => "Assignment Deed",
                "power_of_attorney" => "Power of Attorney",
                "application_form" => "Application Form",
                "design_certificate" => "Design Certificate",
                "design_views" => "Design Views",
                "design_description" => "Design Description",
                "representation_sheet" => "Representation Sheet",
                "locarno_classification" => "Locarno Classification",
                _ => attachmentId ?? "Design Document"
            };
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
