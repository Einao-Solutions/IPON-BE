using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    /// <summary>
    /// IPO Nigeria branded publication journal document.
    /// Design: forest-green left accent bar, Georgia serif titles,
    /// two-column field grid, green-rule section headers.
    /// Brand: iponigeria.com — Federal Ministry of Industry, Trade & Investment
    /// </summary>
    public class JournalDocument(
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
        private const string AvatarBg = "#EAF3EA";
        private const string AvatarBorder = "#A8C8A8";
        private const string TextDark = "#1A3A1A";
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
                .BorderBottom(4).BorderColor(GreenDark)
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
                                .Background(GreenLight)
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
                            .Text("IPO Nigeria  ·  iponigeria.com  ·  Commercial Law Department  ·  Confidential")
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

        // ── Body ──────────────────────────────────────────────────────────────
        void ComposeBody(IContainer container)
        {
            container.Background(White).Padding(24).Column(col =>
            {
                col.Spacing(18);
                foreach (var model in models)
                    col.Item().Element(c => ComposeEntry(c, model, models.IndexOf(model) + 1));
            });
        }

        // ── Entry card ────────────────────────────────────────────────────────
        void ComposeEntry(IContainer container, PublicationInfo model, int index)
        {
            container
                .Border(0.5f).BorderColor(GreenBorder)
                .Row(card =>
                {
                    // Left green accent bar
                    card.ConstantItem(5).Background(GreenDark);

                    card.RelativeItem().Column(body =>
                    {
                        // Top bar
                        body.Item()
                            .Background(GreenLight)
                            .BorderBottom(0.5f).BorderColor(GreenBorder)
                            .Padding(10).PaddingLeft(14).PaddingRight(14)
                            .Row(top =>
                            {
                                top.ConstantItem(30).AlignMiddle()
                                    .Width(26).Height(26).Background(GreenDark)
                                    .AlignCenter().AlignMiddle()
                                    .Text($"{index:D2}")
                                    .FontSize(10).Bold().FontColor(White).FontFamily("Arial");

                                top.RelativeItem().PaddingLeft(10).AlignMiddle().Column(d =>
                                {
                                    d.Item().Text("Publication Date")
                                        .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                                    d.Item().Text(model.PublicationDate.ToString("d MMMM yyyy"))
                                        .FontSize(11).Bold().FontColor(GreenDark).FontFamily("Arial");
                                });

                                if (!string.IsNullOrWhiteSpace(model.FileNumber))
                                {
                                    top.ConstantItem(155).AlignMiddle()
                                        .Background(GreenDark)
                                        .Padding(4).PaddingLeft(10).PaddingRight(10)
                                        .Text(model.FileNumber)
                                        .FontSize(9).FontColor(White).FontFamily("Arial");
                                }
                            });

                        // Title block
                        if (!string.IsNullOrWhiteSpace(model.Title))
                        {
                            body.Item()
                                .BorderBottom(0.5f).BorderColor(GreenFaint)
                                .Padding(14).PaddingLeft(14).PaddingRight(14)
                                .Column(t =>
                                {
                                    t.Item().Text(TitleLabel.ToUpper())
                                        .FontSize(8).Bold().FontColor(GreenMuted).FontFamily("Arial");
                                    t.Item().PaddingTop(4).Text(model.Title)
                                        .FontSize(13).Bold().FontColor(TextDark).FontFamily("Georgia");
                                });
                        }

                        // Two-column meta grid
                        body.Item()
                            .BorderBottom(0.5f).BorderColor(GreenFaint)
                            .Row(grid =>
                            {
                                grid.RelativeItem()
                                    .BorderRight(0.5f).BorderColor(GreenFaint)
                                    .Padding(10).PaddingLeft(14).Column(c =>
                                    {
                                        c.Item().Text("Type".ToUpper())
                                            .FontSize(8).Bold().FontColor(GreenMuted).FontFamily("Arial");
                                        c.Item().PaddingTop(3).Text(TypeLabel)
                                            .FontSize(11).Bold().FontColor(GreenDark).FontFamily("Arial");
                                    });
                            });

                        // Sections
                        if (model.Applicants?.Count > 0)
                            body.Item().Element(c => ComposePeopleSection(c, "Applicants", model.Applicants));

                        if (model.Inventors?.Count > 0)
                            body.Item().Element(c => ComposePeopleSection(c, CreatorLabel, model.Inventors));

                        if (model.Correspondence is not null)
                            body.Item().Element(c => ComposeCorrespondence(c, model.Correspondence));

                        if (type == FileTypes.Design && model.ImagesUrl?.Count > 0)
                            body.Item().Element(c => ComposeImages(c, model.ImagesUrl));
                    });
                });
        }

        // ── Section bar ───────────────────────────────────────────────────────
        void SectionBar(ColumnDescriptor col, string label)
        {
            col.Item()
                .Background(GreenLight)
                .BorderTop(0.5f).BorderColor(GreenBorder)
                .BorderBottom(0.5f).BorderColor(GreenBorder)
                .Padding(6).PaddingLeft(14)
                .Row(r =>
                {
                    r.ConstantItem(3).Background(GreenDark);
                    r.RelativeItem().PaddingLeft(8).AlignMiddle()
                        .Text(label.ToUpper())
                        .FontSize(8).Bold().FontColor(GreenDark).FontFamily("Arial");
                });
        }

        // ── People section ────────────────────────────────────────────────────
        void ComposePeopleSection(IContainer container, string heading, List<ApplicantInfo> people)
        {
            container.Column(col =>
            {
                SectionBar(col, heading);
                foreach (var person in people)
                {
                    if (string.IsNullOrWhiteSpace(person.Name))
                        continue;

                    col.Item()
                        .BorderBottom(0.5f).BorderColor(GreenFaint)
                        .Padding(10).PaddingLeft(14).PaddingRight(14)
                        .Row(row =>
                        {
                            row.ConstantItem(34).AlignTop().PaddingTop(1)
                                .Width(30).Height(30)
                                .Border(1.5f).BorderColor(AvatarBorder)
                                .Background(AvatarBg)
                                .AlignCenter().AlignMiddle()
                                .Text(Initials(person.Name))
                                .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");

                            row.RelativeItem().PaddingLeft(10).Column(info =>
                            {
                                info.Item().Text(person.Name)
                                    .FontSize(11).Bold().FontColor(TextDark).FontFamily("Arial");

                                var hasPhone = !string.IsNullOrWhiteSpace(person.Phone);
                                var hasEmail = !string.IsNullOrWhiteSpace(person.Email);

                                if (hasPhone || hasEmail)
                                {
                                    info.Item().PaddingTop(2).Text(text =>
                                    {
                                        if (hasPhone)
                                            text.Span(person.Phone!)
                                                .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");

                                        if (hasPhone && hasEmail)
                                            text.Span("  ·  ").FontSize(9).FontColor(GreenBorder).FontFamily("Arial");

                                        if (hasEmail)
                                            text.Span(person.Email!).FontSize(9).FontColor(GreenDark).FontFamily("Arial");
                                    });
                                }

                                var address = FormatAddress(person.Address, person.country);
                                if (!string.IsNullOrWhiteSpace(address))
                                {
                                    info.Item().PaddingTop(1)
                                        .Text(address)
                                        .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                                }
                            });
                        });
                }
            });
        }

        // ── Correspondence section ────────────────────────────────────────────
        void ComposeCorrespondence(IContainer container, CorrespondenceType corr)
        {
            if (string.IsNullOrWhiteSpace(corr.name))
                return;

            container.Column(col =>
            {
                SectionBar(col, "Correspondence");
                col.Item()
                    .Padding(10).PaddingLeft(14).PaddingRight(14)
                    .Row(row =>
                    {
                        row.ConstantItem(34).AlignTop().PaddingTop(1)
                            .Width(30).Height(30)
                            .Border(1.5f).BorderColor(AvatarBorder)
                            .Background(AvatarBg)
                            .AlignCenter().AlignMiddle()
                            .Text(Initials(corr.name))
                            .FontSize(9).Bold().FontColor(GreenDark).FontFamily("Arial");

                        row.RelativeItem().PaddingLeft(10).Column(info =>
                        {
                            info.Item()
                                .Text($"{corr.name}{(string.IsNullOrWhiteSpace(corr.state) ? "" : $"  ·  {corr.state}")}")
                                .FontSize(11).Bold().FontColor(TextDark).FontFamily("Arial");

                            var hasPhone = !string.IsNullOrWhiteSpace(corr.phone);
                            var hasEmail = !string.IsNullOrWhiteSpace(corr.email);

                            if (hasPhone || hasEmail)
                            {
                                info.Item().PaddingTop(2).Text(text =>
                                {
                                    if (hasPhone)
                                        text.Span(corr.phone!).FontSize(9).FontColor(GreenMuted).FontFamily("Arial");

                                    if (hasPhone && hasEmail)
                                        text.Span("  ·  ").FontSize(9).FontColor(GreenBorder).FontFamily("Arial");

                                    if (hasEmail)
                                        text.Span(corr.email!).FontSize(9).FontColor(GreenDark).FontFamily("Arial");
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(corr.address))
                                info.Item().PaddingTop(1).Text(corr.address)
                                    .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                        });
                    });
            });
        }

        // ── Design images ─────────────────────────────────────────────────────
        void ComposeImages(IContainer container, List<byte[]> images)
        {
            container.Column(col =>
            {
                SectionBar(col, "Design Representations");
                col.Item()
                    .Padding(12).PaddingLeft(14).PaddingRight(14)
                    .Row(imgRow =>
                    {
                        imgRow.Spacing(10);
                        foreach (var bytes in images)
                        {
                            var img = Image.FromBinaryData(bytes);
                            imgRow.ConstantItem(95)
                                .Border(0.5f).BorderColor(GreenBorder)
                                .Background(GreenLight)
                                .Height(95)
                                .Image(img).FitArea();
                        }
                    });
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string Initials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[^1][0]}".ToUpper()
                : name[..Math.Min(2, name.Length)].ToUpper();
        }

        private static string FormatAddress(string? address, string? country)
        {
            return string.Join("  ·  ",
                new[] { address, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}
