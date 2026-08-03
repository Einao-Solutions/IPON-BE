using patentdesign.Models;
using patentdesign.Utils;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Tfunctions.pdfs
{
    public class JournalDocumentNewspaper(
        List<PublicationInfo> models,
        FileTypes type,
        DateTime end, int vol, int num) : IDocument
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

        // ── Computed groups (shared across TOC + body) ────────────────────────
        private List<(string SectionId, string Label, string? Description, List<PublicationInfo> Items)>? _groups;
        private List<(string SectionId, string Label, string? Description, List<PublicationInfo> Items)> Groups
            => _groups ??= models
                .GroupBy(m => m.Class)
                .OrderBy(g => g.Key)
                .Select(g => (
                    SectionId: $"class-{g.Key?.ToString() ?? "none"}",
                    Label: g.Key.HasValue ? $"Class {g.Key}" : "Unclassified",
                    Description: g.First().ClassDescription,
                    Items: g.ToList()))
                .ToList();

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
            // Cover page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Content().Element(ComposeCoverPage);
            });

            // Statutory provisions page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeLegalNotice);
                page.Footer().Element(c => ComposeFooter(c, "Statutory Provisions"));
            });

            // Table of Contents page
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeTableOfContents);
                page.Footer().Element(c => ComposeFooter(c, "Table of Contents"));
            });

            // One page-section per class group
            foreach (var group in Groups)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Header().Element(ComposeHeader);
                    page.Content().Element(c => ComposeClassBody(c, group.SectionId, group.Label, group.Description, group.Items));
                    page.Footer().Element(c => ComposeFooter(c, group.Label));
                });
            }
        }

        // ── Cover Page ────────────────────────────────────────────────────────
        void ComposeCoverPage(IContainer container)
        {
            // A4 height ≈ 842pt. Green takes ~70%; the white section fills the
            // remainder so the divider band doesn't push content onto a new page.
            const float pageHeight = 842f;
            const float greenHeight = pageHeight * 0.70f;

            container.Column(page =>
            {
                // ── Top ~70%: Green background ────────────────────────────────
                page.Item().Height(greenHeight).Background(GreenDark)
                    .PaddingVertical(70).PaddingHorizontal(60)
                    .Column(top =>
                    {
                        top.Item().AlignCenter()
                            .Width(100).Height(100)
                            .Image("assets/logo.png")
                            .FitArea();

                        top.Item().PaddingTop(24).AlignCenter()
                            .Text("TRADEMARKS")
                            .FontSize(46)
                            .ExtraBlack()                  
                            .FontColor(White)
                            .FontFamily("Arial Black"); 
                        top.Item().AlignCenter()
                            .Text("JOURNAL")
                            .FontSize(46)
                            .ExtraBlack()                  
                            .FontColor(White)
                            .FontFamily("Arial Black");
                        top.Item().ExtendVertical().AlignMiddle().Column(center =>
                        {
                            center.Item().PaddingBottom(24).PaddingHorizontal(120)
                                .LineHorizontal(2).LineColor(White);

                            center.Item().AlignCenter()
                                .Text($"Vol. 1 No. 1")
                                .FontSize(28).Bold().FontColor(White).FontFamily("Georgia");
                        });
                    });

                // ── Accent divider with end date (natural height) ─────────────
                page.Item().Background(GreenBorder).PaddingVertical(12).AlignCenter()
                    .Text($"{end:d MMMM, yyyy}")
                    .FontSize(18).Bold().FontColor(GreenDark).FontFamily("Arial");

                // ── Remaining ~30%: White background (use Extend, not Height) ─
                page.Item().Extend().Background(GreenFaint).PaddingHorizontal(60)
                    .Column(bottom =>
                    {
                        bottom.Item().ExtendVertical().AlignMiddle().Column(center =>
                        {
                            center.Item().AlignCenter()
                               .Text("Published by:")
                               .FontSize(13).FontColor(Colors.Red.Darken4).FontFamily("Arial").SemiBold();

                            center.Item().AlignCenter()
                                .Text("The Trademarks Registry")
                                .FontSize(13).FontColor(GreenDark).FontFamily("Arial").SemiBold();

                            center.Item().PaddingTop(6).AlignCenter()
                                .Text("Federal Ministry of Industry, Trade & Investment")
                                .FontSize(11).FontColor(TextDark).FontFamily("Arial").SemiBold();

                            center.Item().PaddingTop(2).AlignCenter()
                                .Text("Abuja, Nigeria")
                                .FontSize(11).FontColor(TextDark).FontFamily("Arial").SemiBold();
                        });
                    });
            });
        }

        // ── Legal / Statutory Provisions Page ─────────────────────────────────
        void ComposeLegalNotice(IContainer container)
        {
            container.Background(White).Padding(40).PaddingHorizontal(50).Column(col =>
            {
                col.Item().AlignCenter()
                    .Width(90).Height(90)
                    .Image("assets/logo.png")
                    .FitArea();
                col.Item().AlignCenter()
                    .Text("LAW OF THE FEDERATION OF NIGERIA, 1990")
                    .FontSize(14).ExtraBold().FontColor(GreenDark).FontFamily("Georgia");

                col.Item().PaddingTop(4).AlignCenter()
                    .Text("CHAPTER 436")
                    .FontSize(12).ExtraBold().FontColor(GreenDark).FontFamily("Georgia");

                col.Item().PaddingTop(4).AlignCenter()
                    .Text("TRADEMARKS ACT")
                    .FontSize(12).ExtraBold().FontColor(GreenDark).FontFamily("Georgia");

                // Decorative divider
                col.Item().PaddingTop(16).PaddingBottom(16)
                    .PaddingHorizontal(80)
                    .LineHorizontal(2).LineColor(GreenDark);

                // Section heading
                col.Item().AlignCenter()
                    .Text("APPLICATION FOR REGISTRATION OF TRADE MARKS")
                    .FontSize(11).Bold().FontColor(TextDark).FontFamily("Arial");

                // Paragraph 1
                col.Item().PaddingTop(20)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("Pursuant to section 19 of the Trademarks Act, notice is hereby given that " +
                                  "applications have been received for registration of the following Trade Marks.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });

                // Paragraph 2
                col.Item().PaddingTop(14)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("Any person who has grounds of opposition to the registration of any of the " +
                                  "marks advertised shall within two months from the date hereof give notice to " +
                                  "the Registrar of such opposition.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });

                // Paragraph 3
                col.Item().PaddingTop(14)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("Such notice shall be in writing and in the prescribed manner setting out the " +
                                  "ground and shall be submitted in duplicate.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });

                // Paragraph 4
                col.Item().PaddingTop(14)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("On completion of the preliminary proceeding, both the opposition and the " +
                                  "application shall within one month, file with the registrar, their briefs of " +
                                  "argument with copies of the legal authorities relied upon therein.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });

                // Paragraph 5
                col.Item().PaddingTop(14)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("All communication relating to Trade Marks should be addressed to the " +
                                  "Registrar of Trade Marks, Ministry of Trade and Investment P. M. B 88, " +
                                  "Garki Abuja Nigeria.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });

                // Paragraph 6 — class range
                col.Item().PaddingTop(14)
                    .Text(text =>
                    {
                        text.Justify();
                        text.Span("The goods in respect of the following Trade Marks are in classes 1–34 of " +
                                  "schedule 4, and service marks in classes 35–45.")
                            .FontSize(10).FontColor(TextBody).FontFamily("Georgia");
                    });
            });
        }

        // ── Table of Contents ─────────────────────────────────────────────────
        void ComposeTableOfContents(IContainer container)
        {
            container.Background(White).Padding(24).Column(col =>
            {
                // TOC title
                col.Item()
                    .PaddingBottom(16)
                    .BorderBottom(3).BorderColor(GreenDark)
                    .PaddingBottom(10)
                    .Text("Table of Contents")
                    .FontSize(18).Bold().FontColor(GreenDark).FontFamily("Georgia");

                col.Item().Column(rows =>
                {
                    // Column headers
                    rows.Item()
                        .PaddingBottom(8)
                        .BorderBottom(1).BorderColor(GreenBorder)
                        .Row(headerRow =>
                        {
                            headerRow.ConstantItem(60)
                                .Text("Classes")
                                .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");

                            headerRow.ConstantItem(45).AlignRight()
                                .Text("Pages")
                                .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");

                            headerRow.RelativeItem().PaddingLeft(10)
                                .Text("Description")
                                .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");
                        });

                    foreach (var group in Groups)
                    {
                        var desc = group.Description ?? "";
                        var maxLen = 99;
                        string truncatedDesc;
                        if (desc.Length > maxLen)
                        {
                            var lastSpace = desc.LastIndexOf(' ', maxLen);
                            var cutoff = lastSpace > 0 ? lastSpace : maxLen;
                            truncatedDesc = $"{desc[..cutoff].TrimEnd()} etc.";
                        }
                        else
                        {
                            truncatedDesc = desc;
                        }
                        rows.Item()
                            .PaddingTop(2).PaddingBottom(2)
                            .BorderBottom(0.5f).BorderColor(GreenFaint)
                            .SectionLink(group.SectionId)
                            .Row(row =>
                            {
                                // Class label
                                row.ConstantItem(60).AlignMiddle()
                                    .Text(group.Label)
                                    .FontSize(11).Bold().FontColor(TextDark).FontFamily("Georgia");

                                // Page range
                                row.ConstantItem(45).AlignRight().AlignMiddle()
                                    .Text(text =>
                                    {
                                        text.BeginPageNumberOfSection(group.SectionId)
                                            .FontSize(10).FontColor(GreenDark).FontFamily("Arial");
                                        text.Span(" – ")
                                            .FontSize(10).FontColor(GreenMuted).FontFamily("Arial");
                                        text.EndPageNumberOfSection(group.SectionId)
                                            .FontSize(10).FontColor(GreenDark).FontFamily("Arial");
                                    });

                                // Class description (takes remaining space)
                                row.RelativeItem().PaddingLeft(10).AlignMiddle()
                                    .Text(truncatedDesc)
                                    .FontSize(9).FontColor(TextBody).FontFamily("Arial");
                            });
                    }

                    // Summary
                    rows.Item()
                        .PaddingTop(16)
                        .BorderTop(2).BorderColor(GreenDark)
                        .PaddingTop(8)
                        .Row(summary =>
                        {
                            summary.RelativeItem()
                                .Text($"Total: {Groups.Count} classes, {models.Count} publications")
                                .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");
                        });
                });
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
                        .Padding(18).PaddingLeft(20).PaddingRight(18)
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
                                wm.Item().Text("Intellectual Property Office Nigeria")
                                    .FontSize(13).Bold().FontColor(GreenDark).FontFamily("Georgia");
                                wm.Item().PaddingTop(2)
                                    .Text("Trademarks Registry")
                                    .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                                wm.Item().PaddingTop(2)
                                    .Text("Federal Ministry of Industry, Trade & Investment")
                                    .FontSize(8).FontColor(GreenMuted).FontFamily("Arial");
                                
                            });

                            // Document meta
                            inner.ConstantItem(210).AlignMiddle().Column(meta =>
                            {
                                meta.Item().AlignRight()
                                    .Text($"{TypeLabel} Publication Journal")
                                    .FontSize(10).Bold().FontColor(GreenDark).FontFamily("Arial");
                                //meta.Item().PaddingTop(3).AlignRight()
                                //    .Text($"{start:d MMMM yyyy} — {end:d MMMM yyyy}")
                                //    .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                                meta.Item().PaddingTop(2).AlignRight()
                                    .Text($"Generated: {DateTime.Now:d MMM yyyy}")
                                    .FontSize(8).FontColor(GreenBorder).FontFamily("Arial");
                            });
                        });
                });
        }

        // ── Page footer ───────────────────────────────────────────────────────
        void ComposeFooter(IContainer container, string classLabel)
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
                            .Text($"iponigeria.com  ·  Commercial Law Department  ·  {classLabel}")
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

        // ── Body for a single class group ─────────────────────────────────────
        void ComposeClassBody(IContainer container, string sectionId, string classLabel, string? classDesc, List<PublicationInfo> groupItems)
        {
            container.Background(White).Padding(24)
                .Section(sectionId)
                .Column(col =>
                {
                    // Class header
                    col.Item()
                        .BorderBottom(2).BorderColor(GreenDark)
                        .PaddingBottom(6)
                        .Column(header =>
                        {
                            header.Item()
                                .Text(classLabel)
                                .FontSize(13).Bold().FontColor(GreenDark).FontFamily("Georgia");

                            if (!string.IsNullOrWhiteSpace(classDesc))
                            {
                                header.Item().PaddingTop(2)
                                    .Text(classDesc)
                                    .FontSize(9).FontColor(GreenMuted).FontFamily("Arial");
                            }
                        });

                    // Distribute group items across two columns
                    var midpoint = (groupItems.Count + 1) / 2;
                    var leftColumn = groupItems.Take(midpoint).ToList();
                    var rightColumn = groupItems.Skip(midpoint).ToList();

                    col.Item().PaddingTop(12)
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