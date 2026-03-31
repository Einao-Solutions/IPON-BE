using patentdesign.Models;
using patentdesign.Utils;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    /// <summary>
    /// IPO Nigeria branded publication journal document — newspaper style.
    /// Design: forest-green accents, Georgia serif body, double-column layout,
    /// paragraph-format publication details (editorial/magazine style).
    /// Brand: iponigeria.com — Federal Ministry of Industry, Trade & Investment
    /// </summary>
    public class JournalDocumentNewspaper(
        List<PublicationInfo> models,
        FileTypes type,
        DateTime start,
        DateTime end) : IDocument
    {
        // ── Brand tokens ──────────────────────────────────────────────────────
        private const string GreenDark = "#1E5631";
        private const string GreenLight = "#EAF3EA";
        private const string GreenBorder = "#C2D4C2";
        private const string GreenMuted = "#5A7A5A";
        private const string GreenFaint = "#E8F0E8";
        private const string TextDark = "#1A3A1A";
        private const string TextBody = "#2A2A2A";
        private const string White = "#FFFFFF";

        // ── Labels ────────────────────────────────────────────────────────────
        private string TitleLabel => type switch
        {
            FileTypes.Design => "Title of Design",
            FileTypes.Patent => "Title of Invention",
            _ => "Title"    
        };
        private string CreatorLabel => type switch
        {
            FileTypes.Design => "Design Creators",
            FileTypes.Patent => "Patent Inventors",
            _ => "Inventors"
        };
        private string TypeLabel => type switch
        {
            FileTypes.Design => "Design",
            FileTypes.Patent => "Patent",
            FileTypes.TradeMark => "Trademark",
            _ => type.ToString()
        };

        // ── IDocument ─────────────────────────────────────────────────────────
        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeBody);
                page.Footer().Element(ComposeFooter);
            });
        }

        // ── Page header ───────────────────────────────────────────────────────
        void ComposeHeader(IContainer container)
        {
            container
                .BorderBottom(3).BorderColor(GreenDark)
                .Row(header =>
                {
                    header.ConstantItem(8).Background(GreenDark);

                    header.RelativeItem()
                        .Padding(18).PaddingLeft(20).PaddingRight(24)
                        .Row(inner =>
                        {
                            // Coat of arms
                            inner.ConstantItem(50).AlignMiddle()
                                .Width(44).Height(44)
                                .AlignCenter().AlignMiddle()
                                .Image("assets/Commeciallawdepartmentlogo.png")
                                .FitArea();

                            // Wordmark
                            inner.RelativeItem().PaddingLeft(12).AlignMiddle().Column(wm =>
                            {
                                wm.Item().Text("IPO Nigeria")
                                    .FontSize(15).Bold().FontColor(GreenDark).FontFamily("Georgia");
                                wm.Item().PaddingTop(2)
                                    .Text("Federal Ministry of Industry, Trade & Investment")
                                    .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                                wm.Item().Text("Commercial Law Department")
                                    .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                            });

                            // Document meta
                            inner.ConstantItem(210).AlignMiddle().Column(meta =>
                            {
                                meta.Item().AlignRight()
                                    .Text($"{TypeLabel} Publications Journal")
                                    .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");
                                meta.Item().PaddingTop(3).AlignRight()
                                    .Text($"{start:d MMMM yyyy} — {end:d MMMM yyyy}")
                                    .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                                meta.Item().PaddingTop(2).AlignRight()
                                    .Text($"Generated: {DateTime.Now:d MMM yyyy}")
                                    .FontSize(8).FontColor(GreenBorder).FontFamily("Arial");
                            });
                        });
                });
        }

        // ── Page footer ───────────────────────────────────────────────────────
        void ComposeFooter(IContainer container)
        {
            container
                .BorderTop(2).BorderColor(GreenDark)
                .Padding(8).PaddingLeft(20).PaddingRight(20)
                .Row(row =>
                {
                    row.RelativeItem().Row(left =>
                    {
                        left.ConstantItem(3).Background(GreenDark);
                        left.RelativeItem().PaddingLeft(8).AlignMiddle()
                            .Text("IPO Nigeria  ·  iponigeria.com  ·  Commercial Law Department")
                            .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                    });

                    row.ConstantItem(80).AlignRight().AlignMiddle().Text(text =>
                    {
                        text.Span("Page ").FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                        text.CurrentPageNumber().FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                        text.Span(" of ").FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                        text.TotalPages().FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                    });
                });
        }

        // ── Body — Double-column layout ───────────────────────────────────────
        void ComposeBody(IContainer container)
        {
            container.Background(White).Padding(24).Column(col =>
            {
                // Distribute publications across two columns
                var midpoint = (models.Count + 1) / 2;
                var leftColumn = models.Take(midpoint).ToList();
                var rightColumn = models.Skip(midpoint).ToList();

                col.Item()
                    .Row(columns =>
                    {
                        // Left column
                        columns.RelativeItem()
                            .PaddingRight(16)
                            .Column(leftCol =>
                            {
                                leftCol.Spacing(20);
                                foreach (var model in leftColumn)
                                {
                                    var index = models.IndexOf(model) + 1;
                                    leftCol.Item().Element(c => ComposeEntryNewspaper(c, model, index));
                                }
                            });

                        // Right column
                        columns.RelativeItem()
                            .PaddingLeft(16)
                            .Column(rightCol =>
                            {
                                rightCol.Spacing(20);
                                foreach (var model in rightColumn)
                                {
                                    var index = models.IndexOf(model) + 1;
                                    rightCol.Item().Element(c => ComposeEntryNewspaper(c, model, index));
                                }
                            });
                    });
            });
        }

        // ── Entry card — Newspaper style ──────────────────────────────────────
        void ComposeEntryNewspaper(IContainer container, PublicationInfo model, int index)
        {
            container.Column(card =>
            {
                // Headline with publication number and date
                card.Item()
                    .BorderBottom(2).BorderColor(GreenDark)
                    .PaddingBottom(8)
                    .Row(headlineRow =>
                    {
                            
                        if (model.Representation is { Length: > 0 })
                        {
                            headlineRow.ConstantItem(60)
                                .PaddingRight(10)
                                .Height(50)
                                .AlignCenter().AlignMiddle()
                                .Border(0.5f).BorderColor(GreenBorder)
                                .Background(GreenFaint)
                                .Image(model.Representation)
                                .FitArea();
                        }

                        // Title and publication info
                        headlineRow.RelativeItem().Column(headline =>
                        {
                            headline.Item()
                                .Text(model.Title)
                                .FontSize(14).Bold().FontColor(TextDark).FontFamily("Georgia");

                            headline.Item().PaddingTop(4)
                                .Text(text =>
                                {
                                    text.Span($"{model.FileNumber}  ·  ")
                                        .FontSize(9).FontColor(GreenDark).FontFamily("Arial");
                                    text.Span(model.PublicationDate.ToString("d MMMM yyyy"))
                                        .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                                });
                        });
                    });

                // Body text — Details in paragraph format
                card.Item().PaddingTop(10)
                    .Column(body =>
                    {
                        // Type and File Number paragraph
                        body.Item()
                            .Text(text =>
                            {
                                text.Justify();
                                text.Span($"Class: {model.Class} - ")
                                    .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");

                                text.Span(model.ClassDescription)
                                    .FontSize(9).FontColor(TextBody).FontFamily("Georgia");
                            });

                        // Applicants section
                        if (model.Applicants?.Count > 0)
                        {
                            body.Item().PaddingTop(10)
                                .Text(text =>
                                {
                                    text.Justify();
                                    text.Span("APPLICANTS: ")
                                        .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                                    text.Span(string.Join("; ", model.Applicants
                                        .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                                        .Select(a => FormatPersonForParagraph(a))))
                                        .FontSize(9).FontColor(TextBody).FontFamily("Georgia");
                                });
                        }

                        // Inventors section
                        if (model.Inventors?.Count > 0)
                        {
                            body.Item().PaddingTop(8)
                                .Text(text =>
                                {
                                    text.Justify();
                                    text.Span($"{CreatorLabel.ToUpper()}: ")
                                        .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                                    text.Span(string.Join("; ", model.Inventors
                                        .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                                        .Select(i => FormatPersonForParagraph(i))))
                                        .FontSize(9).FontColor(TextBody).FontFamily("Georgia");
                                });
                        }

                        // Correspondence section
                        if (model.Correspondence is not null && !string.IsNullOrWhiteSpace(model.Correspondence.name))
                        {
                            body.Item().PaddingTop(8)
                                .Text(text =>
                                {
                                    text.Justify();
                                    text.Span("CORRESPONDENCE: ")
                                        .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");
                                    text.Span(FormatCorrespondenceForParagraph(model.Correspondence))
                                        .FontSize(9).FontColor(TextBody).FontFamily("Georgia");
                                });
                        }

                    });
            });
        }

        // ── Paragraph formatting helpers ──────────────────────────────────────
        private static string FormatPersonForParagraph(ApplicantInfo person)
        {
            if (string.IsNullOrWhiteSpace(person.Name))
                return string.Empty;

            var parts = new List<string> { person.Name };

            if (!string.IsNullOrWhiteSpace(person.Phone))
                parts.Add(person.Phone);

            if (!string.IsNullOrWhiteSpace(person.Email))
                parts.Add(person.Email);

            var address = FormatAddress(person.Address, person.country);
            if (!string.IsNullOrWhiteSpace(address))
                parts.Add(address);

            return string.Join(" | ", parts);
        }

        private static string FormatCorrespondenceForParagraph(CorrespondenceType corr)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(corr.name))
            {
                var nameWithState = corr.name;
                if (!string.IsNullOrWhiteSpace(corr.state))
                    nameWithState += $", {corr.state}";
                parts.Add(nameWithState);
            }

            if (!string.IsNullOrWhiteSpace(corr.phone))
                parts.Add(corr.phone);

            if (!string.IsNullOrWhiteSpace(corr.email))
                parts.Add(corr.email);

            if (!string.IsNullOrWhiteSpace(corr.address))
                parts.Add(corr.address);

            return string.Join(" | ", parts);
        }

        private static string FormatAddress(string? address, string? country)
        {
            return string.Join(", ",
                new[] { address, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}